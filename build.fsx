
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
open System.IO
open System


let env x =
    let result = Environment.environVarOrNone x
    match result with
    | Some v -> Trace.logfn "ENV: %s - VALUE: %s" x v
    | None -> Trace.logfn "Did not find variable with name: %s" x
    result


let envSecret x =
    let result = Environment.environVarOrNone x
    match result with
    | Some v -> TraceSecrets.register v (sprintf "ENVSecret %s" x)
    | None -> ()
    result

let envStrict x=
    env x |> Option.defaultWith (fun () -> failwithf "Unable to get environmentVariable %s" x)

let runDotNet cmd workingDir =
    let result = DotNet.exec (DotNet.Options.withWorkingDirectory workingDir) cmd ""
    if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir

let assertVersion inputStr =
    if SemVer.isValid inputStr then SemVer.parse inputStr
    else failwithf "Value in version.yml must adhere to the SemanticVersion 2.0 Spec - %s" inputStr

let packageVersion = lazy(
    let version = env "PACKAGE_VERSION" |> Option.defaultValue "0.2.0-alpha01"
    let semVerVersion = assertVersion version

    let shortVersion = sprintf "%d.%d.%d" semVerVersion.Major semVerVersion.Minor semVerVersion.Patch
    let localBranch =
        let gitBranch = Information.getBranchName "."
        match gitBranch with
        | "master"
        | "develop" -> Some semVerVersion
        | _ ->
            let rnd = Random()
            // Need to figure out what to do with this case
            (sprintf "%s-alpha.build%04i+experiment.%s" shortVersion (rnd.Next(1, 1000)) gitBranch) |> assertVersion |> Some

    let travisBranch () =
        let branch = envStrict "TRAVIS_BRANCH"
        let buildNum = envStrict "TRAVIS_BUILD_NUMBER" |> int32
        let pr = envStrict "TRAVIS_PULL_REQUEST"
        if pr <> "false" then
            let prBranch = envStrict "TRAVIS_PULL_REQUEST_BRANCH"
            let prNumber = int32 pr
            // eg 2.0.1-cipr004+BranchNum003 PR 3 Build 4
            sprintf "%s-alpha.cipr%03i+%s%03i" shortVersion buildNum prBranch prNumber  |> assertVersion |> Some
        elif branch = "master" then
            Some semVerVersion
        else
            None
            // Need to figure out what to do with this case
            //Some (sprintf "%s-ci%04i" version buildNum)

    let ciBranch =
        let isTravis = env "TRAVIS" = Some "true"
        if isTravis
        then travisBranch()
        else None

    let result = if env "CI" = Some "true" then ciBranch else localBranch
    match result with
    | Some x -> Trace.logfn "Version to package %A" x
    | None -> Trace.log "No Version generated, so no package will be generated"
    result)


let buildDir = "build"


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
    let packProject (version: SemVerInfo) projectPath =
        DotNet.pack (fun p ->
            { p with Configuration = DotNet.BuildConfiguration.Release
                     OutputPath = Some(sprintf "../%s" buildDir)
                     NoBuild = true }
            |> withVersionArgs (version.ToString())) projectPath
    match packageVersion.Value with
    | Some v -> packProject v "Resultful/Resultful.csproj"
    | None -> ())
Target.create "Publish" (fun _ ->
    let nugetKeyVariable = "NUGET_KEY"
    let publishPackage apiKey (version: SemVerInfo) =
        let packagePath = sprintf "Resultful.%s.nupkg" (version.Normalize())
        runDotNet (sprintf "nuget push -k %s -s %s ./build/%s" apiKey "https://www.myget.org/F/resultful" packagePath ) "."
    match envSecret nugetKeyVariable, packageVersion.Value with
    | Some key, Some ver -> publishPackage key ver
    | x, y -> Trace.logfn "Package upload skipped because %s no API Key: %A and/or package version %A" nugetKeyVariable x y)

// *** Define Dependencies ***
open Fake.Core.TargetOperators

"Clean" ==> "Build" ==> "Test" ==> "Package" ==> "Publish"
// *** Start Build ***
Fake.Core.Target.runOrDefault "Package"
