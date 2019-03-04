open Fake.Core
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

let env x =
    let result = Environment.environVarOrNone x
    match result with
    | Some v -> Trace.logfn "ENV: %s - VALUE: %s" x v
    | None -> Trace.logfn "Did not find variable with name: %s" x
    result

let envStrict x=
    env x |> Option.defaultWith (fun () -> failwithf "Unable to get environmentVariable %s" x)

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
        let isPr = envStrict "TRAVIS_PULL_REQUEST" <> "false"
        if isPr then None
        else
        match envStrict "TRAVIS_BRANCH", envStrict "TRAVIS_BUILD_NUMBER" with
        | "master", _ -> Some version
        | _, buildNum -> Some (sprintf "%s-build%s" version buildNum)
    let ciBranch =
        let isTravis = env "TRAVIS" <> Some "true"
        if isTravis
        then travisBranch()
        else None

    let result = if env "CI" = Some "true" then ciBranch else localBranch
    match result with
    | Some x -> Trace.logfn "Version to package %s" x
    | None -> Trace.log "No Version generated, so no package will be generated"
    result)


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
    match Environment.hasEnvironVar nugetKeyVariable, packageVersion.Value with
    | true, Some _ -> publishPackage()
    | x, y -> Trace.logfn "Package upload skipped because %s existed: %b and/or package version %A" nugetKeyVariable x y)

// *** Define Dependencies ***
open Fake.Core.TargetOperators

"Clean" ==> "Build" ==> "Test" ==> "Package" ==> "Publish"
// *** Start Build ***
Fake.Core.Target.runOrDefault "Package"
