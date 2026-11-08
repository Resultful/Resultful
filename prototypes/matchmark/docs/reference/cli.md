# Command line

`matchmark` is a dotnet tool (`Matchmark.Cli`). Every command reads a spec in YAML or JSON and applies any
`--overlay` files to it first, in the order given.

**Exit codes, for every command:** `0` the command succeeded and found nothing that fails it; `1` it ran and found
something that fails it (as each command says below); `2` it couldn't run: bad arguments, a file that can't be read,
or a target that can't be reached.

| Command | Status | What it does |
|:--|:--|:--|
| [`audit`](#audit) | Built | What the spec leaves out, measured in the cases each fix would add. |
| [`regress`](#regress) | Built | The suite the spec writes for itself, run against any API. |
| [`diff`](#diff) | Built | What changed between two specs, and whom it breaks. |
| [`scaffold`](#scaffold) | Built | The server side as ASP.NET source you own. |
| [`twin`](#twin) | Built | A stateful fake of the API from the spec alone. |
| [`fidelity`](#fidelity) | Built | A fake scored against the real API, and the gate for changes to it. |
| [`upgrade`](#upgrade) | Built | The spec, forward-ported to the latest version of its format, or written down to OpenAPI 3.1. |
| [`host`](#host) | Planned | The session host: a copy of the API per test session. |

## audit

```
matchmark audit <spec> [--arazzo <file>]... [--overlay <file>]... [--sarif <file>] [--spectral <file>] [--strict]
```

Reports what the spec leaves out that the generated suite, fake and server would use, and measures each finding by
generating the suite again with the change made. A finding names its rule, its level (note or warning), where it
applies, and the cases or workflows its fix would add. The last line scores the spec: the share of what the suite could
check that is checkable.

| Option | Meaning |
|:--|:--|
| `--arazzo <file>` | An Arazzo document whose workflows count. Repeatable. |
| `--overlay <file>` | An Overlay applied to the spec first. Repeatable. |
| `--sarif <file>` | Also write the findings as SARIF 2.1.0, for CI and editors. |
| `--spectral <file>` | Also write the static rules as a Spectral ruleset. |
| `--strict` | Exit `1` when there is a warning. |

## regress

```
matchmark regress <spec> <base url> [--arazzo <file>]... [--overlay <file>]... [--no-workflows] [--webhooks]
                  [--random <n>] [--seed <n>]
```

Generates the regression suite from the spec and runs it against the API at `<base url>`, checking every response
([checks](checks.md)). The suite holds the spec's examples, valid requests, boundary values, one invalid request per
constraint, workflows from its `links`, and the Arazzo workflows given. Exits `1` when any check fails.

| Option | Meaning |
|:--|:--|
| `--arazzo <file>` | An Arazzo document whose workflows run too. Repeatable. |
| `--overlay <file>` | An Overlay applied to the spec first. Repeatable. |
| `--no-workflows` | Don't follow links or run workflows, for a target that keeps no state. |
| `--webhooks` | `<base url>` is a receiver of the spec's webhooks, each at `/{name}`: test it instead of the API. |
| `--random <n>` | Also send `n` random valid requests per operation, shrinking each failure to the smallest request that still fails. |
| `--seed <n>` | The seed for `--random`, to repeat a run. Default `1`. |

**An AsyncAPI 3 document** in place of the spec tests a consumer of its messages. `<base url>` is then a bridge that
hands each `POST /{operationId}` to the consumer as a message (the body its payload, the headers its headers) and
answers `2xx` when the consumer takes it, `400` when it refuses it.

## diff

```
matchmark diff <old spec> <new spec> [--allow-breaking]
```

Lists each change between two specs and classifies it:

| Class | Meaning |
|:--|:--|
| breaking | An existing client or server stops working: a removed operation, a new required field, a narrowed type. |
| generated | Code generated from the old spec stops compiling, though the wire is unchanged: a renamed operation. |
| additive | Nothing existing breaks. |

Exits `1` when a change is breaking and `info.version`'s major number didn't go up, unless `--allow-breaking`.

## scaffold

```
matchmark scaffold <spec> <output dir> [--style controllers|minimal] [--contract-namespace NS] [--namespace NS]
                   [--api NAME] [--overlay <file>]... [--force]
```

Writes the server side of the spec as ASP.NET source you own, with a reference implementation that answers with the
spec's examples, so the service runs and conforms on day one. The contract (types, unions, handler interfaces) stays
generated: reference a project that generates it (`MatchmarkParts="Contract"`, see [build settings](build.md)).

| Option | Meaning |
|:--|:--|
| `--style` | `controllers` (default): `[ApiController]` classes. `minimal`: Minimal API endpoints. |
| `--contract-namespace` | Where the generated contract lives. Default `<api>.Api`. |
| `--namespace` | The scaffolded code's namespace. Default `<api>`. |
| `--api` | The API name used in type names. Default: from the spec's file name. |
| `--overlay <file>` | An Overlay applied to the spec first. Repeatable. |
| `--force` | Overwrite files that exist. Without it, existing files are kept, so running again after a spec change only adds what is new, and the compiler shows what no longer fits. |

## twin

```
matchmark twin <spec> [--overlay <file>]... [--urls <url>]
```

Serves a twin: a stateful fake of the API from the spec alone ([twins](twins.md)). Paths that share a prefix are one
collection, so what is created can be read, listed, changed and deleted, and requests are checked as the spec says.
Default URL `http://localhost:5080`. Runs until stopped.

## fidelity

```
matchmark fidelity <spec> <real url | recording.json> <fake url> [--overlay <file>]... [--floor <percent>]
                   [--record <file>] [--tasks <file>] [--gate <file>] [--held-out <percent>]
```

Runs the suite against the real API and against a fake, both from the same starting state, and reports the share of
steps that answer the same, per operation and overall ([twins](twins.md)). Exits `1` below `--floor`, or when `--gate`
rejects the change.

| Option | Meaning |
|:--|:--|
| `--floor <percent>` | Exit `1` when the score is below this. |
| `--record <file>` | Save the real API's answers. A recording given in place of the real URL is replayed, so a pull-request build needs neither the real API nor its credentials. |
| `--tasks <file>` | Write each mismatch as a task, as JSON: the step, both answers, and where they first differ. |
| `--gate <file>` | Judge a change to the fake on the held-out part of the run: exit `1` unless the held-out score rose and no operation fell since the scores kept in `<file>`; keep the new scores there when it passes. With `--gate`, `--tasks` holds only the training part. |
| `--held-out <percent>` | The share of sequences held out for `--gate`. Default `30`. |

## upgrade

```
matchmark upgrade <spec> [--output <file>] [--to <version>]
```

Writes the spec forward-ported to the latest version of its format (decisions Q22 to Q25), and lists every change the
upgrade made and anything the target version has no place for. Exits `1` when something was lost.

| Format | Written as | How |
|:--|:--|:--|
| OpenAPI 2.0 to 3.2 | 3.2, or 3.1 with `--to 3.1` | Read and written by Microsoft.OpenApi. Written down to 3.1, a `QUERY` operation goes into `x-oai-additionalOperations`, as ASP.NET Core writes it. |
| AsyncAPI 2.x, 3.0 | 3.1 | 2.x by Matchmark's port of the official guide, *Migrating to v3* (`AsyncApiUpgrade`); 3.0 moves up unchanged. |
| Arazzo 1.0 | 1.1 | JSONPath criteria are named as the Goessner draft 1.0 meant, since 1.1 reads RFC 9535 by default. |
| Overlay 1.0, 1.1 | 1.2 | The version moves up; nothing else changes. |

| Option | Meaning |
|:--|:--|
| `--output <file>` | The file to write: `.json` for JSON, anything else YAML. Without it, YAML goes to standard output with the changes as `#` comments at the top. |
| `--to <version>` | The version to write: `3.2` or `3.1` for OpenAPI; only the latest for the other formats. |

What a 2.x AsyncAPI upgrade reports as **lost**: a channel parameter's schema keywords other than `enum`, `default` and
`examples` (3.0 parameters are strings); scopes a server or operation required, which move onto the security scheme and
so apply to everything that uses it; a channel given by `$ref`, which isn't converted.

## host

*Planned (roadmap step 4; decisions Q34 to Q43, and [questions](../QUESTIONS.md) 6 and 8).*

```
matchmark host <spec> [--arazzo <file>]... [--overlay <file>]... [--urls <url>]
```

Serves the session host: one copy of the API per test session, routed by host. See [hosted sessions](sessions.md) for
the control API.
