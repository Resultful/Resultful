#r "paket: groupref build-deps //"
#load "./.fake/build.fsx/intellisense.fsx"
#if !FAKE
#r "netstandard"

#endif

open System
open System.IO
open Fake.Core
open Fake.IO
open Fake.DotNet
open Fake.Tools.Git

type Build = {
    Version: string
    Publish: bool
}

let config = {
    Version = "0.2.0-alpha01"
    Publish = true
}

let buildDir = "build"

let assertVersion inputStr =
    if Fake.Core.SemVer.isValid inputStr then
        ()
    else
        failwith "Value in version.yml must adhere to the SemanticVersion 2.0 Spec"

assertVersion config.Version


let inline withVersionArgs version options =
    options |> DotNet.Options.withCustomParams (Some (sprintf "/p:VersionPrefix=\"%s\"" version))


let nugetKeyVariable =
    "NUGET_KEY"

let getProjFolders projPath =
    let dir = Path.GetDirectoryName projPath
    [ sprintf "%s/bin" dir
      sprintf "%s/obj" dir ]

let runDotNet cmd workingDir =
    let result = DotNet.exec (DotNet.Options.withWorkingDirectory workingDir) cmd ""
    if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir

Target.create "PaketInstall" (fun _ ->
    let tool =
        if Environment.isUnix then "paket"
        else "paket.exe"

    let toolPath = (sprintf ".paket\\%s" tool)
    match ProcessUtils.tryFindFileOnPath toolPath with
    | Some _ -> runDotNet "tool update --tool-path \".paket\" Paket" "."
    | None -> runDotNet "tool install --tool-path \".paket\" Paket --add-source https://api.nuget.org/v3/index.json" "."
)

// *** Define Targets ***
Target.create "Clean" (fun _ ->
    let projects = [
        "./OneOf.ROP"
        "./OneOf.ROP.Examples"
        "./OneOf.ROP.Tests"
    ]

    let allFoldersToClean =
        projects
        |> List.collect (fun project -> getProjFolders project)

    Shell.cleanDirs (buildDir :: allFoldersToClean)
)

Target.create "Build" (fun _ ->
    DotNet.build (fun p ->
        { p with
            Configuration = DotNet.BuildConfiguration.Release;
        }) ""
)

Target.create "Test" (fun _ ->
    let test project =
        DotNet.test (fun p ->
            { p with
                Configuration = DotNet.BuildConfiguration.Release;
                NoBuild = true
            }) project

    test "OneOf.ROP.Tests"
)

Target.create "Package" (fun _ ->
    Directory.ensure buildDir
    let packProject version projectPath =
        DotNet.pack (fun p ->
            { p with
                Configuration = DotNet.BuildConfiguration.Release
                OutputPath = Some (sprintf "../%s" buildDir)
                NoBuild = true
            } |> withVersionArgs version) projectPath

    packProject config.Version "OneOf.ROP/OneOf.ROP.csproj"
)



Target.create "Publish" (fun _ ->
    let gitBranch = Information.getBranchName "."

    Trace.log (sprintf "Git branch: %s" gitBranch)
    //let isMaster = gitBranch = "master"
    let isMaster = false
    let publishPackage shouldPublish project =
        if shouldPublish && isMaster then
            Fake.DotNet.Paket.push (fun p ->
                { p with
                    WorkingDir = "build"
                })
        else
            Trace.log (sprintf "Package upload skipped because %s was not set to be published or the branch %s is not master" project gitBranch )

    match Environment.environVarOrNone nugetKeyVariable with
    | Some _ -> publishPackage config.Publish "OneOf.ROP"
    | None -> Trace.log (sprintf "Package upload skipped because %s was not found" nugetKeyVariable)
)

// *** Define Dependencies ***
open Fake.Core.TargetOperators

"PaketInstall"
    ==> "Clean"
    ==> "Build"

"Build"
    ==> "Package"
    ==> "Publish"

"Build"
    ==> "Test"
    ==> "Publish"

// *** Start Build ***
Fake.Core.Target.runOrDefault "Package"
