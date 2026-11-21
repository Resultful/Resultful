# Resultful.Analyzers

Analyzers for [Resultful](https://github.com/Resultful/Resultful). Add it next to the `Resultful` package:

```xml
<PackageReference Include="Resultful" Version="..." />
<PackageReference Include="Resultful.Analyzers" Version="..." PrivateAssets="all" />
```

| Rule | Severity | Reports |
|------|----------|---------|
| RF0001 | warning | A `[MustObserve]` value thrown away: a call or `new` left as a statement, an unawaited `Task` or `ValueTask` of one, an awaited one dropped, an array of them, a task of one converted to `Task`, and an async void handler passed to its `Switch` or `Tee`. The fix discards it with `_ =` (fix-all too). |
| RF0002 | warning | A result created with `default` or `new T()`: a struct that forbids `new T()` with `[Obsolete(..., error: true)]`, a `[ResultShape]`, or an error union. A comparison with `default` is not reported; suppress RF0002 where a zero value is meant (Resultful.Generators does in its output). |

The rules read attributes by full name, so a library's own `internal` copy of
`Resultful.CodeAnalysis.MustObserveAttribute` counts. For a third-party type you can't mark, list its documentation ID:

```ini
[*.cs]
resultful.must_observe_types = T:Vendor.ApiResponse`1, M:Vendor.Client.TrySend
```

The package holds one build per compiler: `analyzers/dotnet/roslyn4.14` (SDK 9.0.3xx) and `analyzers/dotnet/roslyn5.0`
(SDK 10 and later).
