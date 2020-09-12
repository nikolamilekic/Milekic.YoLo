#load ".fake/build.fsx/intellisense.fsx"

Fake.Core.Target.initEnvironment ()

module CustomTargetOperators =
    //nuget Fake.Core.Target

    open Fake.Core.TargetOperators

    let (==>) xs y = xs |> Seq.iter (fun x -> x ==> y |> ignore)
    let (?=>) xs y = xs |> Seq.iter (fun x -> x ?=> y |> ignore)

module FinalVersion =
    //nuget Fake.IO.FileSystem

    open System.Text.RegularExpressions
    open Fake.IO
    open Fake.IO.Globbing.Operators
    open Fake.Core

    let pathToAssemblyInfoFile =
        lazy
        !! "src/*/obj/Release/**/*.AssemblyInfo.fs"
        |> Seq.head

    let (|Regex|_|) pattern input =
        let m = Regex.Match(input, pattern)
        if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ])
        else None

    let finalVersion =
        lazy
        pathToAssemblyInfoFile.Value
        |> File.readAsString
        |> function
            | Regex "AssemblyInformationalVersionAttribute\(\"(.+)\"\)>]" [ version ] ->
                SemVer.parse version
            | _ -> failwith "Could not parse assembly version"

module ReleaseNotesParsing =
    //nuget Fake.Core.ReleaseNotes

    open System
    open Fake.Core

    let releaseNotesFile = "RELEASE_NOTES.md"
    let releaseNotes =
        lazy
        (ReleaseNotes.load releaseNotesFile).Notes
        |> String.concat Environment.NewLine

module Clean =
    //nuget FSharpPlus
    //nuget Fake.IO.FileSystem

    open Fake.IO
    open Fake.IO.Globbing.Operators
    open Fake.Core
    open FSharpPlus

    Target.create "Clean" <| fun _ ->
        lift2 tuple2 [|"src"; "tests"|] [|"bin"; "obj"|]
        >>= fun (x,y) -> !!(sprintf "%s/**/%s" x y) |> toSeq
        |> plus ([|"bin"; "obj"|] |> toSeq)
        |> Shell.deleteDirs

        Shell.cleanDir "publish"

module Build =
    //nuget Fake.DotNet.Cli
    //nuget Fake.BuildServer.AppVeyor
    //nuget Fake.IO.FileSystem

    open Fake.DotNet
    open Fake.Core
    open Fake.Core.TargetOperators
    open Fake.BuildServer
    open Fake.IO.Globbing.Operators

    open FinalVersion

    let projectToBuild = !! "*.sln" |> Seq.head

    Target.create "Build" <| fun _ ->
        DotNet.build id projectToBuild

        if AppVeyor.detect() then
            let finalVersion = finalVersion.Value
            let appVeyorVersion =
                sprintf
                    "%d.%d.%d.%s"
                    finalVersion.Major
                    finalVersion.Minor
                    finalVersion.Patch
                    AppVeyor.Environment.BuildNumber

            AppVeyor.updateBuild (fun p -> { p with Version = appVeyorVersion })

    "Clean" ?=> "Build"

module Pack =
    //nuget Fake.DotNet.Cli
    //nuget Fake.IO.FileSystem

    open Fake.DotNet
    open Fake.Core
    open Fake.IO.Globbing.Operators

    open CustomTargetOperators
    open ReleaseNotesParsing

    let projectToPack = !! "*.sln" |> Seq.head

    Target.create "Pack" <| fun _ ->
        let newBuildProperties = [ "PackageReleaseNotes", releaseNotes.Value ]
        DotNet.pack
            (fun p ->
                { p with
                    OutputPath = Some (__SOURCE_DIRECTORY__ + "/publish")
                    NoBuild = true
                    MSBuildParams =
                        { p.MSBuildParams with
                            Properties =
                                newBuildProperties @ p.MSBuildParams.Properties }})
            projectToPack

    [ "Clean"; "Build" ] ==> "Pack"

module Publish =
    //nuget FSharpPlus
    //nuget Fake.DotNet.Cli
    //nuget Fake.IO.FileSystem
    //nuget Fake.IO.Zip

    open System.IO
    open Fake.DotNet
    open Fake.Core
    open Fake.IO
    open Fake.IO.Globbing.Operators
    open Fake.IO.FileSystemOperators
    open FSharpPlus

    open FinalVersion
    open CustomTargetOperators

    let projectsToPublish = [ ]

    Target.create "Publish" <| fun _ ->
        for (project, framework, runtime, custom) in projectsToPublish do
            project
            |> DotNet.publish (fun p ->
                { p with
                    Framework = framework
                    Runtime = runtime
                    Common = { p.Common with CustomParams = custom } } )

            let sourceFolder =
                seq {
                    project |> Some
                    "bin/Release" |> Some
                    framework
                    runtime
                    "publish" |> Some
                }
                |> Seq.choose id
                |> Seq.fold (</>) ""

            let targetFolder =
                seq {
                    "publish" |> Some
                    Path.GetFileName project |> Some
                    framework
                    runtime
                }
                |> Seq.choose id
                |> Seq.fold (</>) ""

            Shell.copyDir targetFolder sourceFolder (konst true)

            let zipFileName =
                seq {
                    sprintf
                        "%s.%s"
                        (Path.GetFileName project)
                        (finalVersion.Value.NormalizeToShorter()) |> Some
                    framework
                    runtime
                }
                |> Seq.choose id
                |> String.concat "."

            Zip.zip
                targetFolder
                (sprintf "publish/%s.zip" zipFileName)
                !! (targetFolder </> "**")

    [ "Clean"; "Build" ] ==> "Publish"

