
#r "paket:
    nuget FSharp.Core 5.0.0.0
    nuget Fake.Core.Target
    nuget Fake.Core.ReleaseNotes
    nuget Fake.DotNet.Cli
    nuget Fake.Tools.Git //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.Tools.Git
open System.IO
open System

let buildDir = "build"
let testResultDir = "testResults"
let packageVersion = "0.2.0"
let packageFeed = "https://www.myget.org/F/resultful/api/v2/package"

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

let packageVersionSemVer = lazy(
    // let version = env "PACKAGE_VERSION" |> Option.defaultWith (failwith "PACKAGE_VERSION not provided in environment")
    let semVerVersion = assertVersion packageVersion

    let shortVersion = sprintf "%d.%d.%d" semVerVersion.Major semVerVersion.Minor semVerVersion.Patch
    let localVersion () =
        let gitBranch = Information.getBranchName "."
        match gitBranch with
        | "main" -> Some semVerVersion
        | _ ->
            let rnd = Random()
            // Need to figure out what to do with this case
            (sprintf "%s-alpha.build%04i+experiment.%s" shortVersion (rnd.Next(1, 1000)) gitBranch) |> assertVersion |> Some

    let githubVersion () =
        let branch = Information.getBranchName "."
        let buildNum = envStrict "GITHUB_RUN_NUMBER" |> int32
        let pr = envStrict "GITHUB_EVENT_NAME"
        if pr = "pull_request" then
            let prBranch = envStrict "GITHUB_HEAD_REF"
            // eg 2.0.1-cipr004+BranchNum Build 4
            sprintf "%s-alpha.cipr%03i+%s" shortVersion buildNum prBranch |> assertVersion |> Some
        elif branch = "main" then
            Some semVerVersion
        else
            failwith "Must be either main or PR change"
            // Need to figure out what to do with this case
            //Some (sprintf "%s-ci%04i" version buildNum)

    let result = if env "CI" = Some "true" then githubVersion() else localVersion()
    match result with
    | Some x -> Trace.logfn "Version to package %A" x
    | None -> Trace.log "No Version generated, so no package will be generated"
    result)

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
    let allFoldersToClean = (testResultDir :: buildDir :: projFoldersToDelete )
    Trace.logfn "All folders to clean: %A" allFoldersToClean
    Shell.cleanDirs allFoldersToClean)
Target.create "Build" (fun _ -> DotNet.build (fun p -> { p with Configuration = DotNet.BuildConfiguration.Release }) "")
Target.create "Test" (fun _ ->
    let test project =
        DotNet.test (fun p ->
            { p with Configuration = DotNet.BuildConfiguration.Release
                     NoBuild = true
                     Logger = Some "nunit;LogFilePath=test-results.xml"
                     TestAdapterPath = Some "." }) project
    test "Resultful.Tests"
    Directory.create(testResultDir)
    for file in !! "./**/test-results.xml" do
        let dirName = Path.GetFileName(Path.GetDirectoryName(file))
        let newPath = sprintf "./%s/test-results-%s.xml" testResultDir dirName
        File.Move(file, newPath) )
Target.create "Package" (fun _ ->
    Directory.ensure buildDir
    let packProject (v: string) projectPath =
        DotNet.pack (fun p ->
            { p with Configuration = DotNet.BuildConfiguration.Release
                     OutputPath = Some(sprintf "./%s" buildDir)
                     NoBuild = true }
            |> withVersionArgs (v)) projectPath
    match packageVersionSemVer.Value with
    | Some v ->
        packProject (v.ToString()) "Resultful/Resultful.csproj"
    | None -> ())
Target.create "Publish" (fun _ ->
    let nugetKeyVariable = "NUGET_KEY"
    let publishPackage apiKey (version: SemVerInfo) =
        let packagePath = sprintf "./%s/Resultful.%s.nupkg"  buildDir (version.Normalize())
        runDotNet (sprintf "nuget push -k %s -s %s %s" apiKey packageFeed packagePath ) "."
    match envSecret nugetKeyVariable, packageVersionSemVer.Value with
    | Some key, Some ver -> publishPackage key ver
    | x, y -> Trace.logfn "Package upload skipped because %s no API Key: %A and/or package version %A" nugetKeyVariable x y)

// *** Define Dependencies ***
open Fake.Core.TargetOperators

"Clean" ==> "Build" ==> "Test" ==> "Package" ==> "Publish"
// *** Start Build ***
Fake.Core.Target.runOrDefault "Package"
