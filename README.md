# Resultful

Typed, zero-allocation error handling and result shapes for .NET — from domain errors to HTTP `ProblemDetails`, GraphQL, `System.Text.Json` (Native AOT), OpenTelemetry, and compile-time safety.

## 30-Second Taste

```csharp
[ClosedHierarchy]
public abstract partial record OrderError(string Message) : Error(Message)
{
    public sealed record NotFound(int OrderId) : OrderError($"Order {OrderId} was not found.");
    public sealed record Locked(int OrderId, string Owner) : OrderError($"Order {OrderId} is locked.");
}

[ResultShape]
public readonly partial struct GetOrderResult : IResultShape<Order, OrderError>;

// Domain pipeline: fail-fast composition + structured context (never leaked to callers)
GetOrderResult FindOrder(int id) =>
    LoadOrder(id)
        .Bind(EnsureUnlocked)
        .WithContext($"lookup order {id}");

// Boundary: maps domain leaves to RFC 9457 ProblemDetails with zero PII leakage
app.MapGet("/orders/{id:int}", (int id) => FindOrder(id).ToHttpResult());
```

Roslyn analyzers enforce at compile time that results are never silently discarded (`RF0001`), never left uninitialised (`RF0002`), and exhaustively handled (`CS8509`).

## The Ecosystem (4 Layers, Lockstep Versioned)

Start with the dependency-free core and adopt each layer only when you need it:

| Layer | Packages | Purpose |
|---|---|---|
| **Core (0 deps)** | `Resultful` | `Result<T, TError>`, `Result<T>`, `Validation<T, TError>`, `Error`, `Option<T>`, `Unit` (`netstandard2.0`, `net10.0`, `net11.0`) |
| **Compiler** | `Resultful.Generators`, `Resultful.Analyzers` | Zero-alloc `[ResultShape]` & `[ClosedHierarchy]` (`.NET 11 [Union]` ready), `RF0001`–`RF0010`, `CS8509` suppressor |
| **Boundaries** | `Resultful.AspNetCore`, `Resultful.Text.Json`, `Resultful.HotChocolate`, `Resultful.FluentValidation`, `Resultful.Diagnostics`, `Resultful.FSharp`, `Resultful.OneOf` | RFC 9457 `ProblemDetails`, AOT JSON, GraphQL error unions, validation bridge, opt-in OTel spans, F# & `OneOf` interop |
| **Testing** | `Resultful.Assertions` *(+ `AwesomeAssertions`, `FluentAssertions` 7.x)* | Zero-dependency result/error assertions (`ShouldSucceed()`, `ShouldFailWith<TError>()`) |

## Install

```bash
dotnet add package Resultful
dotnet add package Resultful.Analyzers   # recommended: compile-time safety
dotnet add package Resultful.Generators  # optional: [ResultShape] & [ClosedHierarchy]
```

## Documentation

- **[Documentation Portal](docs/index.md)** — Start here (`docs.ryankelly.dev/resultful`)
- **[Quick Start](docs/guides/quickstart.md)** — From first `Result` to HTTP & GraphQL
- **[Comparing Resultful](docs/guides/comparing-resultful.md)** — How it compares to exceptions, `OneOf`, `ErrorOr`, `FluentResults`, and `LanguageExt`
- **[Using Resultful with AI Agents](docs/guides/using-with-ai-agents.md)** — Drop-in `CLAUDE.md` / `AGENTS.md` rules for consuming apps
- **[Release Notes](RELEASE_NOTES.md)** — Version history and upgrade notes

---

<sub>**For AI agents & maintainers:** repo guardrails live in [AGENTS.md](AGENTS.md); architecture decisions in [docs/decisions/](docs/decisions/index.md); design explorations in [docs/explorations/](docs/explorations/seamless-with-dotnet.md); exact public API baselines in `src/*/PublicAPI.*.txt`.</sub>
