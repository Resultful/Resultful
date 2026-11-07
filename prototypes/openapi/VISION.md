# Vision

Where this project is going, and why. [CONCEPT.md](CONCEPT.md) describes how the prototype works today,
[PETSTORE.md](PETSTORE.md) walks through the sample, and the [README](README.md) covers the mechanics.

## The problem

An API has three descriptions of itself, and they drift apart:

- **The spec** says what the API should do.
- **The code** decides what it actually does.
- **The tests** check whatever someone thought to check.

Code-first tools keep the spec in step by deriving it from the code, so the spec records accidents as well as
decisions. Spec-first tools generate a skeleton once and then lose track of it. Neither keeps all three in step, and
none can say which documented behaviours the tests actually reach.

## The thesis

The spec should be an **enforceable contract** for the whole life of an API. It should fix everything except the
behaviour inside each operation, and independent checks should prove that the code, the running service and the tests
all match it:

- **The compiler** proves completeness. Every documented outcome is a case of a union, so leaving one unhandled
  doesn't compile and returning an undocumented one can't be written.
- **The round trip** proves the running service describes itself exactly as the spec does.
- **Conformance** proves every response a caller sees is one the spec allows.
- **Outcome coverage** proves the tests reach every documented behaviour.

The contract is an interface (`IPetsHandlers`), not an HTTP surface. The service, the HTTP client, test fakes and
adapters to legacy systems are all implementations of it. One test suite runs against any of them.

## What it becomes

**For teams building services,** the default way to build an API on .NET:
1. Change the spec in a reviewed pull request.
2. Let the compiler show what that change requires of handlers and consumers.
3. Ship when the gates are green.

Consumers get a typed client in which every error is a case they must handle. Breaking changes are judged against what
consumers actually use, not against the whole spec.

**For teams testing APIs,** a framework that checks any API against its spec:
- **Which APIs:** code-first, legacy, or not .NET at all, with no change to the service.
- **Every call is a conformance check,** and the spec's own examples become test cases.
- **Coverage is measured in documented behaviours,** not lines.
- **Migrations are safe:** the same suite runs against the old and new implementations, and the results are compared
  outcome by outcome.

**For teams consuming APIs,** a fake server that costs nothing to build:
- **Generated from the spec.** No hand-written behaviour, no database. It answers with the spec's examples, or with
  samples built to pass the schema.
- **Behaves like the real thing at the edges.** It uses the real service's generated endpoints, so it validates
  requests, writes problems as `problem+json` and publishes the same OpenAPI document.
- **Scripted per test.** Any documented outcome can be scripted in-process with typed functions, or over HTTP from any
  language. An undocumented one can't be scripted, so a consumer can't test against behaviour the API will never have.
- **Asserts how it is used.** A scenario, written in code or declared in a file, says which calls to expect, in what
  order, and what each request must contain. The fake fails the first wrong call and reports everything that went
  differently.
- **Fluent, in .NET.** `server.Given.GetPet(petId: 7).Returns.NotFound()` and
  `server.Should().HaveReceived.GetPet(petId: 7).Once()`, against a real server on a real port, typed from the spec,
  with messages that say what was received.
- **One server, many isolated fakes.** Posting a scenario to the fake server stands up a fresh fake API at its own base
  URL, so parallel tests, in any language, share one cheap process.
- **Available from day one.** It exists as soon as the spec does, so consumers build against it while the service is
  still being written.

**Both directions from one spec:**
- **Generated behaviour** you never touch: the fake.
- **Owned code** you start from and keep: scaffolding writes ordinary ASP.NET controllers and a reference
  implementation that answers with the spec's examples, so the service runs and conforms on day one. Then the team
  replaces one handler body at a time. It is also the way off the generator: the contract stays generated, and the
  server becomes plain ASP.NET.
- **And owned code stays honest.** The generator checks hand-written controllers and Map calls against the spec at
  compile time: a missing operation, an undocumented endpoint, a response or name that differs, or a parameter or body
  bound from the wrong place, with the wrong type or as required when it's optional, is a build error at the line
  that's wrong. Models are consistent by construction: the generated types *are* the schemas. A hand-written model is
  compared with its schema as JSON, constraints included, and each difference is reported at its path. Controllers and
  Minimal API are checked alike, responses included, read the way ASP.NET Core's own OpenAPI document reads them.

The three uses share one engine. Most teams will start with testing or the fake, because neither asks anything of
the service, and move to spec-first services for new work.

## Principles

1. **Decisions belong in the spec.** Status codes, errors and constraints are API design, not implementation side
   effects. Anything the spec can't express belongs in an extension or an overlay, never in a hidden convention.
2. **The compiler is the first test.** Use types to make wrong code fail to build. Leave runtime checks for what types
   cannot express.
3. **Errors are data.** Every documented failure is a value with a type, on the server and in the client. No outcome is
   only a status code to remember.
4. **One contract, many implementations.** Server, client, fake and legacy adapter implement the same interface. Tests
   target the interface.
5. **Every check is checked.** A gate counts only if breaking the thing it guards makes it fail; each one here was
   broken on purpose to prove it. Nothing is excused silently: when the generated output and the spec differ, the
   difference is fixed or named.
