# Progress

Rows are in stack order.

| # | Branch | PR | State | Note |
|---|---|---|---|---|
| 00 | rtkelly13/error-design | #34 | green | stack B base: rebase onto 04b, retarget |
| 01 | rtkelly13/stack-01-consumption-tests | #38 | done (#38, head 24d079b, CI green, reviewed) | copied from #36; stack A base = main |
| 02 | rtkelly13/stack-02-test-infra | #40 | green | d083225, CI green: review fixes + release-check gaps closed (ApiCompat strict added); size waiver |
| 03 | rtkelly13/stack-03-analysis-level | #41 | green | 900f8aa, CI green incl. strict ApiCompat; review fixes as fixup!; carries 02's commits by cherry-pick; nullable lines split into `eng/api-nullable-0.2.0.txt` |
| 04 | rtkelly13/stack-04-per-package-release | #43 | green | d51d2e3, CI green; review fixups in; carries 03's commits by cherry-pick (drop on restack); api-diff glob loosened to 03's, tighten after restack |
| 04b | rtkelly13/stack-04b-release-0.2 | #49 | review | edfbfdf, CI green: 0.2.0, Shipped API, release notes, five release checks vs beta06, NuGet net8/10/11 + Paket min + net481 consumers; mutation run 0 = 42.96% (119 survivors are ConfigureAwait); reviewer + adversary running |
| 04c | rtkelly13/stack-04c-spec-harness | #42 | green | 9a465b0, CI green; review fixes (gate reads api/<tfm>, frozen 0.2.0 baseline, CORE-MATRIX, UERR twins, harness runs cases apart from expected errors), ls-lint rules (f72ad89), [MustObserve]; 268 spec tests, 137 ledger lines pending |
| 04d | rtkelly13/stack-04d-reference-app | #44 | green | 262b016, CI green; review fixes in; carries 04c's commits to 9a465b0; own ls-lint lines |
| 04d2 | rtkelly13/stack-04d2-end-to-end | #45 | green | 3499c1e, CI green; review fixes in; 3 spec: commits (UC10 fixture back to 23a); 292 spec, 34 e2e green locally |
| 04e | rtkelly13/stack-04e-publish-preview | #47 | green | 4f37dca, CI green incl. lint and preview dry run; review fixes in |
| 13a | rtkelly13/stack-13a-generator-target | #46 | review | review fixes in (46a46fd, 5 fixup! commits): language-version gate, stronger fuzz (catches the seeded order fault), no CS0109; 42/47 tests green |
| 05 | rtkelly13/stack-05-named-methods | #51 | review | review: no blockers, 4 should-fix (zero-value docs, UERR sentinel, null-error test, body); spec CI job added (7d69361); an `-s ours` merge 2104c15 (drop on restack); fixing |
| 06 | rtkelly13/stack-06-error-record |  | building | on 05@870518e |
| 07 | rtkelly13/stack-07-attributes |  | todo |  |
| 08a | rtkelly13/stack-08a-own-storage |  | todo |  |
| 08b | rtkelly13/stack-08b-zero-guard |  | todo |  |
| 09 | rtkelly13/stack-09-result-error |  | todo |  |
| 10 | rtkelly13/stack-10-validation |  | todo |  |
| 11 | rtkelly13/stack-11-oneof-split |  | todo |  |
| 12 | rtkelly13/stack-12-void-to-unit |  | todo |  |
| 12b | rtkelly13/stack-12b-text-json | #48 | review | review fixes in (e75a6a8): ValidationFailed<X> wire name, duplicates rejected, streaming reader, AOT resolver test; 126/135 tests green against the stand-in |
| 18 | rtkelly13/stack-18-operators |  | todo |  |
| 19 | rtkelly13/stack-19-async |  | todo | checkpoint: core done |
| 13b | rtkelly13/stack-13b-generator-package |  | todo |  |
| 15 | rtkelly13/stack-15-shape-conversions |  | todo |  |
| 16a | rtkelly13/stack-16a-analyzers-core | #50 | review | review fixes in (596a78f): Task-receiver handlers, arrays/WhenAll, Task upcasts; all 5 RF fixtures pass through 04c's harness |
| 16b | rtkelly13/stack-16b-analyzers-more |  | building | drafted ahead on 16a |
| 17 | rtkelly13/stack-17-closed-hierarchy-rules |  | todo | checkpoint: tooling done |
| 20a | rtkelly13/stack-20a-aspnetcore-errors | #53 | review | drafted ahead on 04c@9a465b0; 4d17410; spec: ResultHttpResult<T, TError>; 69/70 tests green against the stand-in; AOT probe clean; reviewer running |
| 20b | rtkelly13/stack-20b-aspnetcore-endpoints |  | todo |  |
| 21 | rtkelly13/stack-21-fluentvalidation-diagnostics | #52 | review | drafted ahead on 04c@9a465b0; c8e2ecf; API equals target; 23+19 tests green against the stand-in; FV-VALIDATE, DIAG-HOOKS would flip |
| 22 | rtkelly13/stack-22-aot |  | todo |  |
| 23a | rtkelly13/stack-23a-finish-the-shop |  | todo |  |
| 23b | rtkelly13/stack-23b-samples-integrations |  | todo |  |
| 24 | rtkelly13/stack-24-core-union |  | todo |  |
| 25 | rtkelly13/stack-25-docs-sync |  | todo |  |

- 2026-11-20: the plan moved from the local `temp/stack/` to this branch (#39), for a Claude Code on the web run. 01 was built and reviewed locally. 02's contract is written; 02 is next.
- 2026-11-20 (web run 1): resumed on Claude Code on the web. SDKs pulled from the MCR images, GitHub through the MCP tools (`WEB-RUN.md`). Starting 02.
- Live tracker (private artifact, updated at each PR boundary): https://claude.ai/artifact/KPm6VvRnY2U7moMKKZufrE
- Done in 04c (eb8beb9, 156481b, 9a465b0, a36c3d4): the `spec:` fixes batched with 04c's review: from 13a, `GEN-RFG0005` can't pass as written (CS1729; give `NestedError` its own protected constructor, see 13a's `GenRfg0005Proposed.cs`), `building-the-generator.md` still says colliding names are prefixed (now RFG0008), `SHAPE-UNIT` calls `Ok(Unit.Value)` but the tsv says `Ok()`; from 04d, the UC10 "before" snippet in `errors-in-practice.md` doesn't compile on 0.2, GOAL row 12 misses CS0411.
- 2026-11-21 ~02:40 UTC: **checkpoint, the 0.3 target is reviewable.** 04c (#42), 04d (#44), 04d2 (#45) green after review; 12 design questions on #39 with defaults taken. Stack A: #38, #40, #41, #43 green; 04b building.
