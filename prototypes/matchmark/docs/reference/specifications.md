# Specifications and versions

Matchmark supports the OpenAPI family in full: OpenAPI, AsyncAPI, Arazzo and Overlay (decision Q19). GraphQL, gRPC and
Protocol Buffers are out of scope until someone needs them.

**Full support** means three things (Q20): every construct in a supported version is read; anything Matchmark can't
generate yet is reported by name, never mis-generated; and support is measured against each specification's official
examples, in a [support matrix](support-matrix.md) generated from the tests.

## Versions

| Format | The model holds | Read by forward porting | Written | Status today |
|:--|:--|:--|:--|:--|
| OpenAPI | 3.2 | 2.0, 3.0, 3.1 | 3.2, and 3.1 for ASP.NET Core 10 | **Partial.** 2.0 to 3.2 are read through Microsoft.OpenApi. The engine normalises to 3.1 JSON, and keeps 3.2 only when the input is 3.2. |
| AsyncAPI | 3.1 | 2.x, 3.0 | 3.1 | **Built.** 2.x is forward-ported by Matchmark's port of the official migration guide; 3.x is read by Matchmark's own reader, to be replaced (Q30). |
| Arazzo | 1.1 | 1.0 | 1.1 | **Built** by Matchmark's own reader (`Matchmark.Specs.Arazzo`, Q56), regressed against the specification in `specs/arazzo`. |
| Overlay | 1.2 | 1.0, 1.1 | | **Built** by Matchmark's own reader (`Matchmark.Specs.Overlay`, Q56), regressed against the specification in `specs/overlay`. |

## The version policy

- **One target per format (Q24).** The model holds the latest version of each. When a specification releases a new
  version, the model moves to it, and the version it replaces becomes a forward-port input.
- **Forward porting on read (Q22).** Older versions are converted up as they are read. Anything the conversion can't
  carry across is reported as a warning.
- **Viability (Q25).** An older version is supported when a maintained converter or an official migration guide covers
  it, and upgrading loses nothing the generated code or the tests rely on. Anything else is reported as unsupported,
  naming the version and why.
- **Writing down.** Where a consumer needs an older version, Matchmark writes it, converting down where nothing is lost
  and reporting anything that is. ASP.NET Core does the same when it writes `QUERY` into a 3.1 document as
  `x-oai-additionalOperations`.
- **How long (Q24).** A version stays supported for as long as a supported .NET release emits it. ASP.NET Core 10 (LTS)
  writes OpenAPI 3.1 by default; ASP.NET Core 11 writes 3.2. Both are first-class today.
