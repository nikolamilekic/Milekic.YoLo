#r "paket:
source https://api.nuget.org/v3/index.json
source https://nuget.nikolamilekic.com/index.json

nuget FSharp.Core

nuget Fake.Api.GitHub
nuget Octokit
nuget Fake.BuildServer.GitHubActions
nuget Fake.Core.ReleaseNotes
nuget Fake.Core.SemVer
nuget Fake.Core.Target
nuget Fake.DotNet.Cli
nuget Fake.DotNet.Paket
nuget Fake.IO.FileSystem
nuget Fake.IO.Zip
nuget Fake.Tools.Git

nuget Milekic.YoLo
nuget Fs1PasswordConnect //"
#load ".fake/build.fsx/intellisense.fsx"

//nuget Fake.Core.Target
open Fake.Core.TargetOperators

let (==>) xs y = xs |> Seq.iter (fun x -> x ==> y |> ignore)
let (?=>) xs y = xs |> Seq.iter (fun x -> x ?=> y |> ignore)

module FinalVersion =
    //nuget Fake.IO.FileSystem
    //nuget Fake.Core.SemVer
    //nuget Milekic.YoLo

    open Fake.IO
    open Fake.IO.Globbing.Operators
    open Fake.Core
    open Milekic.YoLo

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

module ConnectClient =
    open Fs1PasswordConnect
    let client = ConnectClient.fromEnvironmentVariablesCached ()

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
    //nuget Fake.IO.FileSystem

    open Fake.DotNet
    open Fake.Core
    open Fake.IO.Globbing.Operators

    let projectToBuild = !! "*.sln" |> Seq.head

    Target.create "Build" <| fun _ -> DotNet.build id projectToBuild
    [ "Clean" ] ?=> "Build"

    Target.create "Rebuild" ignore
    [ "Clean"; "Build" ] ==> "Rebuild"

module Test =
    //nuget Fake.DotNet.Cli
    //nuget Fake.IO.FileSystem

    open System.IO
    open Fake.IO
    open Fake.Core
    open Fake.DotNet
    open Fake.IO.Globbing.Operators

    let testProjects = !!"tests/*/*.?sproj"

    Target.create "Test" <| fun _ ->
        let testException =
            testProjects
            |> Seq.map (fun project ->
                let project = Path.getDirectory project
                try
                    DotNet.test
                        (fun x ->
                            { x with
                                NoBuild = true
                                Configuration = DotNet.BuildConfiguration.Release })
                        project
                    None
                with e -> Some e)
            |> Seq.tryPick id

        let testResults = query {
            for _project in testProjects do
            let _projectName = Path.GetFileNameWithoutExtension _project
            let _projectPath = Path.getDirectory _project
            let _outputPath = Path.combine _projectPath "bin/Release"
            let dllPath = Path.combine _outputPath $"{_projectName}.dll"
            let testExecutionFile = Path.combine _outputPath "TestExecution.json"
            where (File.Exists dllPath && File.Exists testExecutionFile)
            let output = $"./testResults/{_projectName}.html"
            select (dllPath, testExecutionFile, output)
        }

        for dllPath, testExecutionFile, output in testResults do
            DotNet.exec id "livingdoc" $"test-assembly {dllPath} -t {testExecutionFile} -o {output}"
            |> ignore

        match testException with
        | None -> ()
        | Some e -> raise e

    [ "Build" ] ==> "Test"

module Pack =
    //nuget Fake.DotNet.Cli
    //nuget Fake.IO.FileSystem

    open Fake.DotNet
    open Fake.Core
    open Fake.IO.Globbing.Operators

    open ReleaseNotesParsing

    let projectToPack = !! "*.sln" |> Seq.head

    Target.create "Pack" <| fun _ ->
        let newBuildProperties = [ "PackageReleaseNotes", releaseNotes.Value ]
        DotNet.pack
            (fun p ->
                { p with
                    OutputPath = Some (__SOURCE_DIRECTORY__ + "/publish")
                    NoBuild = true
                    NoRestore = true
                    MSBuildParams =
                        { p.MSBuildParams with
                            Properties =
                                newBuildProperties @ p.MSBuildParams.Properties }})
            projectToPack

    [ "Build" ] ==> "Pack"

    Target.create "Repack" ignore
    [ "Clean"; "Pack" ] ==> "Repack"

