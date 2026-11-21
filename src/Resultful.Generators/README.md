# Resultful.Generators

A source generator for [Resultful](https://github.com/Resultful/Resultful). Add it next to the `Resultful` package:

```xml
<PackageReference Include="Resultful" Version="..." />
<PackageReference Include="Resultful.Generators" Version="..." PrivateAssets="all" />
```

- `[ClosedHierarchy]` on an `abstract partial record` deriving from `Error` writes `Cases`, `Match`, `Switch` and
  `TryMap` over its leaves.
- `[ResultShape(typeof(OrderError), Success = typeof(Order))]` on a `readonly partial struct` writes a named result:
  `Ok`, `Fail`, conversions from the value and from each leaf, `Match` with one handler per leaf, `WithContext`, and
  widening to `Result<T, TError>` and `Result<T>`.
- On .NET 11 with C# 15, a shape is also a `[Union]`, so `switch` over it is exhaustive, and the error type may be a
  `partial union` of `Error` records, which gets `IErrorUnion`. A nested union stays one case.

Adding a leaf or a union case adds a parameter to the shape's `Match`: a breaking change for callers in other
assemblies, which must be rebuilt.

The package holds one build per compiler: `analyzers/dotnet/roslyn4.14` (SDK 9.0.3xx, never emits `[Union]`) and
`analyzers/dotnet/roslyn5.0` (SDK 10 and later).
