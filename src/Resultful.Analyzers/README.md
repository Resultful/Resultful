# Resultful.Analyzers

Analyzers for [Resultful](https://github.com/Resultful/Resultful). Add it next to the `Resultful` package:

```xml
<PackageReference Include="Resultful" Version="..." />
<PackageReference Include="Resultful.Analyzers" Version="..." PrivateAssets="all" />
```

Each rule's description (shown by the IDE) lists what it covers and its known gaps.

| Rule | Severity | Reports |
|------|----------|---------|
| RF0001 | warning | A `[MustObserve]` value thrown away: a call or `new` left as a statement, an unawaited `Task` or `ValueTask` of one, an awaited one dropped, an array of them, a task of one converted to `Task`, and an async void handler passed to its `Switch` or `Tee`. The fix discards it with `_ =` (fix-all too). |
| RF0002 | warning | A result created with `default` or `new T()`: a struct that forbids `new T()` with `[Obsolete(..., error: true)]`, a `[ResultShape]`, or an error union. A comparison with `default` is not reported; suppress RF0002 where a zero value is meant (Resultful.Generators does in its output). |
| RF0003 | warning | `r.Value` without an `IsSuccess`, `IsFailure` or `TryGetValue` check of the same variable guarding it (an `if`, `?:`, `&&`, `\|\|`, or an earlier `if (…) return;`). |
| RF0004 | info | A string literal or interpolated string that becomes the success of a `Result<string>` (a failure before 0.3). The fix wraps it in `Result.Ok(…)` or `Result.Fail<string>(…)`. |
| RF0006 | warning | A result field, or get-only auto-property, with no initializer that a constructor leaves unassigned, so it holds the zero value. |
| RF0008 | **error** | An untyped lambda passed to a generated multi-case `Match`, `Switch` or `TryMap`: adding or renaming a case can reorder the handlers. The fix adds the types (fix-all). Core's `Match(onSuccess, onError)` is exempt. |
| RF0009 | warning | `Map` whose selector returns a result (`Result<Result<T>>`). The fix replaces it with `Bind` (fix-all). |

The rules read attributes by full name, so a library's own `internal` copy of
`Resultful.CodeAnalysis.MustObserveAttribute` counts. For a third-party type you can't mark, list its documentation ID:

```ini
[*.cs]
resultful.must_observe_types = T:Vendor.ApiResponse`1, M:Vendor.Client.TrySend
```

The package holds one build per compiler: `analyzers/dotnet/roslyn4.14` (SDK 9.0.3xx) and `analyzers/dotnet/roslyn5.0`
(SDK 10 and later).
