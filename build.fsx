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

let version = "0.2.0-alpha02"
let rnd = Random()

let env = Environment.environVarOrNone
let assertVersion inputStr =
    if Fake.Core.SemVer.isValid inputStr then ()
    else failwith "Value in version.yml must adhere to the SemanticVersion 2.0 Spec"

let packageVersion = lazy(
    let localBranch =
        let gitBranch = Information.getBranchName "."

        match gitBranch with
        | "NoBranch" -> None
        | "master" -> Some version
        | _ ->
            Some (sprintf "%s-local%d" version (rnd.Next(1, 1000)))

    let travisBranch () =
        let isPr = env "TRAVIS_PULL_REQUEST" <> Some "false"
        if isPr then None
        else
        match env "TRAVIS_BRANCH", env "TRAVIS_BUILD_NUMBER" with
        | Some "master", _ -> Some version
        | Some _, Some buildNum -> Some (sprintf "%s-build%s" version buildNum)
        | _, _ -> Trace.log "Travis information not found"; None
    let ciBranch =
        let isTravis = env "Travis" <> Some "true"
        if isTravis
        then travisBranch()
        else None

    if env "CI" <> Some "true" then ciBranch else localBranch)


let buildDir = "build"

assertVersion version

let inline withVersionArgs version options =
    options |> DotNet.Options.withCustomParams (Some(sprintf "/p:VersionPrefix=\"%s\"" version))


let getProjFolders projPath =
    let dir = Path.GetDirectoryName projPath
    [ sprintf "%s/bin" dir
      sprintf "%s/obj" dir ]

// *** Define Targets ***
Target.create "Clean" (fun _ ->
    let projFoldersToDelete =
        !!"*/*.csproj" |> List.ofSeq |> List.collect getProjFolders
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
    match packageVersion.Value with
    | Some v -> packProject v "Resultful/Resultful.csproj"
    | None -> ())
Target.create "Publish" (fun _ ->
    let nugetKeyVariable = "NUGET_KEY"
    let publishPackage () =
        Fake.DotNet.Paket.push (fun p -> { p with WorkingDir = "build"; PublishUrl = "https://www.myget.org/F/resultful" })
    match env nugetKeyVariable, packageVersion.Value with
    | Some _, Some _ ->  publishPackage()
    | _ -> Trace.log (sprintf "Package upload skipped because %s was not found and/or No package packed" nugetKeyVariable))

// *** Define Dependencies ***
open Fake.Core.TargetOperators

"Clean" ==> "Build" ==> "Test" ==> "Package" ==> "Publish"
// *** Start Build ***
Fake.Core.Target.runOrDefault "Package"
