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
*Assumption until then:* the engine keeps working on normalised JSON.

**2. How should the generator read a spec split across files?** The OpenAPI 2.0 `petstore-separate` example refers to
`../common/Error.yaml` and `parameters.yaml`; Matchmark reads a spec's text alone, so the reference has nothing to
resolve against and the example isn't read (found by the official-examples measurement). A source generator may read only
the files given to it as `AdditionalFiles`, so the other files have to reach it somehow.
*The options:* (a) the project lists every part as an `AdditionalFile`, marked as part of the spec
(`MatchmarkSpecPart`), and Matchmark resolves references among them; (b) a `matchmark bundle` command writes one file
that the project uses instead; (c) both, with (b) for CI and specs from elsewhere.
*Assumption until then:* a split spec isn't read, and the measurement names it as the one OpenAPI example that fails.

**3. Should the generated client validate on .NET's layer too?** The server now validates on Microsoft.Extensions.Validation
(Q45). The client checks a request before sending it, and a response after, with Matchmark's own `{Api}Validation`. It
runs outside ASP.NET, so moving it means every generated client package depends on the Microsoft.Extensions.Validation
package, still marked experimental (`ASP0029`) in .NET 10, and a contract-only package (`MatchmarkProblems="Owned"`) would
take it as its one new dependency.
*What it decides:* whether `{Api}Validation` can be retired from generated code (validation.md, done when), or stays for
the client and as the server's second pass.
*Assumption until then:* the client keeps `{Api}Validation`; the server uses .NET's layer first and `{Api}Validation` after.
