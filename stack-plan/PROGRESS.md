# Progress

Rows are in stack order.

| # | Branch | PR | State | Note |
|---|---|---|---|---|
| 00 | rtkelly13/error-design | #34 | green | stack B base: rebase onto 04b, retarget |
| 01 | rtkelly13/stack-01-consumption-tests | #38 | done (#38, head 24d079b, CI green, reviewed) | copied from #36; stack A base = main |
| 02 | rtkelly13/stack-02-test-infra |  | building | contract written |
| 03 | rtkelly13/stack-03-analysis-level |  | todo |  |
| 04 | rtkelly13/stack-04-per-package-release |  | todo |  |
| 04b | rtkelly13/stack-04b-release-0.2 |  | todo | checkpoint; Ryan merges 00–04b |
| 04c | rtkelly13/stack-04c-spec-harness |  | building | drafted alongside 02–04 on 00; contract written |
| 04d | rtkelly13/stack-04d-reference-app |  | todo | checkpoint; may run alongside 02–04 |
| 04e | rtkelly13/stack-04e-publish-preview |  | todo | stack B; preview job |
| 13a | rtkelly13/stack-13a-generator-target |  | todo | copied from #37 |
| 05 | rtkelly13/stack-05-named-methods |  | todo |  |
| 06 | rtkelly13/stack-06-error-record |  | todo |  |
| 07 | rtkelly13/stack-07-attributes |  | todo |  |
| 08a | rtkelly13/stack-08a-own-storage |  | todo |  |
| 08b | rtkelly13/stack-08b-zero-guard |  | todo |  |
| 09 | rtkelly13/stack-09-result-error |  | todo |  |
| 10 | rtkelly13/stack-10-validation |  | todo |  |
| 11 | rtkelly13/stack-11-oneof-split |  | todo |  |
| 12 | rtkelly13/stack-12-void-to-unit |  | todo |  |
| 12b | rtkelly13/stack-12b-text-json |  | todo | JSON structural assertions for every later PR |
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
