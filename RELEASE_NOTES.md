# Release notes

## Unreleased

### Build

- CI hardened: least-privilege tokens, no persisted credentials, SDK from `global.json`, NuGet cache, format check,
  pack and package check on every pull request, zizmor, ls-lint, Dependabot.
- The package check is a C# file-based app: `dotnet run eng/verify-package.cs -- <nupkg>`.
- Publishing from `main` works again; the old condition never fired.

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
