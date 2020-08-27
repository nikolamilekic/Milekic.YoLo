#load ".fake/build.fsx/intellisense.fsx"

open System
open System.IO
open System.Text.RegularExpressions

open Fake.Api
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.DotNet.Testing
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.IO.FileSystemOperators
open Fake.Runtime
open Fake.Tools.Git
open Fake.BuildServer
open FSharpPlus

let productName = "Milekic.YoLo"
let gitOwner = "nikolamilekic"
let gitHome = "git@github.com:nikolamilekic/Milekic.YoLo.git"
let releaseNotes =
    (ReleaseNotes.load "RELEASE_NOTES.md").Notes
    |> String.concat Environment.NewLine
let pathToAssemblyInfoFile = "/src/Milekic.YoLo/obj/Release/netstandard2.0/Milekic.YoLo.AssemblyInfo.fs"
let uploadPackageToNuget = true

let (|Regex|_|) pattern input =
    let m = Regex.Match(input, pattern)
    if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ])
    else None

let finalVersion =
    lazy
    __SOURCE_DIRECTORY__ + pathToAssemblyInfoFile
    |> File.readAsString
    |> function
        | Regex "AssemblyInformationalVersionAttribute\(\"(.+)\"\)>]" [ version ] ->
            SemVer.parse version
        | _ -> failwith "Could not parse assembly version"

let (==>) xs y = xs |> Seq.iter (fun x -> x ==> y |> ignore)
let (?=>) xs y = xs |> Seq.iter (fun x -> x ?=> y |> ignore)

Target.initEnvironment ()

Target.create "Clean" <| fun _ ->
    lift2 tuple2 [|"src"; "tests"|] [|"bin"; "obj"|]
    >>= fun (x,y) -> !!(sprintf "%s/**/%s" x y) |> toSeq
    |> plus ([|"bin"; "obj"|] |> toSeq)
    |> Shell.deleteDirs

    Shell.cleanDir "publish"

Target.create "Build" <| fun _ ->
    DotNet.build id (productName + ".sln")
    !! "src/**/*.fsproj"
    |> toSeq
    |>> (fun projectPath ->
        (Path.GetDirectoryName projectPath) </> "bin/Release",
        "bin" </> (Path.GetFileNameWithoutExtension projectPath))
    |> iter (fun (source, target) -> Shell.copyDir target source (konst true))

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
        (productName + ".sln")

[ "Clean" ] ==> "Pack"

Target.create "Test" <| fun _ ->
    !! "tests/**/*.fsproj"
    |> toSeq
    >>= fun projectPath ->
        let projectName = Path.GetFileNameWithoutExtension projectPath
        !! (sprintf "tests/%s/bin/release/**/%s.dll" projectName projectName)
        |> toSeq
    |> Expecto.run id
[ "Build"; "Pack" ] ?=> "Test"

Target.create "TestSourceLink" <| fun _ ->
    !! "publish/*.nupkg"
    |> flip Seq.iter <| fun p ->
        DotNet.exec
            id
            "sourcelink"
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

[ "TestSourceLink"; "Test" ] ==> "UploadArtifactsToGitHub"

Target.create "UploadPackageToNuget" <| fun _ ->
    if uploadPackageToNuget then
        Paket.push <| fun p ->
            { p with
                ToolType = ToolType.CreateLocalTool()
                WorkingDir = __SOURCE_DIRECTORY__ + "/publish" }

[ "TestSourceLink"; "Test" ] ==> "UploadPackageToNuget"

Target.create "Release" <| fun _ ->
    CommandHelper.directRunGitCommandAndFail
        ""
        (sprintf "push -f %s HEAD:release" gitHome)

[ "Clean"; "Build"; "Test" ] ==> "Release"

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

[ "UploadArtifactsToGitHub"; "UploadPackageToNuget"; "Test" ] ==> "AppVeyor"

Target.create "Default" ignore
[ "Build"; "Test" ] ==> "Default"

Target.runOrDefault "Default"
