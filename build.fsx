#r "paket: groupref build-deps //"
#load "./.fake/build.fsx/intellisense.fsx"
#if !FAKE
#r "netstandard"
#r "Facades/netstandard"
#endif


open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.Tools.Git
open System
open System.IO

type Build =
    { Version : string
      Publish : bool }

let config =
    { Version = "0.2.0-alpha01"
      Publish = true }

let buildDir = "build"

let assertVersion inputStr =
    if Fake.Core.SemVer.isValid inputStr then ()
    else failwith "Value in version.yml must adhere to the SemanticVersion 2.0 Spec"

assertVersion config.Version

let inline withVersionArgs version options =
    options |> DotNet.Options.withCustomParams (Some(sprintf "/p:VersionPrefix=\"%s\"" version))
let nugetKeyVariable = "NUGET_KEY"

let getProjFolders projPath =
    let dir = Path.GetDirectoryName projPath
    [ sprintf "%s/bin" dir
      sprintf "%s/obj" dir ]

// *** Define Targets ***
Target.create "Clean" (fun _ ->
    let projFoldersToDelete =
        !!"*/*.csproj" |> List.ofSeq |> List.collect (fun project -> getProjFolders project)
    let allFoldersToClean = (buildDir :: projFoldersToDelete)
    Trace.logfn "All folders to clean: %A" allFoldersToClean
    Shell.cleanDirs allFoldersToClean)
Target.create "Build" (fun _ -> DotNet.build (fun p -> { p with Configuration = DotNet.BuildConfiguration.Release }) "")
Target.create "Test" (fun _ ->
    let test project =
        DotNet.test (fun p ->
            { p with Configuration = DotNet.BuildConfiguration.Release
                     NoBuild = true }) project
    test "Resultful.Tests")
Target.create "Package" (fun _ ->
    Directory.ensure buildDir
    let packProject version projectPath =
        DotNet.pack (fun p ->
            { p with Configuration = DotNet.BuildConfiguration.Release
                     OutputPath = Some(sprintf "../%s" buildDir)
                     NoBuild = true }
            |> withVersionArgs version) projectPath
    packProject config.Version "Resultful/Resultful.csproj")
Target.create "Publish" (fun _ ->
    let gitBranch = Information.getBranchName "."
    Trace.log (sprintf "Git branch: %s" gitBranch)
    let isMaster = gitBranch = "master"

    let publishPackage shouldPublish project =
        if shouldPublish && isMaster then Fake.DotNet.Paket.push (fun p -> { p with WorkingDir = "build" })
        else
            Trace.log
                (sprintf "Package upload skipped because %s was not set to be published or the branch %s is not master"
                     project gitBranch)
    match Environment.environVarOrNone nugetKeyVariable with
    | Some _ -> publishPackage config.Publish "Resultful"
    | None -> Trace.log (sprintf "Package upload skipped because %s was not found" nugetKeyVariable))

// *** Define Dependencies ***
open Fake.Core.TargetOperators

"Clean" ==> "Build" ==> "Test" ==> "Package" ==> "Publish"
// *** Start Build ***
Fake.Core.Target.runOrDefault "Package"
