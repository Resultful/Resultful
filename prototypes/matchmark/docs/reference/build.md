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
| `MatchmarkOutcomes` | a spec | `Plain` (default), `Resultful` | `Plain` | The contract's own outcome types, or Resultful's and OneOf's (Q12). Every project generating from the spec gives the same value. |
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
| `Microsoft.Extensions.Validation` | **Built** (Q45) | Validation attributes on models, a generated `{Api}ValidationResolver`, registered by `Add{Api}()` ahead of Matchmark's own validator ([validation](validation.md)). In the ASP.NET Core shared framework from .NET 10. |

## Packages

| Package | Status | Contents |
|:--|:--|:--|
| `Matchmark.Generator` | Built (packed to a local feed) | The source generator, netstandard2.0, with Microsoft.OpenApi inside the analyzer. |
| `Matchmark.Fakes` | Built | The runtime for generated fakes: `Scenario`, `ScenarioRunner`, `FakeCall`. |
| `Matchmark.Testing` | Built | The regression engine, twin and fidelity. Needs only System.Text.Json. |
| `Matchmark.Rules` | Built | CEL and IDL evaluators. |
| `Matchmark.Conformance` | Built | `UseMatchmarkConformance(spec)` middleware, in observe or enforce mode. |
| `Matchmark.Cli` | Built | The `matchmark` dotnet tool. |
| Container image | **Built** (Q17) | The CLI, for teams without the .NET SDK: `prototypes/matchmark/Dockerfile`, built from the repository root (`docker build -f prototypes/matchmark/Dockerfile -t matchmark .`), on ASP.NET Core's runtime image so `host` and `twin` serve HTTP; the working directory is `/work`, for a mounted spec. CI builds it, audits a sample with it and hosts a session; built and smoke-tested here too (367 MB). Behind a TLS-inspecting proxy, pass its certificate as a build secret (`--secret id=ca,src=proxy.crt`) with the usual `HTTPS_PROXY` build argument. Publishing it to a registry waits on Matchmark's own repository (Q48). |
| `Matchmark.Validation` | **Built** (Q45) | Validation attributes .NET lacks: `[MultipleOf]`, `[UniqueItems]`, `[PropertyCount]`. Generated onto models when referenced. |

**Plain outcomes by default (Q12, Built).** A generated contract carries its own outcome types: `Result<T, TError>`,
`VoidResult<TError>`, `Unit`, `Result.Ok` and `Result.Fail`, and each operation's union as a class with `Match`,
`Switch`, `Value` and `Index`, so it depends on neither Resultful nor OneOf (`ApiModelTests` compiles a contract and
server with neither referenced). The members keep those libraries' names, so a handler reads the same either way.
`MatchmarkOutcomes="Resultful"` generates onto Resultful and OneOf instead, the adapter, for code that uses their wider
API (`ToOneOf`, `Map`, `Bind`): the Pet Store sample does. `matchmark scaffold --outcomes resultful` writes owned code
to match.
