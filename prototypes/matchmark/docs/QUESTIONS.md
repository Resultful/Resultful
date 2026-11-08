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
