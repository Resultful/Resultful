# Round 3: PR Order, Dependency, Ledger & Gate Audit

## Critical (breaks build or makes a PR impossible as ordered)

1. **`04c` `real PublicAPI ⊆ target` gate fails immediately on `04c`–`11`.**
   - `GOAL.md:87`, `VERIFY.md:30`: requires `real PublicAPI.*.txt ⊆ TargetApi` on every PR from `04c` on.
   - At `04c`, real `PublicAPI.Shipped.txt` still has 0.2.0's `VoidResult`, `ToOneOf`, `IEnumerable<string>` overloads, and `IPlus`, which are absent from the 0.3 `TargetApi` until `09`/`11`/`12`.
   - **Fix:** From `04c`–`12`, gate `PublicAPI.Unshipped.txt` additions ⊆ `TargetApi`, plus a `tests/Resultful.TargetApi/removed-in-0.3.txt` allowlist that shrinks to empty by `12`; enforce `real == target` only at `25`.

2. **`13b` runs "the four compiler legs" on the Shop and `Broken/*` before `16a`–`17` and `20a`–`22` exist.**
   - `GOAL.md:101`, `VERIFY.md:59,85`: the four legs build `Shop.*` and `Broken/*` (discarded result `16a`, `default` `16a`, bare string `16b`, cross-assembly `17`, `Shop.Api` `20a`/`20b`). None of those packages exist at `13b`.
   - **Fix:** Scope `13b`'s four legs to `Shop.Domain` + `Broken/Generator/*` (`CS8509` missing arm, `RFG0001`). Add `Resultful.Analyzers` + `Broken/Analyzers/*` to the legs in `17`, and `Shop.Api` in `20b`.

3. **`19` checkpoint claims "core equals its target API" five PRs before `24` (`core-union`).**
   - `GOAL.md:100,112`: `24` adds `[Union]`, `IUnion`, `HasValue`, `Value`, `TryGetValue` to core types on `net11.0`, so core cannot match `TargetApi` at `19`.
   - **Fix:** Either move `24` (`core-union`) immediately after `19` (so `13b` and the Shop build on the finished core), or exempt `UNION-*` from `19`'s target-equality check.

4. **`13a` compiles `Resultful.Generators` against `TargetApi` while `SHAPE-*`/`HIER-*`/`GEN-*` are `pending`.**
   - `VERIFY.md:42-43` fails the build if a `pending` fixture passes, yet `GOAL.md:89,101` leaves `GEN-*`/`HIER-*` `pending` until `13b`. Compile-only fixtures (`RFG0001`–`RFG0006`, `GEN-*`) will pass in `13a` and trip the gate.
   - **Fix:** Have `SpecHarness.cs` run strictly against real `src/` DLLs (never `TargetApi`), or split ledger ownership so `13a` flips `GEN-*`/`RFG-*` and `13b` flips runtime/packed `SHAPE-*`/`HIER-*`.

## High (premature checks, ownership collisions, or parallel merge conflicts)

5. **Running `04c`/`04d` in parallel with `02`–`04` will conflict on shared root files.**
   - `GOAL.md:134` assumes `04c`/`04d` only add new directories, but wiring `TargetApi`, `Spec`, `Shop.*`, and `EndToEnd` requires editing `Resultful.slnx`, `Directory.Packages.props`, `.github/workflows/ci.yml`, and `.ls-lint.yml` — the exact files `02`–`04b` mutate.
   - **Fix:** Draft `04c`/`04d` strictly as isolated directories with zero edits to root files; wire them into `Resultful.slnx`, `Directory.Packages.props`, and `ci.yml` only after rebasing onto `04b`.

6. **`12` (`void-to-unit`) asserts `Shop.Legacy` fails with UC9/UC10 errors before `16a`/`16b` exist.**
   - `GOAL.md:98`, `VERIFY.md:52`: `Shop.Legacy` includes UC9 (`internal [MustObserve]`, PR `16a`) and UC10's bare-string `Result<string>` warning (PR `16b`).
   - **Fix:** At `12`, assert only compiler errors on `Shop.Legacy` (`CS0029` on `Result<int> F() => "bad"`, `CS0618` on `[Obsolete] VoidResult`); flip UC9 and bare-string checks in `16a`/`16b`.

7. **Frozen `01` characterisation suite conflicts with `08b`/`09`/`11`/`12` + `test-counts.json`.**
   - `VERIFY.md:23-24,104`: freezing `01` breaks `08b` (`default` throws) and `09` (`Result<T>` drops `IEnumerable<string>`), while deleting tests trips the non-decreasing test-count gate.
   - **Fix:** In `08a`, split the frozen 0.2 suite into the baseline-differential runner (against packed `0.2.0`); allow `08b`/`09`/`11`/`12` to migrate `tests/Resultful.Consumption` assertions without dropping count.

8. **`13b` and `15` both claim Tier 1 `Match`/`Switch`/`TryMap` (`HIER-*`).**
   - `GOAL.md:101-102`: `13b` flips `HIER-*` while `15` says "Tier 1 `Match`/`Switch`/`TryMap` complete".
   - **Fix:** `13b` owns `HIER-*` completely (already implemented in #37); `15` owns only shape-to-shape/`Validation` conversions, `WithContext` keeping the shape, and `ManyErrors`.

9. **`04b` `net481` Windows consumer vs `02`'s `eng/run-tests.cs` on Linux.**
   - `VERIFY.md:21`: `net481` runs on `windows-latest`, while `02`'s `eng/run-tests.cs` auto-discovers `tests/*/*.csproj` on `ubuntu-latest`.
   - **Fix:** Put the `net481` consumer under `temp/` or `tests/Compatibility/Net481` (or teach `eng/run-tests.cs` to skip `net481` TFMs on non-Windows).
