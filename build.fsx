#load ".fake/build.fsx/intellisense.fsx"

module CustomTargetOperators =
    //nuget Fake.Core.Target

    open Fake.Core.TargetOperators

    let (==>) xs y = xs |> Seq.iter (fun x -> x ==> y |> ignore)
    let (?=>) xs y = xs |> Seq.iter (fun x -> x ?=> y |> ignore)

module FinalVersion =
    //nuget Fake.IO.FileSystem
    //nuget Fake.Core.SemVer

    open System.Text.RegularExpressions
    open Fake.IO
    open Fake.IO.Globbing.Operators
    open Fake.Core

    let (|Regex|_|) pattern input =
        let m = Regex.Match(input, pattern)
        if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ])
        else None

    let getFinalVersionFromAssemblyInfo x =
        match File.readAsString x with
        | Regex "AssemblyInformationalVersionAttribute\(\"(.+)\"\)" [ version ] ->
            Some (SemVer.parse version)
        | _ -> None

    let getFinalVersionForProject project =
        let assemblyInfoFileGlob =
            [ Path.getDirectory project; "obj/Release/**/*.AssemblyInfo.?s" ]
            |> List.fold Path.combine ""
        !!assemblyInfoFileGlob
        |> Seq.tryHead
        |> Option.bind getFinalVersionFromAssemblyInfo

    let finalVersion =
        lazy
        !! "src/*/obj/Release/**/*.AssemblyInfo.?s"
        |> Seq.head
        |> getFinalVersionFromAssemblyInfo
        |> Option.defaultWith (fun _ -> failwith "Could not parse assembly version")

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
    //nuget Fake.IO.FileSystem

    open Fake.IO
    open Fake.IO.Globbing.Operators
    open Fake.Core

    Target.create "Clean" <| fun _ ->
        Seq.allPairs [|"src"; "tests"|] [|"bin"; "obj"|]
        |> Seq.collect (fun (x, y) -> !! $"{x}/**/{y}")
        |> Seq.append [ "publish"; "testResults" ]
        |> Shell.deleteDirs

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
                $"{finalVersion.Major}.{finalVersion.Minor}.{finalVersion.Patch}.{AppVeyor.Environment.BuildNumber}"

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
    //nuget Fake.DotNet.Cli
    //nuget Fake.IO.FileSystem
    //nuget Fake.IO.Zip

    open System.IO
    open System.Text.RegularExpressions
    open Fake.DotNet
    open Fake.Core
    open Fake.IO
    open Fake.IO.Globbing.Operators
    open Fake.IO.FileSystemOperators

    open FinalVersion
    open CustomTargetOperators

    let (|Regex|_|) pattern input =
        let m = Regex.Match(input, pattern)
        if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ])
        else None

    let projectsToPublish = !!"src/*/*.?sproj"
    let runtimesToTarget = [ "osx-x64"; "win-x64"; "linux-arm"; "linux-x64" ]

    Target.create "Publish" <| fun _ ->
        let projectsToPublish = query {
            for _project in projectsToPublish do
            let _projectContents = File.readAsString _project
            let _outputType =
                match _projectContents with
                | Regex "<OutputType>(.+)<\/OutputType>" [ outputType ] ->
                    Some (outputType.ToLower())
                | _ -> None
            let _projectType =
                match _projectContents with
                | Regex "<Project Sdk=\"(.+)\">" [ projectType ] ->
                    Some (projectType.ToLower())
                | _ -> None
            where (
                _projectType = Some "microsoft.net.sdk.web" ||
                _outputType = Some "exe" ||
                _outputType = Some "winexe")

            let _runtimesToTarget =
                match _projectContents with
                | Regex "<RuntimeIdentifier.?>(.+)<\/RuntimeIdentifier" [ runtimes ] ->
                    runtimes |> String.splitStr ";"
                | _ -> runtimesToTarget

            for _runtime in _runtimesToTarget do

            let _targetFrameworks =
                match _projectContents with
                | Regex "<TargetFramework.?>(.+)<\/TargetFramework" [ frameworks ] ->
                    frameworks |> String.splitStr ";"
                | _ -> List.empty

            for framework in _targetFrameworks do

            select (_project, framework, _runtime)
        }

        for project, framework, runtime in projectsToPublish do
            let customParameters = "-p:PublishSingleFile=true -p:PublishTrimmed=true"

            project
            |> DotNet.publish (fun p ->
                { p with
                    Framework = Some framework
                    Runtime = Some runtime
                    Common = { p.Common with CustomParams = Some customParameters } } )

            let sourceFolder =
                seq {
                    (Path.getDirectory project)
                    "bin/Release"
                    framework
                    runtime
                    "publish"
                }
                |> Seq.fold (</>) ""

            let targetFolder =
                seq {
                    "publish"
                    Path.GetFileNameWithoutExtension project
                    framework
                    runtime
                }
                |> Seq.fold (</>) ""

            Shell.copyDir targetFolder sourceFolder (fun _ -> true)

            let zipFileName =
                seq {
                    Path.GetFileNameWithoutExtension project
                    finalVersion.Value.NormalizeToShorter()
                    framework
                    runtime
                }
                |> String.concat "."

            Zip.zip
                targetFolder
                $"publish/{zipFileName}.zip"
                !! (targetFolder </> "**")

    [ "Clean"; "Build" ] ==> "Publish"

