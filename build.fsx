#r "packages/build-deps/FAKE/tools/FakeLib.dll"
#r "packages/build-deps/FSharp.Configuration/lib/net45/FSharp.Configuration.dll"

open System
open Fake
open Fake.Core
open Fake.IO
open Fake.Core.TargetOperators
open FSharp.Configuration

type VersionConfig = YamlConfig<"version.yml">
let versionFile = VersionConfig()

let getVersion inputStr =
    if SemVer.isValidSemVer inputStr then
        SemVer.parse inputStr
    else
        failwith "Value in version.yml must adhere to the SemanticVersion 2.0 Spec"

let versionToPublish  = getVersion versionFile.Version

let globalTimeout = TimeSpan.FromSeconds 30.;

let runCommand execuatable command timeout  =
    let exitCode =
        Process.ExecProcess (fun info ->
        { info with
            FileName = execuatable
            Arguments = command
        }) (timeout)

    if exitCode <> 0 then failwithf "Look at error for  command %s %s" execuatable command

let runMonoCommand timeout command  =
    runCommand "mono" command timeout

let runPaketCommand timeout paketCommand =
    sprintf ".paket/paket.exe %s" paketCommand |> runMonoCommand timeout

let buildDir = "./build"

let publishPackage version =
    let finalVersion = version.ToString();
    sprintf "push build/OneOf.ROP.%s.nupkg" finalVersion
        |> runPaketCommand globalTimeout

let nugetKeyVariable =
    "NUGET_KEY"

// *** Define Targets ***
Target.Create "Clean" (fun _ ->
    let projects = [
        "./OneOf.ROP"
        "./OneOf.ROP.Tests"
    ]

    let allFoledersToClean =
        projects
        |> List.collect (fun project -> [ sprintf "%s/bin" project ; sprintf "%s/obj" project ])

    Shell.CleanDirs (buildDir :: allFoledersToClean)
)

Target.Create "Build" (fun _ ->
    DotNetCli.Build (fun p ->
        { p with
            TimeOut = globalTimeout;
            Configuration = "Release";
        })
)

Target.Create "Test" (fun _ ->
    DotNetCli.Test (fun p ->
        { p with
            TimeOut = globalTimeout;
            Configuration = "Release";
            Project = "OneOf.ROP.Tests";
            AdditionalArgs = [ "--no-build" ; ]
        })
)

Target.Create "Package" (fun _ ->
    Directory.ensure buildDir
    let finalVersion = versionToPublish.ToString();
    Fake.DotNetCli.Pack(fun p ->
        { p with
            TimeOut = globalTimeout;
            Configuration = "Release";
            OutputPath = "../build";
            AdditionalArgs = [ "--no-build"; sprintf "/p:VersionPrefix=\"%s\"" finalVersion ; ]//"--include-source" ;  "--include-symbols"  ]
        })
)

Target.Create "Publish" (fun _ ->
    match environVarOrNone nugetKeyVariable with
    | Some _ -> publishPackage versionToPublish
    | None -> Trace.log (sprintf "Package upload skipped because %s was not found" nugetKeyVariable)
)


// *** Define Dependencies ***
"Clean"
    ==> "Build"

"Build"
    ==> "Package"
    ==> "Publish"

"Build"
    ==> "Test"
    ==> "Publish"

// *** Start Build ***
Target.RunOrDefault "Package"
