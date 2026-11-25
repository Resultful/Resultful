# Release notes

## Unreleased

### Breaking

- **`Resultful` depends on nothing; OneOf interop moves to the new `Resultful.OneOf` package** (decision 1).
  `Option<T>`'s `Match`, `Switch` and their async forms take `Resultful.None` instead of `OneOf.Types.None`, and
  the implicit conversion from `OneOf.Types.None` is gone. `ToOneOf()` (and its `Task` forms) is an extension in
  `Resultful.OneOf`. Migration: reference `Resultful.OneOf`; `ToOneOf()` calls compile unchanged. Replace
  `OneOf.Types.None` with `Resultful.None` (or convert with `ToResultful()` / `ToOneOf()`). `ToResult()`,
  `ToVoidResult()` and `ToOption()` convert a `OneOf` (or a `Task` of one) back.
- With both `using Resultful;` and `using OneOf.Types;`, `None` is ambiguous (CS0104): write `Option<T>.None`, or
  add an alias (`using None = Resultful.None;`).
- The `Task` forms of `ToOneOf` moved from the `Result` and `Option` classes to `OneOfExtensions`: extension-method
  calls compile unchanged, but static calls (`Result.ToOneOf(task)`) don't, and it is a binary break.
- **A step with no value is a `Result<Unit>`; `VoidResult` and `VoidResult<TError>` are obsolete** (decision 14).
  `Result.Ok()`, `Result.Fail(Error)` and `Result.Fail(string)` return `Result<Unit>`; `Result.Ok<TError>()`,
  `error.Fail()` and `Result.Fail<TError>(params TError[])` return `Result<Unit, TError>`. `Sequence`,
  `SequenceAsync`, `Check`, `CheckAsync`, `DiscardValue`, `DiscardValueAsync`, `Unwrap` and `UnwrapAsync` take and
  return `Result<Unit>` (or `Result<Unit, TError>`), and `Then`/`ThenAsync` gain the parameterless-step forms on
  `Result<Unit>`, `Result<Unit, TError>` and their tasks. The void types, their members and the extensions that take
  them (including `ToOneOf` and `ToVoidResult` in `Resultful.OneOf`) still work, with a CS0618 warning, and are
  removed at 1.0. Migration:
  - Replace `VoidResult` with `Result<Unit>` and `VoidResult<TError>` with `Result<Unit, TError>` in signatures.
    Return `Result.Ok()`, an error, or `Unit.Value` as before.
  - `var r = Result.Ok();` is now a `Result<Unit>`: code that passed it on as a `VoidResult` still compiles through
    the implicit conversion, but members only `VoidResult` had need the `Result<T>` spelling: `Map(value)` becomes
    `Map(_ => value)`, `BindValue` becomes `Bind`, `Cast<TCast>()` on `VoidResult<TError>` becomes `CastError`,
    `ToVoidResult()` becomes `ToResult()`, and `ReduceUntil`/`Plus` on void results become `Then` or `Validation`.
  - A non-async lambda step that returns a `VoidResult` still fits `Sequence`, `Check` and `DiscardValue` (its
    return converts). These don't, and need an edit: a method group returning `VoidResult` (CS0407); a lambda
    returning `VoidResult<TError>` passed to `Result.Sequence(...)` (CS0029, `TError` can't be inferred: name it or
    change the step's type); a non-async lambda returning `Task<VoidResult>` passed to `SequenceAsync`, `CheckAsync`
    or `DiscardValueAsync` (CS0029: write `async x => await …`).
  - `Task<VoidResult> M() => Task.FromResult(Result.Ok());` (and the `VoidResult<TError>` form) fails with CS0029,
    because `Task<T>` isn't covariant: make the method `async`, write `Task.FromResult<VoidResult>(…)`, or change the
    return type to `Task<Result<Unit>>`.
  - Mixing `Result.Ok()` and a `VoidResult` with no target type needs a cast: `switch` expressions (CS8506), array
    literals (CS0826), `Match` with one arm of each (CS0411), and assigning such a lambda to a variable (CS0029).
  - In a `switch` or `?:` that mixes `default` with `Result.Ok()` and targets `VoidResult`, `default` now means
    `default(Result<Unit>)`, whose conversion throws: write `default(VoidResult)`.
  - `voidResult.Then(() => Result.Ok())` binds the `Then<T>` that returns a value, so it gives a `Result<Unit>`: a
    `var` holding it changes type.
  - The debugger shows a `Result.Ok()` as `Ok(Resultful.Unit)` instead of `Ok`.

### Added

- A `net11.0` target. The SDK is now 11.0.100.
- **`Resultful.Text.Json`**: System.Text.Json converters for `Result<T>`, `Result<T, TError>`, `Validation<T>`,
  `Validation<T, TError>`, `Unit`, every `Error` and error unions, with a fixed wire format
  (`{"ok":true,"value":…}`, `{"ok":false,"error":{"type","message","context","cause",…}}`, `"errors":[…]`).
  `AddResultful()` registers them all (it needs dynamic code); native-AOT apps add the converters and
  `AddErrorCases<TError>(cases)` to a source-generated context. An unknown `type` reads as a `JsonException`.

### Build

- CI hardened: least-privilege tokens, no persisted credentials, SDK from `global.json`, NuGet cache, format check,
  pack and package check on every pull request, zizmor, ls-lint, Dependabot.
- Public API tracking with `Microsoft.CodeAnalysis.PublicApiAnalyzers`.
- The package check is a C# file-based app: `dotnet run eng/verify-package.cs -- <nupkg>`.
- Publishing is a manual workflow run (`publish` input); merging to `main` never publishes.

## 0.2.0-beta06

### Fixed

- **Vulnerable transitive packages on .NET 8+.** `0.2.0-beta05` depended on OneOf `>= 3.0.178`, whose only
  modern-compatible group was `netstandard1.3`. Consumers resolving lowest-matching versions (Paket
  `strategy: min`, or NuGet with package pruning off) got `NETStandard.Library 1.6.1`, and with it
  `System.Net.Http 4.3.0` and `System.Text.RegularExpressions 4.3.0` (NU1903). The OneOf floor is now `3.0.271`,
  which has a dependency-free `netstandard2.0` group. On `net11.0` with Paket, Resultful now brings in OneOf alone.
- **Package shape.** The package targets `net10.0` and `netstandard2.0` only. It no longer has `.NETStandard1.3`
  or the incorrect `.NETFramework5.0` dependency groups, and no `NETStandard.Library` or `System.ValueTuple`
  dependencies.

### Added

- `Then`, `Sequence` and `Check` for short-circuiting sequential composition on `Result<T>`, `Result<T, TError>`,
  `VoidResult` and `VoidResult<TError>`, with `Async` variants and `Task` receivers. See the README.
- Package metadata: readme, MIT licence expression, repository URL and commit (SourceLink), and a symbols package
  (`.snupkg`).
- The `net10.0` assembly is marked AOT- and trim-compatible.

### Build

- Deterministic CI builds; a pack-and-inspect check (`eng/verify-package.cs`) fails on any regression of the above.
- LF line endings and a `dotnet format` baseline.
- Documentation in `docs/`: vision, roadmap and decisions.

No public API was removed or changed.
