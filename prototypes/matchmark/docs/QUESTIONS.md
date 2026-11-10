# Questions

What is still open. Each question names the assumption work proceeds on until it's answered, so nothing waits on it.
When one is answered, its answer moves to [decisions.md](decisions.md) with its date, and it's deleted here. Questions
answered so far are decisions Q44 to Q54.

**1. Should the test engine keep working on normalised JSON, or take the standard libraries as dependencies?** Once
Matchmark reads specs through Microsoft.OpenApi, the AsyncAPI SDK and BinkyLabs (Q28 to Q31), the engine
(`Matchmark.Testing`) could keep reading the normalised JSON they write, needing only System.Text.Json, or use their
object models directly.
*What decides it:* what the libraries give beyond reading, found while adopting them: `$ref` resolution across files,
their own validation of a document, and whether their object models carry what the engine needs (examples, extensions,
JSON Schema keywords) without loss. A benefit the engine would otherwise have to build is the case for the dependency.
*Found so far:* `$ref` resolution across files is built without them (`SpecBundle`); the readers' problems already reach
the user (`MM000`, the reader's objections); the normalised JSON keeps examples, extensions and every JSON Schema keyword
the engine reads, so nothing has been lost that the object models would keep. No benefit found yet that the engine
would otherwise have to build.
*Assumption until then:* the engine keeps working on normalised JSON.

**2. How should the generator read a spec split across files?** The OpenAPI 2.0 `petstore-separate` example refers to
`../common/Error.yaml` and `parameters.yaml`; Matchmark reads a spec's text alone, so the reference has nothing to
resolve against and the example isn't read (found by the official-examples measurement). A source generator may read only
the files given to it as `AdditionalFiles`, so the other files have to reach it somehow.
*The options:* (a) the project lists every part as an `AdditionalFile`, marked as part of the spec
(`MatchmarkSpecPart`), and Matchmark resolves references among them; (b) a `matchmark bundle` command writes one file
that the project uses instead; (c) both, with (b) for CI and specs from elsewhere.
*Built meanwhile:* both. (b) `matchmark bundle`, which the official-examples measurement uses for the split example;
and (a), the generator bundling a split spec from its other files given as `MatchmarkSpecPart` AdditionalFiles
(`BundleTests`). What's left to decide is which one the docs recommend.
*Assumption until then:* both are supported; the docs show (a) for a project that owns its spec, (b) for CI and specs
from elsewhere.

**3. Should the generated client validate on .NET's layer too?** The server now validates on Microsoft.Extensions.Validation
(Q45). The client checks a request before sending it, and a response after, with Matchmark's own `{Api}Validation`. It
runs outside ASP.NET, so moving it means every generated client package depends on the Microsoft.Extensions.Validation
package, still marked experimental (`ASP0029`) in .NET 10, and a contract-only package (`MatchmarkProblems="Owned"`) would
take it as its one new dependency.
*What it decides:* whether `{Api}Validation` can be retired from generated code (validation.md, done when), or stays for
the client and as the server's second pass.
*Built meanwhile:* the opt-in, `MatchmarkClientValidation="Net"`: the client refuses a request on .NET's layer, through
the resolver's `ValidateAsync`, with the same keys as the server (`ModelAttributeTests`). What's left to decide is
whether it becomes the default, which retires `{Api}Validation`'s request checks.
*Assumption until then:* the client keeps `{Api}Validation` by default; the server uses .NET's layer first and
`{Api}Validation` after.

**4. May the generated API split by tag, for incremental generation?** The model's last slice (model.md: an edit to one
tag regenerates only that tag) needs each tag's output from an equatable per-tag model, so Roslyn can skip unchanged tags.
Today one `Map{Api}()` maps every operation and one `{Api}Endpoints` class holds them; per-tag output means
`Map{Api}{Tag}()` per tag, with `Map{Api}()` calling them, and the shared parts (models, validation, outcomes) emitted
from the components alone.
*What it decides:* whether generated servers gain per-tag registration methods (an addition to the generated API, not a
removal), and whether the remaining outputs move onto the model as functions of it before or with this.
*Built meanwhile:* the addition and the incremental step for endpoints: `Map{Api}{Tag}()` per tag, with `Map{Api}()`
calling them, each tag's endpoints generated from its own slice of the spec into `{Api}.Endpoints.{Tag}.g.cs`, so an
edit to one tag's operations regenerates only that file (`An_edit_to_one_tag_regenerates_only_that_tag`, from Roslyn's
step tracking). What's left to decide is whether the other per-operation outputs (handlers, client, fake) split the
same way, which splits their classes into per-tag partials.
*Assumption until then:* endpoints are per tag; the other outputs are one emission per spec.

