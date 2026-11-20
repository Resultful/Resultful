# Progress

Rows are in stack order.

| # | Branch | PR | State | Note |
|---|---|---|---|---|
| 00 | rtkelly13/error-design | #34 | green | stack B base: rebase onto 04b, retarget |
| 01 | rtkelly13/stack-01-consumption-tests | #38 | done (#38, head 24d079b, CI green, reviewed) | copied from #36; stack A base = main |
| 02 | rtkelly13/stack-02-test-infra | #40 | green | d083225, CI green: review fixes + release-check gaps closed (ApiCompat strict added); size waiver |
| 03 | rtkelly13/stack-03-analysis-level | #41 | review | 12 fixes in (d73ff76, 4 fixup! commits); conflicts with 02's new commits so CI can't run; cherry-picking 02's commits |
| 04 | rtkelly13/stack-04-per-package-release | #43 | green | 34d01ec, CI green; review fixes as 3 fixup! commits (3f804b1, 35d9b27, 34d01ec); will absorb 03's new commits once 03 is green |
| 04b | rtkelly13/stack-04b-release-0.2 |  | building | checkpoint; contract written; on 04@6eb5116 |
| 04c | rtkelly13/stack-04c-spec-harness | #42 | review | review: changes needed (API gate passes on an empty read with 03's layout; 9 should-fix; spec batch from 13a/04d); 8 questions on #39 with defaults; fixing |
| 04d | rtkelly13/stack-04d-reference-app | #44 | review | review: approve, with should-fixes (CardDeclined text, a shape over a union in Net11); fixing |
| 04d2 | rtkelly13/stack-04d2-end-to-end | #45 | review | review: changes needed (MissingArm deadlock with 13b; Legacy cases to MIG-LEGACY; pack failures hidden); 4 questions on #39; fixing |
| 04e | rtkelly13/stack-04e-publish-preview | #47 | review | a96a660 on 04d2@1fedf62; dry run green (0.3.0-preview.0 packed, restored together); lint red from 04c/04d paths (04c adds .ls-lint.yml rules; cherry-pick up); secret `NUGET_PREVIEW_KEY` |
| 13a | rtkelly13/stack-13a-generator-target | #46 | review | review: changes needed ([Union] ignores language version; fuzz mostly hits early returns); fixing |
| 05 | rtkelly13/stack-05-named-methods |  | todo |  |
| 06 | rtkelly13/stack-06-error-record |  | todo |  |
| 07 | rtkelly13/stack-07-attributes |  | todo |  |
| 08a | rtkelly13/stack-08a-own-storage |  | todo |  |
| 08b | rtkelly13/stack-08b-zero-guard |  | todo |  |
| 09 | rtkelly13/stack-09-result-error |  | todo |  |
| 10 | rtkelly13/stack-10-validation |  | todo |  |
| 11 | rtkelly13/stack-11-oneof-split |  | todo |  |
| 12 | rtkelly13/stack-12-void-to-unit |  | todo |  |
| 12b | rtkelly13/stack-12b-text-json | #48 | review | drafted ahead on 04c@5b8b23a; f7b40ca; API equals target; 96/104 tests green against a stand-in core; reviewer running |
| 18 | rtkelly13/stack-18-operators |  | todo |  |
| 19 | rtkelly13/stack-19-async |  | todo | checkpoint: core done |
| 13b | rtkelly13/stack-13b-generator-package |  | todo |  |
| 15 | rtkelly13/stack-15-shape-conversions |  | todo |  |
| 16a | rtkelly13/stack-16a-analyzers-core |  | todo |  |
| 16b | rtkelly13/stack-16b-analyzers-more |  | todo |  |
| 17 | rtkelly13/stack-17-closed-hierarchy-rules |  | todo | checkpoint: tooling done |
| 20a | rtkelly13/stack-20a-aspnetcore-errors |  | todo |  |
| 20b | rtkelly13/stack-20b-aspnetcore-endpoints |  | todo |  |
| 21 | rtkelly13/stack-21-fluentvalidation-diagnostics |  | todo |  |
| 22 | rtkelly13/stack-22-aot |  | todo |  |
| 23a | rtkelly13/stack-23a-finish-the-shop |  | todo |  |
| 23b | rtkelly13/stack-23b-samples-integrations |  | todo |  |
| 24 | rtkelly13/stack-24-core-union |  | todo |  |
| 25 | rtkelly13/stack-25-docs-sync |  | todo |  |

- 2026-11-20: the plan moved from the local `temp/stack/` to this branch (#39), for a Claude Code on the web run. 01 was built and reviewed locally. 02's contract is written; 02 is next.
- 2026-11-20 (web run 1): resumed on Claude Code on the web. SDKs pulled from the MCR images, GitHub through the MCP tools (`WEB-RUN.md`). Starting 02.
- Live tracker (private artifact, updated at each PR boundary): https://claude.ai/artifact/KPm6VvRnY2U7moMKKZufrE
- Pending `spec:` fixes for 04c (batched with 04c's review): from 13a, `GEN-RFG0005` can't pass as written (CS1729; give `NestedError` its own protected constructor, see 13a's `GenRfg0005Proposed.cs`), `building-the-generator.md` still says colliding names are prefixed (now RFG0008), `SHAPE-UNIT` calls `Ok(Unit.Value)` but the tsv says `Ok()`; from 04d, the UC10 "before" snippet in `errors-in-practice.md` doesn't compile on 0.2, GOAL row 12 misses CS0411.
