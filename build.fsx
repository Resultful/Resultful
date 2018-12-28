#r "paket: groupref build-deps //"
#load "./.fake/build.fsx/intellisense.fsx"
#if !FAKE
#r "netstandard"
#r "Facades/netstandard"

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

let gitBranch = Information.getBranchName ""

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
    let publishPackage shouldPublish project =
        if shouldPublish && gitBranch = "master" then
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

"Clean"
    ==> "Build"

"Build"
    ==> "Package"
    ==> "Publish"

"Build"
    ==> "Test"
    ==> "Publish"

// *** Start Build ***
Fake.Core.Target.runOrDefault "Package"
