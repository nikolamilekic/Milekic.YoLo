#r "paket:
nuget Fake.Api.GitHub //
nuget Fake.Core.ReleaseNotes prerelease //
nuget Fake.Core.Target prerelease //
nuget Fake.DotNet.AssemblyInfoFile //
nuget Fake.DotNet.Cli prerelease //
nuget Fake.DotNet.Paket prerelease //
nuget Fake.IO.FileSystem prerelease //
nuget Fake.Runtime prerelease //
nuget Fake.Tools.Git prerelease //"
#load "./.fake/build.fsx/intellisense.fsx"

open System.IO

open Fake.Api
open Fake.Core
open Fake.Core.Target
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.IO.FileSystemOperators
open Fake.Runtime
open Fake.Tools.Git

let productName = "Milekic.YoLo"
let gitOwner = "nikolamilekic"
let gitHome = "https://github.com/" + gitOwner
let releaseNotes = ReleaseNotes.load "RELEASE_NOTES.md"

Target.create "Clean" <| fun _ ->
    Seq.allPairs [|"src"; "tests"|] [|"bin"; "obj"|]
    |> Seq.collect (fun (x, y) -> !!(sprintf "%s/**/%s" x y))
    |> Seq.append [|"bin"; "obj"|]
    |> Shell.deleteDirs
Target.create "Build" <| fun _ -> DotNet.build id (productName + ".sln")
Target.create "Test" <| fun _ ->
    !! "tests/*.Tests/"
    |> Seq.map (fun path ->
        DotNet.exec
            (fun o -> { o with WorkingDirectory = path }) "run" "-c Release")
    |> List.ofSeq
    |> List.iter (fun r -> if r.ExitCode <> 0 then failwith "Tests failed")
Target.create "UpdateAssemblyInfo" <| fun _ ->
    let version =
        match Environment.environVarOrNone "APPVEYOR_BUILD_NUMBER" with
        | None -> releaseNotes.AssemblyVersion
        | Some buildNumber ->
            let assemblyVersion = SemVer.parse(releaseNotes.AssemblyVersion)
            sprintf
                "%i.%i.%i.%s"
                assemblyVersion.Major
                assemblyVersion.Minor
                assemblyVersion.Patch
                buildNumber
    !! "src/**/*.fsproj"
    |> Seq.iter (fun projectPath ->
        let projectName = Path.GetFileNameWithoutExtension projectPath
        let attributes = [
            AssemblyInfo.Title projectName
            AssemblyInfo.Product productName
            AssemblyInfo.Version version
            AssemblyInfo.FileVersion version
        ]
        AssemblyInfoFile.createFSharp
            (Path.GetDirectoryName projectPath </> "AssemblyInfo.fs")
            attributes)
Target.create "BumpVersion" <| fun _ ->
    let appveyorPath = "appveyor.yml"
    let appveyorVersion =
        let assemblyVersion = SemVer.parse(releaseNotes.AssemblyVersion)
        sprintf
            "%i.%i.%i.{build}"
            assemblyVersion.Major
            assemblyVersion.Minor
            assemblyVersion.Patch
    File.ReadAllLines appveyorPath
    |> Seq.map (function
        | line when line.StartsWith "version:" ->
            sprintf "version: %s" appveyorVersion
        | line -> line)
    |> fun lines -> File.WriteAllLines(appveyorPath, lines)
    Staging.stageAll ""
    Commit.exec "" (sprintf "Bump version to %s" releaseNotes.NugetVersion)
Target.create "Release" <| fun _ ->
    let remote =
        CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun s -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun s -> s.Contains(gitOwner + "/" + productName))
        |> function | None -> gitHome + "/" + productName
                    | Some s -> s.Split().[0]
    let token = Environment.environVarOrFail "GitHubToken"
    let version = releaseNotes.NugetVersion
    Branches.tag "" version
    Branches.pushTag "" remote version
    GitHub.createClientWithToken token
    |> GitHub.draftNewRelease
        gitOwner
        productName
        version
        (releaseNotes.SemVer.PreRelease <> None)
        releaseNotes.Notes
    |> GitHub.publishDraft
    |> Async.RunSynchronously
Target.create "CopyBinaries" <| fun _ ->
    !! "src/**/*.fsproj"
    |>  Seq.map (fun projectPath ->
        (Path.GetDirectoryName projectPath) </> "bin/Release",
        "bin" </> (Path.GetFileNameWithoutExtension projectPath))
    |>  Seq.iter (fun (source, target) ->
        Shell.copyDir target source (fun _ -> true))
Target.create "Nuget" <| fun _ ->
    let isAppVeyor = Environment.environVarAsBool "APPVEYOR"
    let fromTag = Environment.environVarAsBool "APPVEYOR_REPO_TAG"
    if isAppVeyor && (not fromTag) then () else
    Paket.pack (fun p ->
        { p with
            OutputPath = "bin"
            Version = releaseNotes.NugetVersion
            ReleaseNotes = String.toLines releaseNotes.Notes})
Target.create "AppVeyor" DoNothing

Target.create "Rebuild" DoNothing

"Build"
    ==> "CopyBinaries"
    ==> "Test"
    ==> "Rebuild"
    ==> "Nuget"
    ==> "AppVeyor"

"UpdateAssemblyInfo" ?=> "Build"
"Clean" ?=> "Build"
"Clean" ==> "Rebuild"
"Rebuild" ==> "Release"
"UpdateAssemblyInfo" ==> "BumpVersion"
"UpdateAssemblyInfo" ==> "AppVeyor"

runOrDefault "Test"
