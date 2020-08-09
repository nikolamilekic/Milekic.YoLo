#load "./.fake/build.fsx/intellisense.fsx"

open System
open System.Text.RegularExpressions

open Fake.Api
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.Runtime
open Fake.Tools.Git
open Fake.BuildServer

let productName = "Milekic.YoLo"
let gitOwner = "nikolamilekic"
let gitHome = "https://github.com/" + gitOwner
let releaseNotes =
    (ReleaseNotes.load "RELEASE_NOTES.md").Notes
    |> String.concat Environment.NewLine

let (|Regex|_|) pattern input =
    let m = Regex.Match(input, pattern)
    if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ])
    else None

let finalVersion =
    lazy
    __SOURCE_DIRECTORY__ + "/src/Milekic.YoLo/obj/Release/netstandard2.0/Milekic.YoLo.AssemblyInfo.fs"
    |> File.readAsString
    |> function
        | Regex "AssemblyInformationalVersionAttribute\(\"(.+)\"\)>]" [ version ] ->
            SemVer.parse version
        | _ -> failwith "Could not parse assembly version"

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
    let newBuildProperties = [ "PackageReleaseNotes", releaseNotes ]
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

Target.create "UploadArtifactsToGitHub" <| fun c ->
    let finalVersion = finalVersion.Value
    if c.Context.FinalTarget = "AppVeyor" && finalVersion.PreRelease.IsSome
    then () else

    let token = Environment.environVarOrFail "GitHubToken"
    GitHub.createClientWithToken token
    |> GitHub.createRelease
        gitOwner
        productName
        (finalVersion.NormalizeToShorter())
        (fun o ->
            { o with
                Body = releaseNotes
                Prerelease = (finalVersion.PreRelease <> None)
                TargetCommitish = AppVeyor.Environment.RepoCommit })
    |> GitHub.uploadFiles !! "publish/*"
    |> GitHub.publishDraft
    |> Async.RunSynchronously

[ "TestSourceLink" ] ==> "UploadArtifactsToGitHub"

Target.create "UploadPackageToNuget" <| fun _ ->
    Paket.push <| fun p ->
        { p with
            ToolType = ToolType.CreateLocalTool()
            WorkingDir = __SOURCE_DIRECTORY__ + "/publish" }

[ "TestSourceLink" ] ==> "UploadPackageToNuget"

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

Target.create "AppVeyor" <| fun _ ->
    let finalVersion = finalVersion.Value
    if AppVeyor.detect() then
        let appVeyorVersion =
            sprintf
                "%d.%d.%d.%s"
                finalVersion.Major
                finalVersion.Minor
                finalVersion.Patch
                AppVeyor.Environment.BuildNumber

        AppVeyor.updateBuild (fun p -> { p with Version = appVeyorVersion })

[ "UploadArtifactsToGitHub"; "UploadPackageToNuget" ] ==> "AppVeyor"

Target.runOrDefault "Build"