module Publish =
    //nuget Fake.DotNet.Cli
    //nuget Fake.IO.FileSystem
    //nuget Fake.IO.Zip
    //nuget Milekic.YoLo

    open System.IO
    open Fake.DotNet
    open Fake.Core
    open Fake.IO
    open Fake.IO.Globbing.Operators
    open Fake.IO.FileSystemOperators
    open Milekic.YoLo

    open FinalVersion

    let projectsToPublish = !!"src/*/*.?sproj"

    type ProjectInfo = {
        Path : string
        OutputType : string option
        ProjectType : string option
        TargetFrameworks : string list
        BundleMacOSApp : string option
    }

    let parse project =
        let projectContents = File.readAsString project
        {
            Path = project
            OutputType =
                match projectContents with
                | Regex "<OutputType>(.+)<\/OutputType>" [ outputType ] ->
                    Some (outputType.ToLower())
                | _ -> None
            ProjectType =
                match projectContents with
                | Regex "<Project Sdk=\"(.+)\">" [ projectType ] ->
                    Some (projectType.ToLower())
                | _ -> None
            TargetFrameworks =
                match projectContents with
                | Regex "<TargetFramework.?>(.+)<\/TargetFramework" [ frameworks ] ->
                    frameworks |> String.splitStr ";"
                | _ -> []
            BundleMacOSApp =
                match projectContents with
                | Regex "<CFBundleName>(.+)<\/CFBundleName>" [ bundleName ] ->
                    Some bundleName
                | _ -> None
        }

    let publish (targetRuntimes : string seq) =
        let projectsToPublish =
            projectsToPublish
            |> Seq.map parse
            |> Seq.filter (fun { ProjectType = projectType; OutputType = outputType } ->
                projectType = Some "microsoft.net.sdk.web" ||
                outputType = Some "exe" ||
                outputType = Some "winexe")

        for project in projectsToPublish do
        for framework in project.TargetFrameworks do
        for runtime in targetRuntimes do
            let sourceFolder =
                seq {
                    (Path.getDirectory project.Path)
                    "bin/Release"
                    framework
                    runtime
                    "publish"
                }
                |> Seq.fold (</>) ""

            let targetFolder =
                seq {
                    "publish"
                    Path.GetFileNameWithoutExtension project.Path
                    framework
                    runtime
                }
                |> Seq.fold (</>) ""

            match runtime, project.BundleMacOSApp with
            | "osx-x64", Some bundle ->
                let customParameters = "-t:BundleApp -p:PublishTrimmed=true --self-contained"

                project.Path
                |> DotNet.publish (fun p ->
                    { p with
                        Framework = Some framework
                        Runtime = Some runtime
                        Common = { p.Common with CustomParams = Some customParameters } } )

                let sourceFolder = sourceFolder </> bundle + ".app"
                let targetFolder = targetFolder </> bundle + ".app"
                Shell.copyDir targetFolder sourceFolder (fun _ -> true)
            | _ ->
                let customParameters = "-p:PublishSingleFile=true -p:PublishTrimmed=true --self-contained"

                project.Path
                |> DotNet.publish (fun p ->
                    { p with
                        Framework = Some framework
                        Runtime = Some runtime
                        Common = { p.Common with CustomParams = Some customParameters } } )

                Shell.copyDir targetFolder sourceFolder (fun _ -> true)

            let zipFileName =
                seq {
                    Path.GetFileNameWithoutExtension project.Path
                    finalVersion.Value.NormalizeToShorter()
                    framework
                    runtime
                }
                |> String.concat "."

            Zip.zip
                targetFolder
                $"publish/{zipFileName}.zip"
                !! (targetFolder </> "**")

    Target.create "PublishWindows" <| fun _ -> publish [ "win-x64" ]
    Target.create "PublishMacOS" <| fun _ -> publish [ "osx-x64" ]
    Target.create "PublishLinux" <| fun _ -> publish [ "linux-arm"; "linux-x64" ]

    [ "Clean"; "Test" ] ?=> "PublishWindows"
    [ "Clean"; "Test" ] ?=> "PublishMacOS"
    [ "Clean"; "Test" ] ?=> "PublishLinux"

