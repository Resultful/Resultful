# Specifications

The specifications Matchmark reads, vendored at pinned commits (decision Q56) so its readers are built and regressed
against the documents themselves: each one's text, its official JSON Schemas, and its own examples and fixtures.
[SOURCES.md](SOURCES.md) lists every folder's repository and commit; `sh vendor.sh` refreshes them. Moving a pin is a
change of its own, with the regression suites run before and after.

| Folder | Holds | Read by | Licence |
|:--|:--|:--|:--|
| `asyncapi/` | the 2.6.0, 3.0.0 and 3.1.0 texts; the official JSON Schemas; the real-world examples (Slack, Gitter, Kraken, streetlights over Kafka and MQTT, and more); each schema's per-object examples | `Matchmark.Specs.AsyncApi` | Apache-2.0 (`asyncapi/LICENSE`, `NOTICE`) |
| `arazzo/` | the 1.0.0, 1.0.1 and 1.1.0 texts; the 1.0 schema, and the 1.1 schema with its pass and fail fixtures (from the 1.1 development branch); the examples | `Matchmark.Specs.Arazzo` | Apache-2.0 (`arazzo/LICENSE`) |
| `overlay/` | the 1.0.0, 1.1.0 and 1.2.0 texts; the schemas; the compliant sets (an input, an overlay, and the output it must give); each version's pass and fail fixtures | `Matchmark.Specs.Overlay` | Apache-2.0 (`overlay/LICENSE`) |
| `openapi/` | the examples of learn.openapis.org, for the official-examples measurement (OpenAPI itself is read by Microsoft.OpenApi) | `OfficialExamplesTests` | CC BY 4.0 (`openapi/LICENSE`): © the OpenAPI Initiative, from [learn.openapis.org](https://github.com/OAI/learn.openapis.org), unchanged |

Nothing here is edited: a fix belongs upstream, and a document Matchmark reads differently from its schema is a
finding of the regression suite, not a change to the file.