module Test =
    //nuget FSharpPlus
    //nuget Fake.IO.FileSystem
    //nuget Fake.DotNet.Testing.Expecto

    open System.IO
    open Fake.Core
    open Fake.Core.TargetOperators
    open Fake.IO.Globbing.Operators
    open Fake.DotNet.Testing
    open FSharpPlus

    Target.create "Test" <| fun _ ->
        !! "tests/**/*.fsproj"
        |> toSeq
        >>= fun projectPath ->
            let projectName = Path.GetFileNameWithoutExtension projectPath
            !! (sprintf "tests/%s/bin/release/**/%s.dll" projectName projectName)
            |> toSeq
        |> Expecto.run id
    "Build" ==> "Test"

module TestSourceLink =
    //nuget FSharpPlus
    //nuget Fake.IO.FileSystem
    //nuget Fake.DotNet.Cli

    open Fake.Core
    open Fake.Core.TargetOperators
    open Fake.IO.Globbing.Operators
    open Fake.DotNet
    open FSharpPlus

    Target.create "TestSourceLink" <| fun _ ->
        !! "publish/*.nupkg"
        |> flip Seq.iter <| fun p ->
            DotNet.exec
                id
                "sourcelink"
                (sprintf "test %s" p)
            |> fun r -> if not r.OK then failwithf "Source link check for %s failed." p

    "Pack" ==> "TestSourceLink"

module UploadArtifactsToGitHub =
    //nuget Fake.Api.GitHub
    //nuget Fake.IO.FileSystem
    //nuget Fake.BuildServer.AppVeyor

    open System.IO
    open Fake.Core
    open Fake.Api
    open Fake.IO.Globbing.Operators
    open Fake.BuildServer

    open CustomTargetOperators
    open FinalVersion
    open ReleaseNotesParsing

    let productName = !! "*.sln" |> Seq.head |> Path.GetFileNameWithoutExtension
    let gitOwner = "nikolamilekic"

    Target.create "UploadArtifactsToGitHub" <| fun _ ->
        let finalVersion = finalVersion.Value
        if AppVeyor.detect() && finalVersion.PreRelease.IsNone then
            let token = Environment.environVarOrFail "GitHubToken"
            GitHub.createClientWithToken token
            |> GitHub.createRelease
                gitOwner
                productName
                (finalVersion.NormalizeToShorter())
                (fun o ->
                    { o with
                        Body = releaseNotes.Value
                        Prerelease = (finalVersion.PreRelease <> None)
                        TargetCommitish = AppVeyor.Environment.RepoCommit })
            |> GitHub.uploadFiles (!! "publish/*.nupkg" ++ "publish/*.zip")
            |> GitHub.publishDraft
            |> Async.RunSynchronously

    [ "Pack"; "Publish"; "Test"; "TestSourceLink" ] ==> "UploadArtifactsToGitHub"

module UploadPackageToNuget =
    //nuget Fake.DotNet.Paket
    //nuget Fake.BuildServer.AppVeyor

    open Fake.Core
    open Fake.DotNet
    open Fake.BuildServer

    open FinalVersion
    open CustomTargetOperators

    Target.create "UploadPackageToNuget" <| fun _ ->
        if AppVeyor.detect() && finalVersion.Value.PreRelease.IsNone then
            Paket.push <| fun p ->
                { p with
                    ToolType = ToolType.CreateLocalTool()
                    WorkingDir = __SOURCE_DIRECTORY__ + "/publish" }

    [ "Pack"; "Test"; "TestSourceLink" ] ==> "UploadPackageToNuget"

module Release =
    //nuget Fake.Tools.Git

    open System.Text.RegularExpressions
    open Fake.IO
    open Fake.IO.Globbing.Operators
    open Fake.Core
    open Fake.Tools

    open CustomTargetOperators

    let pathToThisAssemblyFile =
        lazy
        !! "src/*/obj/Release/**/ThisAssembly.GitInfo.g.fs"
        |> Seq.head

    let (|Regex|_|) pattern input =
        let m = Regex.Match(input, pattern)
        if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ])
        else None

    let gitHome =
        lazy
        pathToThisAssemblyFile.Value
        |> File.readAsString
        |> function
            | Regex "RepositoryUrl = @\"(.+)\"" [ gitHome ] -> gitHome
            | _ -> failwith "Could not parse this assembly file"

    Target.create "Release" <| fun _ ->
        Git.CommandHelper.directRunGitCommandAndFail
            ""
            (sprintf "push -f %s HEAD:release" gitHome.Value)

    [ "Clean"; "Build"; "Test" ] ==> "Release"

module AppVeyor =
    open Fake.Core

    open CustomTargetOperators

    Target.create "AppVeyor" ignore
    [ "UploadArtifactsToGitHub"; "UploadPackageToNuget" ]
    ==> "AppVeyor"

module Default =
    open Fake.Core

    open CustomTargetOperators

    Target.create "Default" ignore
    [ "Build"; "Test" ] ==> "Default"

    Target.runOrDefault "Default"