module TestSourceLink =
    //nuget Fake.IO.FileSystem
    //nuget Fake.DotNet.Cli

    open Fake.Core
    open Fake.IO.Globbing.Operators
    open Fake.DotNet

    Target.create "TestSourceLink" <| fun _ ->
        !! "publish/*.nupkg"
        |> Seq.iter (fun p ->
            DotNet.exec id "sourcelink" $"test {p}"
            |> fun r -> if not r.OK then failwith $"Source link check for {p} failed.")

    [ "Pack" ] ==> "TestSourceLink"

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
    //nuget Fake.BuildServer.GitHubActions

    open System.IO
    open Fake.Core
    open Fake.Api
    open Fake.IO.Globbing.Operators
    open Fake.BuildServer

    open FinalVersion
    open ReleaseNotesParsing

    let productName = !! "*.sln" |> Seq.head |> Path.GetFileNameWithoutExtension
    let gitOwner = "nikolamilekic"

    Target.create "CreateGitHubRelease" <| fun _ ->
        let targetCommit =
            if GitHubActions.detect() then GitHubActions.Environment.Sha
            else ""
        if targetCommit <> "" then
            let finalVersion = finalVersion.Value
            let token = Environment.environVarOrFail "GITHUB_TOKEN"
            GitHub.createClientWithToken token
            |> GitHub.createRelease
                gitOwner
                productName
                (finalVersion.NormalizeToShorter())
                (fun o ->
                    { o with
                        Draft = false
                        Body = releaseNotes.Value
                        Prerelease = (finalVersion.PreRelease <> None)
                        TargetCommitish = targetCommit })
            |> Async.Ignore
            |> Async.RunSynchronously

    [
        "Pack"
        "PublishWindows"
        "PublishMacOS"
        "PublishLinux"
        "Test"
        "TestSourceLink"
    ] ?=> "CreateGitHubRelease"

    Target.create "UploadArtifactsToGitHub" <| fun _ ->
        let targetCommit =
            if GitHubActions.detect() then GitHubActions.Environment.Sha
            else ""
        if targetCommit <> "" then
            let token = Environment.environVarOrFail "GITHUB_TOKEN"
            GitHub.createClientWithToken token
            |> fun client -> async {
                let! client = client
                let rec retry attemptsRemaining : Async<GitHub.Release> = async {
                    if attemptsRemaining = 0 then
                        return failwith $"Could not find release for commit {targetCommit}"
                    else

                    let! releases =
                        client.Repository.Release.GetAll(gitOwner, productName)
                        |> Async.AwaitTask
                    let releaseMaybe =
                        releases
                        |> Seq.tryFind (fun r -> r.TargetCommitish = targetCommit)

                    match releaseMaybe with
                    | Some release ->
                        return {
                            Client = client
                            Owner = gitOwner
                            Release = release
                            RepoName = productName
                        }
                    | None ->
                        do! Async.Sleep 3000
                        return! retry (attemptsRemaining - 1)
                }
                return! retry 5
            }
            |> GitHub.uploadFiles (
                !! "publish/*.nupkg"
                ++ "publish/*.snupkg"
                ++ "publish/*.zip")
            |> Async.Ignore
            |> Async.RunSynchronously

    [
        "Pack"
        "PublishWindows"
        "PublishMacOS"
        "PublishLinux"
        "Test"
        "TestSourceLink"
        "CreateGitHubRelease"
    ] ?=> "UploadArtifactsToGitHub"

