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

let version =
    if AppVeyor.detect() then
        let assemblyVersion = SemVer.parse(releaseNotes.AssemblyVersion)
        let result =
            sprintf
                "%i.%i.%i.%s"
                assemblyVersion.Major
                assemblyVersion.Minor
                assemblyVersion.Patch
                AppVeyor.Environment.BuildNumber
        AppVeyor.updateBuild (fun p -> { p with Version = result })
        result
    else releaseNotes.AssemblyVersion

let flip f a b = f b a
let (==>) xs y = xs |> Seq.iter (fun x -> x ==> y |> ignore)
let (?=>) xs y = xs |> Seq.iter (fun x -> x ?=> y |> ignore)

Target.initEnvironment ()

Target.create "UpdateAssemblyInfo" <| fun _ ->
    !! "src/**/*.fsproj"
    |> flip Seq.map <| fun projectPath ->
        projectPath, (Path.GetDirectoryName projectPath </> "AssemblyInfo.fs")
    |> flip Seq.where <| fun (_, assemblyInfoPath) ->
        File.exists assemblyInfoPath
    |> Seq.iter (fun (projectPath, assemblyInfoPath) ->
        let projectName = Path.GetFileNameWithoutExtension projectPath
        let attributes = seq {
            yield! [
                AssemblyInfo.Title projectName
                AssemblyInfo.Product productName
                AssemblyInfo.Version version
                AssemblyInfo.FileVersion version
            ]

            if AppVeyor.detect() then
                let commitHash = Information.getCurrentHash()
                yield AssemblyInfo.Metadata("GitHash", commitHash)
        }
        AssemblyInfoFile.createFSharp assemblyInfoPath attributes)

Target.create "Clean" <| fun _ ->
    [|"bin"; "obj"|]
    |> Seq.collect (fun x -> !!(sprintf "src/**/%s" x))
    |> Seq.append [|"bin"; "obj" |]
    |> Shell.deleteDirs

    Shell.cleanDir "publish"

Target.create "Build" <| fun _ ->
    Paket.restore id
    DotNet.build id "src/Milekic.YoLo"

[ "Clean"; "UpdateAssemblyInfo" ]  ?=> "Build"

Target.create "Pack" <| fun _ ->
    let buildNumber = Environment.environVarOrNone "APPVEYOR_BUILD_NUMBER"
    let isAppVeyor = Environment.environVarAsBool "APPVEYOR"
    let prerelease = releaseNotes.SemVer.PreRelease |> Option.isSome
    let fromTag = Environment.environVarAsBool "APPVEYOR_REPO_TAG"
    let version = match buildNumber with
                  | Some buildNumber when prerelease
                      -> sprintf "%s.%s" releaseNotes.NugetVersion buildNumber
                  | _ -> releaseNotes.NugetVersion
    if not isAppVeyor || prerelease || fromTag then
        Paket.pack (fun p ->
            { p with OutputPath = "publish"
                     Version = version
                     ReleaseNotes = String.toLines releaseNotes.Notes } )

[ "UpdateAssemblyInfo" ] ?=> "Pack"
[ "Clean"; "Build" ] ==> "Pack"

Target.create "UploadArtifactsToGitHub" <| fun _ ->
    if AppVeyor.detect() && AppVeyor.Environment.RepoBranch = "release" then
        let token = Environment.environVarOrFail "GitHubToken"
        GitHub.createClientWithToken token
        |> GitHub.createRelease
            gitOwner
            productName
            version
            (fun o ->
                { o with
                    Body = String.concat Environment.NewLine releaseNotes.Notes
                    Prerelease = (releaseNotes.SemVer.PreRelease <> None)
                    TargetCommitish = AppVeyor.Environment.RepoCommit })
        |> GitHub.uploadFiles !! "publish/*"
        |> GitHub.publishDraft
        |> Async.RunSynchronously

[ "Pack" ] ==> "UploadArtifactsToGitHub"

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
[ "Pack"; "UpdateAssemblyInfo"; "UploadArtifactsToGitHub" ] ==> "AppVeyor"

Target.runOrDefault "Build"