- **`matchmark upgrade` (Q23).** The forward port as a command: it writes the migrated spec and lists what changed and
  what was lost ([command line](cli.md#upgrade)). **Built.**

## Libraries

Standard libraries where they're sufficient; an [Oxbow](https://github.com/rtkelly-labs/oxbow) fork where one falls
short of the model's version (Q28). A fork is upstream plus a short stack of recorded changes, each written so upstream
could take it and dropped once upstream releases it (Q33).

| Format | Library | Licence | Covers | Fork adds | Status |
|:--|:--|:--|:--|:--|:--|
| OpenAPI | [Microsoft.OpenApi](https://github.com/microsoft/OpenAPI.NET) 3.11 | MIT | 2.0 to 3.2 | nothing | **Built** |
| AsyncAPI | [`asyncapi/net-sdk`](https://github.com/asyncapi/net-sdk) 3.0.6 (formerly Neuroglia.AsyncApi) | Apache 2.0 | v2, v3.0; reads the corpus's 3.1 document too | 3.1 | **Measured** (`ReaderLibraryTests`): reads every corpus document. Not adopted: net8 and up only (question 5) |
| Arazzo | [BinkyLabs.OpenApi.Arazzo](https://github.com/BinkyLabs/openapi-arazzo-dotnet) 1.0.0-preview.4 | MIT | 1.0 | 1.1 | **Measured** (`ReaderLibraryTests`): reads the 1.0 documents, refuses every 1.1 one. Not adopted: net8 and up only, no 1.1 (question 5) |
| Overlay | [BinkyLabs.OpenApi.Overlays](https://github.com/BinkyLabs/openapi-overlays-dotnet) 3.0.0 | MIT, but depends on JsonPath.Net 3 and JsonPointer.Net 7 (Open Source Maintenance Fee EULA) | 1.0, 1.1 | 1.2 | **Set aside** under the licence rule: Matchmark's own reader stays (Q31's fallback; question 5) |

- The AsyncAPI SDK reads v2 and v3 but doesn't convert between them, and the official converter is a JavaScript
  package. AsyncAPI 2.x is forward-ported by `AsyncApiUpgrade`, Matchmark's C# port of the official guide *Migrating to
  v3*, section by section, kept inside Matchmark (Q52).
- The BinkyLabs libraries are pre-release, so they are pinned to exact versions.
- If a BinkyLabs library proves insufficient, Matchmark gets a reader of its own for that format (Q31).
- Matchmark's own readers are retired only when their replacement passes the same tests: the corpus ledger and the
  Arazzo, Overlay and AsyncAPI tests (Q32).
- Considered and set aside for AsyncAPI: [LEGO/AsyncAPI.NET](https://github.com/LEGO/AsyncAPI.NET), which follows
  Microsoft.OpenApi's style but carries a modified Apache 2.0 licence.

## How each format is read today

- **OpenAPI.** `SpecReader` reads YAML or JSON through Microsoft.OpenApi, applies any overlays first, and treats reader
  objections as warnings (`MM007`) when a document was built. The engine reads the normalised JSON.
- **AsyncAPI.** An AsyncAPI 3 document is read as OpenAPI webhooks, one per operation, its message's payload the body
  and its headers header parameters, answered `202` when taken, `400` when refused and `415` for the wrong content type.
  Everything generated for webhooks is then generated for its messages. `AsyncApiDocument` (in `Matchmark.Testing`)
  reads it directly for the consumer suite and for checking a producer's messages.
- **Arazzo.** `Matchmark.Specs.Arazzo` (netstandard2.0, shipped beside the generator) reads 1.0 and 1.1 into a typed
  `ArazzoDocument`, with each problem at its JSON pointer:
  - every step kind: OpenAPI operations, 1.1's AsyncAPI operations and channels, and other workflows;
  - parameters and reusable objects;
  - criteria, with 1.0's and 1.1's expression types;
  - selectors, payload replacements, success and failure actions, components;
  - workflow inputs, checked as JSON Schema 2020-12 the way its metaschema checks them (`JsonSchemaShape`).

  The generator reads the Regression part's documents through it. Its regression suite covers:
  - the 75 1.1 pass and fail fixtures;
  - the official 1.0 and 1.1 schemas, which agree with every fixture;
  - the examples, and the complete examples in the 1.0.0, 1.0.1 and 1.1.0 texts;
  - every mutation of every valid document, over 5,000 (each field removed, each value of the wrong type, unknown
    fields, arrays emptied or repeating), on which the reader and the official schema agree.

  The 1.1.0 text's complete example gives two AsyncAPI steps an `operationPath`, which its own schema refuses. That is
  pinned as an upstream inconsistency, and the reader refuses it too. `ArazzoDocument` in `Matchmark.Testing` runs the
  workflows: runtime expressions, criteria, `retry`, `goto`, `end`, nested workflows and message steps.
- **Overlay.** `Matchmark.Specs.Overlay` (netstandard2.0, shipped in the generator's analyzer folder) reads 1.0, 1.1 and
  1.2 into a typed `OverlayDocument`, with each problem at its JSON pointer. That covers 1.1's `copy` and
  `info.description`, and 1.2's `$self`, `components.actions` and reusable action references, escaped keys included.
  `OverlayApplier` applies `update`, `remove` and `copy` with RFC 9535 JSONPath targets and filters. On every mutation of every
  valid 1.0 and 1.1 document, the reader and the official schema agree. The generator applies
  overlays through it, and an overlay's problems are reported as reader problems (`MM007`). Its regression suite
  (`Matchmark.Specs.Tests`) covers the vendored specification:
  - all 132 pass and fail fixtures, each failing for the reason its name gives;
  - the official 1.0 and 1.1 schemas, which agree with every fixture (JsonSchema.Net 7.3.3 as the oracle);
  - the 8 compliant sets, each overlay applied to its input giving exactly its output;
  - the 32 complete examples in the three specification texts.
- **YAML.** `Matchmark.Specs` reads YAML by YAML 1.2's core schema, and `YamlTests` holds it to Microsoft.OpenApi's
  reading over all 296 vendored YAML files. Two differences are kept, both where Microsoft.OpenApi departs from YAML: it
  writes a null as a sentinel string (`openapi-json-null-sentinel-value-…`), and reads an empty value as an empty string.
  The generator's AsyncAPI, Arazzo, Overlay and bundle reading now goes through `Matchmark.Specs`, so those nulls arrive
  as nulls.

## Measured against the specifications' own examples

**Built** (decisions Q20, Q51, Q56). Each specification's examples are vendored in `specs/` at a pinned commit
(`specs/vendor.sh`, `specs/SOURCES.md`), and `OfficialExamplesTests` measures them in every test run; CI puts the ledger
in the run's summary. A pin moves in a change of its own, with the measurement before and after.

| Source, pinned | What passing means | 8 November 2026 |
|:--|:--|:--|
| `OAI/learn.openapis.org` `82b82f3`, `examples/` (2.0 to 3.2, the YAML of each) | read, then generated as a contract and server that compile | 2.0: 8/8 (`petstore-separate` after `matchmark bundle`); 3.0: 6/6; 3.1: 3/3; 3.2: 2/2 |
| `asyncapi/spec` `4166274`, `examples/` (3.0 and 3.1) | read | 24/24 |
| `OAI/Arazzo-Specification` `e9ee1c4`, `examples/` (1.0, 1.1) | read, with its workflows | 6/6 |
| `OAI/Overlay-Specification` `be39b56`, `compliant-sets/` | applied to the set's input, gives its output exactly | 8/8 |

What the first measurement found: the 3.1 `tictactoe` example's enum request body generated a call to a validator that
doesn't exist for enums (fixed); and the 2.0 `petstore-separate` example, split across files, couldn't be read; it's measured after `matchmark bundle`
now, and how the generator itself should see split files is [question 2](../QUESTIONS.md). `OAI/OpenAPI-Specification` itself no longer holds examples; they moved to
`learn.openapis.org`.

## Version facts this is written against

Checked on 8 November 2026.

| Specification | Latest | Released |
|:--|:--|:--|
| OpenAPI | 3.2 (3.2.1 text) | 3.2.0 in September 2025 |
| AsyncAPI | 3.1.0 | 31 January 2026, no breaking changes from 3.0 |
| Arazzo | 1.1.0 | |
| Overlay | 1.2.0 | |

| .NET | OpenAPI it writes | Options |
|:--|:--|:--|
| ASP.NET Core 10 (LTS) | 3.1 | 3.0 |
| ASP.NET Core 11 | 3.2 | 3.1, 3.0 |
