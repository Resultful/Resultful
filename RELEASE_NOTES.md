# Release notes

## 0.2.0

0.2.0 is `0.2.0-beta06`'s API as a stable release. No public member was added or removed, and every method body
is the same as beta06's. On every pull request CI (`eng/build.cs`) runs the SDK's package validation, the public
API analyzer against `PublicAPI.Shipped.txt`, the package check, an AnyCPU check of every packed DLL, and the
packed-package consumers, including a library compiled against beta05 running on 0.2.0.

### Added

- A `net11.0` target, with the same code as `net10.0`. The SDK is now 11.0.100.
- An XML doc comment on every public type and member, in the source. The package doesn't include the XML file
  yet, so its file list stays beta06's.

### Changed

- Eight members now say in their signature that they accept `null`: five `Fail` overloads
  (`Fail(params string[]?)`, `Fail(this IEnumerable<string>?)`, `Fail<T, TError>(params TError[]?)`,
  `Fail<T, TError>(this IEnumerable<TError>?)`, `Fail<TError>(params TError[]?)`), which treat `null` as no errors,
  and three implicit conversions to `VoidResult` and `VoidResult<TError>` (from `string[]?` and from `Unit?`), which
  already accepted it. Only the nullable annotations changed, so passing `null` no longer gives a warning. Nothing
  else changes for callers.
- The assembly and file version stay 1.0.0.0, as in every published 0.2.0 pre-release, so a library compiled
  against beta05 or earlier still loads 0.2.0's `Resultful.dll` on .NET Core. The package version is 0.2.0.

### Known hazards

These behave as they did in beta06. 0.3 fixes them.

- **The zero value is a success.** `default(Result<T>)` and `new Result<T>()` are a success holding `default(T)`.
  The same holds for `Result<T, TError>`, `VoidResult` and `VoidResult<TError>`, whose zero value succeeds with a
  `null` `Unit`. A result field you forgot to assign, or a `default` in an array, reads as a success.
- **A bare string converts to a failure.** `Result<string> r = "hello";` is a failure with the error `"hello"`,
  not a success. Write `Result.Ok("hello")` or `"hello".Ok()`.
- The instance `SwitchAsync` on the four result types returns `void`. It drops the callback's `Task`, so it can't
  be awaited and an exception in that `Task` is lost. Await a `Match` that returns a `Task` instead.

### 0.3 will break

0.3 is a breaking release, in source and in binary. The zero value stops being a success
([decision 10](docs/decisions/0010-default-is-not-success.md)), a bare string stops converting to an error, and the
core drops its OneOf dependency ([decision 1](docs/decisions/0001-core-is-dependency-free.md)). Stay on `0.2.*`
until you are ready to move; 0.3's release notes carry the migration.

### Build

- CI hardened: least-privilege tokens, no persisted credentials, SDK from `global.json`, NuGet cache, format check,
  pack and package check on every pull request, zizmor, ls-lint, Dependabot.
- Public API tracking with `Microsoft.CodeAnalysis.PublicApiAnalyzers`, one shared list for every target.
- The package check is a C# file-based app: `dotnet run eng/verify-package.cs -- <nupkg>`.
- A characterisation suite pins 0.2's public behaviour, both hazards included. It runs against the project and
  the packed package, under `tr-TR` and the invariant culture too.
- `EnablePackageValidation`, and `eng/build.cs`'s `Pack` target fails unless every packed DLL is AnyCPU.
- Consumers on `net8.0`, `net10.0`, `net11.0`, `net481` and Paket `strategy: min` restore the packed package alone
  (`CompatNuGet`, `CompatNet481`, the `compatibility` job). `CompatUpgrade` runs a library compiled against beta05
  on the packed package, on `net8.0` and `net10.0`.
- Releases are a manual `release.yml` run given the packages and versions to publish; no version is committed,
  and each package is tagged `<package>/v<version>` (`Resultful/v0.2.0`) with its own GitHub Release
  ([decision 22](docs/decisions/0022-releases-are-a-dispatch-map.md)). It stages each package on nuget.org, which
  goes public only when promoted there with 2FA, and drafts its GitHub Release; once the version is public,
  `finalize.yml` publishes the draft, which tags it ([decision 23](docs/decisions/0023-draft-release-then-finalize.md)).
  Merging to `main` never publishes.

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