6. **Nothing generated is edited by hand.** Code is generated at build time and never checked in, so it can't drift from
   its spec or overwrite anyone's changes.
7. **Adoptable one step at a time.** Each step is useful on its own, starting with a client in an existing test
   project.

## What we learn from others

| From | Idea | How it lands here |
|:--|:--|:--|
| [Kiota](https://learn.microsoft.com/en-us/openapi/kiota/extending/architecture), [Smithy](https://smithy.io/2.0/) | A language-neutral model between the definition and the code | A small model of operations, outcome unions, schemas and traits. OpenAPI is the first input; the contract, server, client and tests are outputs. |
| [Smithy protocol compliance tests](https://smithy.io/2.0/additional-specs/http-protocol-compliance-tests.html) | Tests are data inside the definition | The spec's `examples`, and malformed requests, become generated cases in the contract suite. |
| Smithy traits, [TypeSpec](https://typespec.io/docs/emitters/openapi3/openapi/) decorators | Meaning the base format can't express | Retryable errors, pagination and idempotency as extensions, turned into client behaviour, server behaviour and test properties. |
| [OpenAPI Overlays](https://spec.openapis.org/overlay/v1.0.0) | Changes to a spec kept in a separate file | Generator hints live outside the public spec. |
| [buf breaking rules](https://buf.build/docs/breaking/rules/) | "Breaking" depends on who is affected | Every spec change is graded twice: does it break generated code, and does it break the JSON on the wire? |
| Pact | Consumers state what they rely on | Each consumer's recorded outcomes (`Observed`) become its contract, with no pact files to write. |
| TypeSpec versioning | Many versions from one source | v1 and v2 contracts side by side, with generated adapters. |
| [Effect HttpApi](https://effect.website/docs/v4/api/openapi-generator/HttpApiTransformer) | One definition drives the server, client and docs, in either direction | Spec-first versus code-first stops mattering once the round trip is enforced. |

**What we don't take:** a separate definition language as a requirement (TypeSpec and Smithy are welcome as *inputs*),
template generators' sprawl of options, and reflection that nothing checks.

## Roadmap

Each stage is useful on its own. Items marked ✓ exist in the prototype.

**0. Proven in the prototype.**
- ✓ The generated contract: types, outcome unions and handler interfaces.
- ✓ Generated request validation and the validating decorator.
- ✓ The round-trip check against ASP.NET Core's own document, with no differences excused.
- ✓ A typed client that implements the contract and checks every response.
- ✓ One contract suite run in-process and over HTTP, with outcome coverage.
- ✓ Contract and server generated as separate parts.
- ✓ A generated fake: in-process, or as a runnable server scripted over HTTP. Every documented outcome it can produce
  is checked against the spec.
- ✓ Scenarios of expected calls, in order or not, written in code or JSON and checked against the spec; a fake server
  that stands up an isolated fake API per scenario.
- ✓ A fluent fake server for .NET tests: typed `Given` rules and `Should()` assertions, on a real port.
- ✓ Scaffolding the server as owned ASP.NET source (controllers or Minimal API) with a reference implementation, checked
  against the spec before anyone edits it.
- ✓ Compile-time verification that hand-written endpoints cover the spec, and nothing else: routes, names, responses,
  parameter binding, and model shapes and constraints.

**1. Usable by someone else.**
- A NuGet package that declares its build settings itself.
- Problem types the contract owns, so a client-only package doesn't need ASP.NET Core.
- A lenient client mode that logs violations instead of throwing.
- A run against a large real spec, fixing what it finds.

**2. The model in between.**
- Refactor the generator around a small semantic model.
- Test the model directly, independent of any output.

**3. The spec tests itself.**
- Examples and malformed requests become generated compliance cases.
- Runtime conformance middleware in two modes: *observe* in production, *enforce* in tests.

**4. Change without fear.**
- Spec diffs in CI, graded at the source and wire levels.
- Each change checked against consumers' recorded outcomes.
- Versioned contracts with generated adapters.

**5. Behaviour, not only shape.**
- Arazzo workflows run as stateful tests.
- Inputs generated from the schemas (property-based testing), starting from the seed scenarios.
- A differential runner comparing old and new implementations.

**6. Beyond HTTP.**
- AsyncAPI for events, and other hosts, through the same model.
- Traits for retries, paging and idempotency.

## How we'll know it's working

- **A spec change reaches every affected handler and consumer as a compile error** before it reaches a reviewer,
  whether the HTTP layer is generated or written by hand.
- **Teams can state their outcome coverage,** and it's above 90%.
- **Undocumented responses seen in production trend to zero** once observe mode is on.
- **A legacy API gets a conforming test suite in a day,** with no change to the service.
- **A consumer starts against a fake server in minutes,** before the service exists, and their tests carry over to
  the real one unchanged.
- **A breaking change to a used outcome can't merge** without a new version.

## Out of scope

- **Generating handler logic.** The behaviour inside an operation is the one thing deliberately left free; it belongs
  to people, or to an agent working within the contract and its tests.
- **Replacing ASP.NET Core.** The server part generates ordinary Minimal API endpoints.
- **Covering all of OpenAPI.** The supported subset grows when real specs need it, and anything unsupported is reported,
  never silently mis-generated.
