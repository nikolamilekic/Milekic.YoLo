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
        !! "src/Milekic.YoLo/obj/Release/**/Milekic.YoLo.AssemblyInfo.fs"
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

module ZipArtifacts =
    //nuget Fake.IO.FileSystem
    //nuget Fake.IO.Zip

    open Fake.IO
    open Fake.IO.Globbing.Operators

    let zipArtifacts =
        lazy
            let files = (!! "publish/**") |> Seq.toList
            printfn "Creating zip file from files:\n%A" files
            Zip.zip
                "publish"
                "publish/artifacts.zip"
                files
        |> fun x -> fun () -> x.Value

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
    // nuget Fake.DotNet.Cli

    open Fake.DotNet
    open Fake.Core

    open CustomTargetOperators

    let projectToBuild = "Milekic.YoLo.sln"

    Target.create "Build" <| fun _ -> DotNet.build id projectToBuild

    [ "Clean" ]  ?=> "Build"

module Pack =
    //nuget Fake.DotNet.Cli

    open Fake.DotNet
    open Fake.Core

    open CustomTargetOperators
    open ReleaseNotesParsing

    let projectToPack = "Milekic.YoLo.sln"

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

    open System.IO
    open Fake.DotNet
    open Fake.Core
    open Fake.IO
    open Fake.IO.FileSystemOperators
    open FSharpPlus

    open CustomTargetOperators

    let projectsToPublish = [ "src/Milekic.YoLo", Some "netstandard2.0", Some "osx-x64", None]

    Target.create "Publish" <| fun _ ->
        for (project, framework, runtime, custom) in projectsToPublish do
            project
            |> DotNet.publish (fun p ->
                { p with
                    Framework = framework
                    Runtime = runtime
                    Common = { p.Common with CustomParams = custom } } )

            let source =
                seq {
                    project |> Some
                    "bin/Release" |> Some
                    framework
                    runtime
                    "publish" |> Some
                }
                |> Seq.choose id
                |> Seq.fold (</>) ""

            let target =
                seq {
                    "publish" |> Some
                    Path.GetFileName project |> Some
                    framework
                    runtime
                }
                |> Seq.choose id
                |> Seq.fold (</>) ""

            Shell.copyDir target source (konst true)

    [ "Clean" ] ==> "Publish"

module Test =
    //nuget FSharpPlus
    //nuget Fake.IO.FileSystem
    //nuget Fake.DotNet.Testing.Expecto

    open System.IO
    open Fake.Core
    open Fake.IO.Globbing.Operators
    open Fake.DotNet.Testing
    open FSharpPlus

    open CustomTargetOperators

    Target.create "Test" <| fun _ ->
        !! "tests/**/*.fsproj"
        |> toSeq
        >>= fun projectPath ->
            let projectName = Path.GetFileNameWithoutExtension projectPath
            !! (sprintf "tests/%s/bin/release/**/%s.dll" projectName projectName)
            |> toSeq
        |> Expecto.run id
    [ "Build"; "Pack" ] ?=> "Test"

module TestSourceLink =
    //nuget FSharpPlus
    //nuget Fake.IO.FileSystem
    //nuget Fake.DotNet.Cli

    open Fake.Core
    open Fake.IO.Globbing.Operators
    open Fake.DotNet
    open FSharpPlus

    open CustomTargetOperators

    Target.create "TestSourceLink" <| fun _ ->
        !! "publish/*.nupkg"
        |> flip Seq.iter <| fun p ->
            DotNet.exec
                id
                "sourcelink"
                (sprintf "test %s" p)
            |> fun r -> if not r.OK then failwithf "Source link check for %s failed." p

    [ "Pack" ] ==> "TestSourceLink"

module UploadArtifactsToGitHub =
    //nuget Fake.Api.GitHub
    //nuget Fake.IO.FileSystem
    //nuget Fake.BuildServer.AppVeyor

    open Fake.Core
    open Fake.Api
    open Fake.IO.Globbing.Operators
    open Fake.BuildServer

    open CustomTargetOperators
    open FinalVersion
    open ReleaseNotesParsing
    open ZipArtifacts

    let productName = "Milekic.YoLo"
    let gitOwner = "nikolamilekic"

    Target.create "UploadArtifactsToGitHub" <| fun c ->
        let finalVersion = finalVersion.Value
        if c.Context.FinalTarget = "AppVeyor" && finalVersion.PreRelease.IsSome
        then ()
        else

        zipArtifacts()

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
        |> GitHub.uploadFiles (!! "publish/**/*.nupkg" ++ "publish/artifacts.zip")
        |> GitHub.publishDraft
        |> Async.RunSynchronously

    [ "Pack"; "Publish"; "Test"; "TestSourceLink" ] ==> "UploadArtifactsToGitHub"

module UploadPackageToNuget =
    //nuget Fake.DotNet.Paket

    open Fake.Core
    open Fake.DotNet

    open FinalVersion
    open CustomTargetOperators

    Target.create "UploadPackageToNuget" <| fun _ ->
        if finalVersion.Value.PreRelease.IsNone then
            Paket.push <| fun p ->
                { p with
                    ToolType = ToolType.CreateLocalTool()
                    WorkingDir = __SOURCE_DIRECTORY__ + "/publish" }

    [ "Pack"; "Test"; "TestSourceLink" ] ==> "UploadPackageToNuget"

module Release =
    //nuget Fake.Tools.Git

    open Fake.Core
    open Fake.Tools

    open CustomTargetOperators

    let gitHome = "git@github.com:nikolamilekic/Milekic.YoLo.git"

    Target.create "Release" <| fun _ ->
        Git.CommandHelper.directRunGitCommandAndFail
            ""
            (sprintf "push -f %s HEAD:release" gitHome)

    [ "Clean"; "Build"; "Test" ] ==> "Release"

module AppVeyor =
    //nuget Fake.BuildServer.AppVeyor

    open Fake.Core
    open Fake.BuildServer

    open CustomTargetOperators
    open FinalVersion
    open ZipArtifacts

    Target.create "SetAppVeyorVersion" <| fun _ ->
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

    [ "SetAppVeyorVersion" ] ==> "UploadPackageToNuget"
    [ "SetAppVeyorVersion" ] ==> "UploadArtifactsToGitHub"
    [ "Build"; "Publish" ] ?=> "SetAppVeyorVersion"

    Target.create "AppVeyor" (ignore >> zipArtifacts)
    [
        "UploadArtifactsToGitHub"
        "UploadPackageToNuget"
        "Pack"
        "Publish"
        "Test"
        "TestSourceLink"
        "SetAppVeyorVersion"
    ]
    ==> "AppVeyor"

module Default =
    open Fake.Core

    open CustomTargetOperators

    Target.create "Default" ignore
    [ "Build"; "Test" ] ==> "Default"

    Target.runOrDefault "Default"
