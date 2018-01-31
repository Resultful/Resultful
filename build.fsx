#r "packages/build-deps/FAKE/tools/FakeLib.dll"
#r "packages/build-deps/FSharp.Configuration/lib/net45/FSharp.Configuration.dll"
#r "packages/build-deps/semver/lib/net452/Semver.dll"

open System
open Fake
open Fake.Core
open Fake.IO
open Fake.Core.TargetOperators
open FSharp.Configuration
open Semver;

type VersionConfig = YamlConfig<"version.yml">
let versionFile = VersionConfig()

let getVersion inputStr =
    let mutable value = null;
    if SemVersion.TryParse(inputStr, &value, true) then
        value
    else
        failwith "Value in version.yml must adhere to the SemanticVersion 2.0 Spec"

let versionToPublish  = getVersion versionFile.Version
let doPublish = versionFile.UploadPackage

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


// *** Define Targets ***
Target.Create "Clean" (fun _ ->
    DotNetCli.RunCommand(fun p ->
        { p with
            TimeOut = globalTimeout;
        })
        "clean -c \"Release\""
    Shell.CleanDir buildDir
)

Target.Create "Build" (fun _ ->
    DotNetCli.Build (fun p ->
        { p with
            TimeOut = globalTimeout;
            Configuration = "Release";
            AdditionalArgs = [ "--no-restore" ]
        })
)

Target.Create "Test" (fun _ ->
    DotNetCli.Test (fun p ->
        { p with
            TimeOut = globalTimeout;
            Project = "OneOf.ROP.Tests";
            AdditionalArgs = [ "--no-build" ;  "--no-restore" ]
        })
)

Target.Create "Package" (fun _ ->
    Directory.ensure buildDir
    let finalVersion = versionToPublish.ToString();
    sprintf "pack build --symbols --version %s --minimum-from-lock-file" finalVersion
        |> runPaketCommand globalTimeout
)

let publishPackage version =
    let finalVersion = version.ToString();
    sprintf "push build/OneOf.ROP.%s.symbols.nupkg" finalVersion
        |> runPaketCommand globalTimeout

Target.Create "Publish" (fun _ ->
    if doPublish then
        publishPackage versionToPublish
    else
        Trace.log "Because UploadPackage was false package upload skipped"
)

Target.Create "Test"

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