module UploadPackageToNuget =
    //nuget Fake.DotNet.Paket
    //nuget Fake.BuildServer.GitHubActions
    //nuget Fs1PasswordConnect

    open Fake.Core
    open Fake.DotNet
    open Fake.BuildServer

    open FinalVersion

    Target.create "UploadPackageToNuget" <| fun _ ->
        if GitHubActions.detect() = false || finalVersion.Value.PreRelease.IsSome then () else

        let apiKey =
            match Environment.environVarOrNone "NUGET_KEY", ConnectClient.client with
            | Some key, Ok client ->
                match client.Inject key |> Async.RunSynchronously with
                | Error e -> failwith $"Could not retrieve nuget key due to the following Connect error: {e.ToString()}."
                | Ok key -> Some key
            | _ -> None

        match apiKey with
        | Some apiKey ->
            Paket.push <| fun p -> {
                p with
                    ApiKey = apiKey
                    ToolType = ToolType.CreateLocalTool()
                    WorkingDir = __SOURCE_DIRECTORY__ + "/publish" }
        | None -> ()

    [ "Pack" ] ==> "UploadPackageToNuget"

    [
        "UploadArtifactsToGitHub"
        "Test"
        "TestSourceLink"
    ] ?=> "UploadPackageToNuget"

module UploadPackageWithSleet =
    //nuget Fake.DotNet.Cli
    //nuget Fs1PasswordConnect
    //nuget Fake.BuildServer.GitHubActions

    open Fake.Core
    open Fake.DotNet
    open Fake.BuildServer
    open System.IO

    let publishDirectory = __SOURCE_DIRECTORY__ + "/publish"

    Target.create "UploadPackageWithSleet" <| fun _ ->
        if (GitHubActions.detect() = false ||
            Directory.Exists(publishDirectory) = false) then () else

        let configFile =
            match Environment.environVarOrNone "SLEET_CONFIG", ConnectClient.client with
            | Some config, Ok client ->
                match client.Inject config |> Async.RunSynchronously with
                | Error e -> failwith $"Could not retrieve Sleet config due to the following Connect error: {e.ToString()}."
                | Ok updatedConfig ->
                    let updatedConfigPath = __SOURCE_DIRECTORY__ + "/Sleet.json"
                    File.WriteAllText(updatedConfigPath, updatedConfig)
                    Some updatedConfigPath
            | _ -> None

        match configFile with
        | Some path ->
            DotNet.exec id "sleet" $"push {publishDirectory} -c {path}"
            |> fun r -> if not r.OK then failwith $"Failed to push to Sleet. Errors: {r.Errors}"
        | _ -> ()

    [ "Pack"  ] ==> "UploadPackageWithSleet"

    [
        "UploadArtifactsToGitHub"
        "Test"
        "TestSourceLink"
    ] ?=> "UploadPackageWithSleet"

module Release =
    //nuget Fake.Tools.Git
    //nuget Milekic.YoLo

    open Fake.IO
    open Fake.IO.Globbing.Operators
    open Fake.Core
    open Fake.Tools
    open Milekic.YoLo

    let pathToThisAssemblyFile =
        lazy
        !! "src/*/obj/Release/**/ThisAssembly.GitInfo.g.?s"
        |> Seq.head

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

    [ "Clean"; "Build"; "Test"; "TestSourceLink" ] ==> "Release"

module GitHubActions =
    open Fake.Core

    Target.create "BuildAction" ignore
    [ "Clean"; "Build"; "Test"; "TestSourceLink" ] ==> "BuildAction"

    Target.create "ReleaseAction" ignore
    [
        "BuildAction"
        "Pack"
        "PublishLinux"
        "CreateGitHubRelease"
        "UploadArtifactsToGitHub"
        "UploadPackageToNuget"
        "UploadPackageWithSleet"
    ] ==> "ReleaseAction"

    Target.create "PublishWindowsAction" ignore
    [ "PublishWindows"; "UploadArtifactsToGitHub" ] ==> "PublishWindowsAction"

    Target.create "PublishMacOSAction" ignore
    [ "PublishMacOS"; "UploadArtifactsToGitHub" ] ==> "PublishMacOSAction"

module Default =
    open Fake.Core

    Target.create "Default" ignore
    [ "Build"; "Test" ] ==> "Default"

    Target.runOrDefaultWithArguments "Default"