module Test =
    //nuget Fake.IO.FileSystem
    //nuget Fake.DotNet.Testing.Expecto

    open System.IO
    open Fake.Core
    open Fake.Core.TargetOperators
    open Fake.IO.Globbing.Operators
    open Fake.DotNet.Testing

    let testProjects = !!"tests/*/*.?sproj"

    Target.create "Test" <| fun _ ->
        testProjects
        |> Seq.collect (fun projectPath ->
            let projectName = Path.GetFileNameWithoutExtension projectPath
            !! $"tests/{projectName}/bin/release/**/{projectName}.dll")
        |> Expecto.run id
    "Build" ==> "Test"

module TestSourceLink =
    //nuget Fake.IO.FileSystem
    //nuget Fake.DotNet.Cli

    open Fake.Core
    open Fake.Core.TargetOperators
    open Fake.IO.Globbing.Operators
    open Fake.DotNet

    Target.create "TestSourceLink" <| fun _ ->
        !! "publish/*.nupkg"
        |> Seq.iter (fun p ->
            DotNet.exec
                id
                "sourcelink"
                $"test {p}"
            |> fun r -> if not r.OK then failwith $"Source link check for {p} failed.")

    "Pack" ==> "TestSourceLink"

module Run =
    open Fake.Core
    open System.Diagnostics

    Target.create "Run" <| fun c ->
        match c.Context.Arguments |> Seq.tryHead with
        | None -> failwith "Need to specify the project to run"
        | Some x -> Process.Start("dotnet", $"run -p {x}") |> ignore

module BisectHelper =
    //nuget Fake.DotNet.Cli

    open Fake.Core
    open Fake.DotNet
    open System.Diagnostics

    Target.create "BisectHelper" <| fun c ->
        let project =
            match c.Context.Arguments |> Seq.tryHead with
            | None -> failwith "Need to specify the project to bisect"
            | Some x -> x

        let exitWith i =
            printfn $"Exiting with exit code {i}"
            exit i

        let rec readInput() : unit =
            printfn "Enter g for good, b for bad, s for skip and a to abort"
            match System.Console.ReadLine() with
            | "g" -> exitWith 0
            | "b" -> exitWith 1
            | "s" -> exitWith 125
            | "a" -> exitWith 128
            | _ -> readInput()

        try
            DotNet.build id project
            Process.Start("dotnet", $"run -p {project}").WaitForExit()
        with _ -> printfn "Build failed"

        readInput()

module UploadArtifactsToGitHub =
    //nuget Fake.Api.GitHub
    //nuget Fake.IO.FileSystem
    //nuget Fake.BuildServer.AppVeyor
    //nuget Fake.BuildServer.GitHubActions

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
        let targetCommit =
            if AppVeyor.detect() then AppVeyor.Environment.RepoCommit
            elif GitHubActions.detect() then GitHubActions.Environment.Sha
            else ""
        if targetCommit <> "" && finalVersion.PreRelease.IsNone then
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
                        TargetCommitish = targetCommit })
            |> GitHub.uploadFiles (!! "publish/*.nupkg" ++ "publish/*.zip")
            |> GitHub.publishDraft
            |> Async.RunSynchronously

    [ "Pack"; "Publish"; "Test"; "TestSourceLink" ] ==> "UploadArtifactsToGitHub"

module UploadPackageToNuget =
    //nuget Fake.DotNet.Paket
    //nuget Fake.BuildServer.AppVeyor
    //nuget Fake.BuildServer.GitHubActions

    open Fake.Core
    open Fake.DotNet
    open Fake.BuildServer

    open FinalVersion
    open CustomTargetOperators

    Target.create "UploadPackageToNuget" <| fun _ ->
        if (AppVeyor.detect() || GitHubActions.detect()) &&
            finalVersion.Value.PreRelease.IsNone then
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
        !! "src/*/obj/Release/**/ThisAssembly.GitInfo.g.?s"
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
            $"push -f {gitHome.Value} HEAD:release"

    [ "Clean"; "Build"; "Test" ] ==> "Release"

module AppVeyor =
    open Fake.Core

    open CustomTargetOperators

    Target.create "AppVeyor" ignore
    [ "UploadArtifactsToGitHub"; "UploadPackageToNuget" ] ==> "AppVeyor"

module GitHubActions =
    open Fake.Core

    open CustomTargetOperators

    Target.create "BuildAction" ignore
    [ "Build"; "Test"; "TestSourceLink" ] ==> "BuildAction"

    Target.create "ReleaseAction" ignore
    [ "UploadArtifactsToGitHub"; "UploadPackageToNuget" ] ==> "ReleaseAction"

module Default =
    open Fake.Core

    open CustomTargetOperators

    Target.create "Default" ignore
    [ "Build"; "Test" ] ==> "Default"

    Target.runOrDefaultWithArguments "Default"
