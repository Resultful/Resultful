# What changed in the plan since your round 3 (agy-review-3.md)

Files: GOAL.md, VERIFY.md, DECISIONS-TAKEN.md, PROGRESS.md (all in this folder). Probes: ../union-error-probe, ../union-probe-default, ../union-probe-2.

## Your round 3 findings: all 9 folded in
API gate = Unshipped ⊆ target + removed-in-0.3.txt (empty by 12); 13b legs scoped to Shop.Domain + Broken/Generator (17 adds analyzers, 20b Shop.Api); 04c/04d drafted as new dirs only; 12 asserts compiler errors only; 13b owns HIER-*; net481 consumer outside eng/run-tests discovery.

## Structure
- Two stacks. A = 01–04b on main, ships 0.2.0 (Ryan merges/publishes). B = 00 (#34), 04c–25, based on A's top, rebased --onto main after A merges. net481 only in 04b.
- No pauses at checkpoints. release/0.2 is Ryan's only.
- New 04e `publish-preview`: workflow_dispatch from rtkelly13/stack-*, `nuget-preview` env, refuses unless every version has `-preview.`, own concurrency group cancel-in-progress:false, dry-run test. Agents push 0.3.0-preview.1/.2/.3 at checkpoints 19/17/25 after CI is green; failure -> next number. Stable 0.3.0 only prepared in 25.

## Decision 17: error unions on .NET 11 (alongside [ClosedHierarchy])
- Core ships `IErrorUnion { Error Error { get; } }`; generator adds it + Message, Cause, WithContext (keeps union type), TryFind to a `partial union`. RFG0007 for a non-Error case.
- Nested union stays ONE case (grouped, not flattened; Ryan reversed earlier answer). Shape cases = the union's direct cases.
- Union shape widens implicitly to Result<T> via .Error (15).
- Fail rejects an empty/default union (08b) with a 0 B check: cached IsUnion + Unsafe.As<TError,object?>(ref e) is null when SizeOf == IntPtr.Size (probe-2). IUnion{Value:null} path allocates 24 B.
- Analyzer flags `default` of an error union (16a).
- Verified facts: default LangVersion on SDK 11.0.100 compiles unions; generated union has only `object Value`, ctors, IUnion (no HasValue); leaf -> union -> Result doesn't chain (CS0029, needs cast); Error-constrained and struct,IUnion extension overloads coexist; nested union can't be flat-switched (CS8121).
- VERIFY: UERR-* fixtures net11.0 only, each with a [ClosedHierarchy] twin; SDK 11 leg builds the net11 Shop folder.

## Analyzers
- RF0008 (16b): Match/Switch/TryMap handlers must be explicitly typed or method groups, error by default, fix-all. Reason: Match params ordered most-derived then FQN, so a rename reorders; swapped typed handlers -> CS0411, untyped compile silently (verified). Broken consumers: untyped handler (RF0008), reordered Match (CS0411).
- RF0009 (16b): Map whose selector returns a result -> Bind.

## Ten quality additions
08b zero value throws only on Value/Error read (ToString/Equals/GetHashCode/debugger safe); 06 structural Error equality incl. Context; 16a RF0001 covers Task/ValueTask results and async void handlers; 13b+25 adding an error case is reported/documented as breaking (MissingMethodException cross-assembly); 02 tests also run under tr-TR; 19 exceptions and cancellation propagate, Result.Try opts in, CA2007 in src/; 20a 500 hides Message outside Development; 05 XML docs required, CS1591 error (stack B, since 0.2 package check forbids it).
New ledger IDs: ZERO-TOSTRING, ERR-EQ, RF-0009, RF-0001-ASYNC, API-CASE-ADD, CULTURE-*, ASYNC-THROW, ASYNC-CANCEL, HTTP-500-NOLEAK.
