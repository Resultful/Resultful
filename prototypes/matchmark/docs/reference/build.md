# Build settings

The generator (`Matchmark.Generator`) is a Roslyn source generator. A project marks its spec as an `AdditionalFiles`
item, and the generator writes code into the compilation at build time. Nothing it writes is checked in
(principle 6). Generated files can be read under `obj/generated/` when the project sets
`EmitCompilerGeneratedFiles`.

```xml
<ItemGroup>
  <PackageReference Include="Matchmark.Generator" />
  <AdditionalFiles Include="petstore.yaml" Matchmark="true" MatchmarkApiName="PetStore"
                   MatchmarkNamespace="PetStore.Api" MatchmarkParts="Contract,Server" />
</ItemGroup>
```

The package declares its own `CompilerVisibleItemMetadata` (`build/Matchmark.Generator.props`), so a project only marks
its files. Inside this repository the samples reference the generator project directly as an analyzer
(`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`).

## Item metadata

| Metadata | On | Values | Default | Meaning |
|:--|:--|:--|:--|:--|
| `Matchmark` | a spec | `true` | | This file is a spec to generate from. OpenAPI 2.0 to 3.2, or AsyncAPI 3 (read as webhooks, one per operation). |
| `MatchmarkApiName` | a spec, or a companion | a name | from the file name, in Pascal case | Prefixes generated type names (`PetStoreClient`, `IPetStoreWebhooksHandlers`). Companions join the spec with the same name. |
| `MatchmarkNamespace` | a spec | a namespace | the project's `RootNamespace`, else `Generated` | Where the generated code lives. |
| `MatchmarkParts` | a spec | comma-separated parts | `Contract,Server` | What to generate (see below). Separate with commas: the value reaches the generator through an `.editorconfig`, where `;` starts a comment. |
| `MatchmarkProblems` | a spec | `Owned` | | The contract generates its own problem types, so a contract-only (client) project needs no ASP.NET Core. Reported as `MM026` alongside a server part. |
| `MatchmarkAudit` | a spec | `true` | | Report what the spec leaves out as `MM024` diagnostics, as `matchmark audit` does. |
| `MatchmarkArazzo` | a companion | `true` | | An Arazzo document for the spec of the same `MatchmarkApiName`: its workflows join the generated regression suite. |
| `MatchmarkOverlay` | a companion | `true` | | An Overlay applied to the spec of the same `MatchmarkApiName` before anything is generated. |

## Parts

| Part | Generates | Typical project |
|:--|:--|:--|
| `Contract` | Models, outcome unions, the handler interface (`I{Api}Handlers`), request validation, the validating decorator, the typed client, and, with `Matchmark.Fakes` referenced, the fake and scenario builder. | A contract library that services, clients and tests share. |
| `Server` | Minimal API endpoints (`Map{Api}`), and the OpenAPI configuration when `Microsoft.AspNetCore.OpenApi` is referenced. | The service. |
| `FakeServer` | Hosting for the fake as a server: the session hub, the control API and `{Api}FakeServer`. | A fake server: `Server,FakeServer`. |
| `Regression` | `{Api}Regression`: the spec as JSON, its Arazzo workflows, and the suite built once from them. Needs `Matchmark.Testing` (`MM006` without it). | A test project. |
| `Verify` | Compile-time verification of hand-written controllers and Map calls against the spec (`MM010` to `MM023`). | A scaffolded service you own. |

Generated files are named `{Api}.{Kind}.g.cs`: `Models`, `Validation`, `Responses`, `Handlers`, `Client`, `Fake`,
`Endpoints`, `OpenApi`, `FakeHosting`, `Regression`, and the `Webhooks.*` and `Callbacks.*` equivalents when the spec
has webhooks or callbacks.

## References that switch features on

The generator looks for these types in the compilation, so a feature is on exactly when its runtime is referenced.

| Referenced | Detected by | Switches on |
|:--|:--|:--|
| `Microsoft.AspNetCore.OpenApi` | `OpenApiOptions` | The service describes itself: the OpenAPI document configuration the round-trip check compares. |
| `Matchmark.Fakes` | `Matchmark.Fakes.Scenario` | The generated fake and scenario builder, in the `Contract` part. |
| `Matchmark.Testing` | `Matchmark.Testing.RegressionSuite` | The `Regression` part. |
| `Matchmark.Rules` | `Matchmark.Rules.Cel` | Enforcement of `x-matchmark-rules`, `x-matchmark-transitions` and `x-dependencies` in the server and client (`MM025` without it). |

## Packages

| Package | Status | Contents |
|:--|:--|:--|
| `Matchmark.Generator` | Built (packed to a local feed) | The source generator, netstandard2.0, with Microsoft.OpenApi inside the analyzer. |
| `Matchmark.Fakes` | Built | The runtime for generated fakes: `Scenario`, `ScenarioRunner`, `FakeCall`. |
| `Matchmark.Testing` | Built | The regression engine, twin and fidelity. Needs only System.Text.Json. |
| `Matchmark.Rules` | Built | CEL and IDL evaluators. |
| `Matchmark.Conformance` | Built | `UseMatchmarkConformance(spec)` middleware, in observe or enforce mode. |
| `Matchmark.Cli` | Built | The `matchmark` dotnet tool. |
| Container image | Planned (Q17) | The CLI, for teams without the .NET SDK. |

Generated code depends on Resultful and OneOf today. Plain unions by default, with Resultful as an opt-in adapter, is
planned (Q12, roadmap step 1).
