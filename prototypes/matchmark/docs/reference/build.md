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
| `MatchmarkOutcomes` | a spec | `Plain`, `Union`, `Resultful` | by language version: `Union` on C# 15, `Plain` before | The contract's own outcome classes (Q12), C# 15 unions (Q55), or Resultful's and OneOf's. Unset, the project's language version decides. Every project generating from the spec, or sharing its contract, gives the same value. |
| `MatchmarkSpecPart` | a companion | `true` | | Another file of a spec split across files, joined by `MatchmarkApiName`: the generator bundles the spec from its parts before reading it ([cli: bundle](cli.md#bundle) does the same on the command line). |
| `MatchmarkClientValidation` | a spec | `Net` | Matchmark's own | The generated client checks requests on .NET's validation layer, with the same keys as the server ([validation](validation.md), question 3). |
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
| `Matchmark.Messaging` | `Matchmark.Messaging.IMessageTransport` | **Built.** For an AsyncAPI document, in the `Contract` part: `I{Api}Consumer` (its receive operations), `{Api}Producer` (every operation's message, checked before it's sent) and `Subscribe{Api}` (the consumer on its channels, each message checked as it arrives, and refused ones given to a dead-letter handler). See [Messaging](#messaging). |
| `Microsoft.Extensions.Validation` | **Built** (Q45) | Validation attributes on models, a generated `{Api}ValidationResolver`, registered by `Add{Api}()` ahead of Matchmark's own validator ([validation](validation.md)). In the ASP.NET Core shared framework from .NET 10. |

## Packages

| Package | Status | Contents |
|:--|:--|:--|
| `Matchmark.Generator` | Built (packed to a local feed) | The source generator, netstandard2.0, with Microsoft.OpenApi inside the analyzer. |
| `Matchmark.Fakes` | Built | The runtime for generated fakes: `Scenario`, `ScenarioRunner`, `FakeCall`. |
| `Matchmark.Testing` | Built | The regression engine, twin and fidelity. Needs only System.Text.Json. |
| `Matchmark.Rules` | Built | CEL and IDL evaluators. |
| `Matchmark.Messaging` | **Built** | The transport the generated AsyncAPI producer and consumer use (`IMessageTransport`), channel addresses, and `InMemoryTransport` for tests. |
| `Matchmark.Messaging.Nats` | **Built** | NATS as the transport (`NatsTransport`, on NATS.Client.Core, Apache-2.0). |
| `Matchmark.Conformance` | Built | `UseMatchmarkConformance(spec)` middleware, in observe or enforce mode. |
| `Matchmark.Cli` | Built | The `matchmark` dotnet tool. |
| Container image | **Built** (Q17) | The CLI, for teams without the .NET SDK: `Dockerfile`, built from the repository root (`docker build -t matchmark .`), on ASP.NET Core's runtime image so `host` and `twin` serve HTTP; the working directory is `/work`, for a mounted spec. CI builds it, audits a sample with it and hosts a session; built and smoke-tested here too (367 MB). Behind a TLS-inspecting proxy, pass its certificate as a build secret (`--secret id=ca,src=proxy.crt`) with the usual `HTTPS_PROXY` build argument. Publishing it to a registry waits on publishing being set up (Q48). |
| `Matchmark.Validation` | **Built** (Q45) | Validation attributes .NET lacks: `[MultipleOf]`, `[UniqueItems]`, `[PropertyCount]`. Generated onto models when referenced. |

**Plain outcomes by default (Q12, Built).** A generated contract carries its own outcome types: `Result<T, TError>`,
`VoidResult<TError>`, `Unit`, `Result.Ok` and `Result.Fail`, and each operation's union as a class with `Match`,
`Switch`, `Value` and `Index`, so it depends on neither Resultful nor OneOf (`ApiModelTests` compiles a contract and
server with neither referenced). The members keep those libraries' names, so a handler reads the same either way.
**C# 15 unions by language version (Q55, Built).** On C# 15 (.NET 11), each operation's outcomes are a C# 15 union over
its case records (`public union GetPetError(GetPetNotFound)`), so a consumer's `switch` over them needs no default arm,
and the compiler warns when a case is missing. The union keeps the class's factories (`GetPetError.NotFound(...)`),
`Match`, `Switch` and `Index`, so handlers, clients and fakes read the same in both shapes. On a runtime without the
union types (.NET 10, with `LangVersion` set to `preview`), the generator writes `UnionAttribute` and `IUnion`, internal,
once per project. `UnionOutcomeTests` proves the shape (with Roslyn 5.9). `EmittedCodeTests` compiles every sample in all
three shapes with no warning, and `LedgerTests` runs the Ledger's generated server through its suite in both.

`MatchmarkOutcomes="Resultful"` generates onto Resultful and OneOf instead, the adapter, for code that uses their wider
API (`ToOneOf`, `Map`, `Bind`): the Pet Store sample does. `matchmark scaffold --outcomes resultful` writes owned code
to match.

## Messaging

**Built.** An AsyncAPI document on a broker. With `Matchmark.Messaging` referenced, the `Contract` part of an AsyncAPI
spec adds three types to the webhooks it already generates, which still back the HTTP bridge and the regression suite:

| Generated | What it is |
|:--|:--|
| `I{Api}Consumer` | One method per receive operation: the channel's parameters, the message's headers (typed by its headers schema), and the payload's model. The application implements it. |
| `{Api}Producer` | One method per operation, on an `IMessageTransport`. It checks a channel parameter against its enum and the payload against its schema, then sends to the channel's address, with the headers and the content type. A message that breaks the document throws `MessageRejectedException` and is never sent. The send operations are the application's own; the receive operations' methods are for its counterpart, such as a test. |
| `Subscribe{Api}(transport, consumer, rejected)` | Subscribes the consumer to each receive operation's channel. A message reaches it only when its content type, headers and payload match the document. One that doesn't goes to `rejected`, a dead-letter handler, with every error by key. A message whose address parameter isn't one of the channel's values belongs to another channel (addresses can overlap: `notifications.{topic}` and `notifications.redeliver`), so it's left alone. |

A channel address's parameter is one segment between its separators (`.` or `/`). `ChannelAddress.Format` refuses a
value that holds a separator or a wildcard, so a parameter can't widen the address.

Transports: `InMemoryTransport`, for tests, and `NatsTransport` (`Matchmark.Messaging.Nats`), where an address is a
subject, a parameter is a `*` token, and headers and the content type travel as NATS headers. `MessagingTests` runs the
same exchange on both, the NATS run against CI's service container (`MATCHMARK_NATS_URL`): a valid message taken typed,
an invalid one refused by the producer, two sent by hand refused as they arrive, and a notification to its address.
