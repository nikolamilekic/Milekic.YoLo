#load "./.fake/build.fsx/intellisense.fsx"

open System
open System.IO

open Fake.Api
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.IO.FileSystemOperators
open Fake.Runtime
open Fake.Tools.Git
open Fake.BuildServer

let productName = "Milekic.YoLo"
let gitOwner = "nikolamilekic"
let gitHome = "https://github.com/" + gitOwner
let releaseNotes = ReleaseNotes.load "RELEASE_NOTES.md"

let packageVersion =
    if releaseNotes.SemVer.PreRelease.IsSome && AppVeyor.detect() then
        sprintf
            "%s.%s"
            releaseNotes.NugetVersion
            AppVeyor.Environment.BuildNumber
    else releaseNotes.NugetVersion
let assemblyVersion = releaseNotes.AssemblyVersion
let fileVersion =
    if AppVeyor.detect() then
        let assemblyVersion = SemVer.parse(assemblyVersion)
        sprintf
            "%i.%i.%i.%s"
            assemblyVersion.Major
            assemblyVersion.Minor
            assemblyVersion.Patch
            AppVeyor.Environment.BuildNumber
    else assemblyVersion

if AppVeyor.detect() then
    AppVeyor.updateBuild (fun p -> { p with Version = fileVersion })

let flip f a b = f b a
let (==>) xs y = xs |> Seq.iter (fun x -> x ==> y |> ignore)
let (?=>) xs y = xs |> Seq.iter (fun x -> x ?=> y |> ignore)

Target.initEnvironment ()

Target.create "Clean" <| fun _ ->
    [|"bin"; "obj"|]
    |> Seq.collect (fun x -> !!(sprintf "src/**/%s" x))
    |> Seq.append [|"bin"; "obj" |]
    |> Shell.deleteDirs

    Shell.cleanDir "publish"

Target.create "Build" <| fun _ -> DotNet.build id "src/Milekic.YoLo"
[ "Clean" ]  ?=> "Build"

Target.create "Pack" <| fun _ ->
    let newBuildProperties = [
        "Version", packageVersion
        "AssemblyVersion", assemblyVersion
        "FileVersion", fileVersion
        "PackageReleaseNotes",
            (String.concat Environment.NewLine releaseNotes.Notes)
    ]
    DotNet.pack
        (fun p ->
            { p with
                OutputPath = Some (__SOURCE_DIRECTORY__ + "/publish")
                MSBuildParams =
                    { p.MSBuildParams with
                        Properties =
                            newBuildProperties @ p.MSBuildParams.Properties }})
        "src/Milekic.YoLo"

[ "Clean" ] ==> "Pack"

Target.create "TestSourceLink" <| fun _ ->
    !! "publish/*.nupkg"
    |> flip Seq.iter <| fun p ->
        DotNet.exec
            id
            "packages/build/sourcelink/tools/netcoreapp2.1/any/sourcelink.dll"
            (sprintf "test %s" p)
        |> fun r -> if not r.OK then failwithf "Source link check for %s failed." p

[ "Pack" ] ==> "TestSourceLink"

Target.create "UploadArtifactsToGitHub" <| fun _ ->
    if AppVeyor.detect() &&
        AppVeyor.Environment.RepoBranch = "release" &&
        releaseNotes.SemVer.PreRelease.IsNone then

        let token = Environment.environVarOrFail "GitHubToken"
        GitHub.createClientWithToken token
        |> GitHub.createRelease
            gitOwner
            productName
            packageVersion
            (fun o ->
                { o with
                    Body = String.concat Environment.NewLine releaseNotes.Notes
                    Prerelease = (releaseNotes.SemVer.PreRelease <> None)
                    TargetCommitish = AppVeyor.Environment.RepoCommit })
        |> GitHub.uploadFiles !! "publish/*"
        |> GitHub.publishDraft
        |> Async.RunSynchronously

[ "TestSourceLink" ] ==> "UploadArtifactsToGitHub"

Target.create "Release" <| fun _ ->
    let remote =
        CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun s -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun s -> s.Contains(gitOwner + "/" + productName))
        |> function | None -> gitHome + "/" + productName
                    | Some s -> s.Split().[0]
    CommandHelper.directRunGitCommandAndFail
        ""
        (sprintf "push -f %s HEAD:release" remote)

[ "Clean"; "Build" ] ==> "Release"

Target.create "AppVeyor" ignore
[ "UploadArtifactsToGitHub" ] ==> "AppVeyor"

Target.runOrDefault "Build"
