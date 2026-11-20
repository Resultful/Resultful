# Restack manifest

What a local agent needs to rebuild the run's branches into one clean stack after the run (GOAL.md, *Append only*).
One row per branch, updated by the orchestrator at each PR boundary. `fixup!` commits fold into the commit they
name with `git rebase --autosquash`; cherry-picked commits (`-x`) drop out where the original is already below.

| Branch | PR | Started from | Intended base in the final stack | Notes |
|---|---|---|---|---|
| rtkelly13/stack-01-consumption-tests | #38 | main 81d012b | main | |
| rtkelly13/stack-02-test-infra | #40 | 01 @ 24d079b | 01 | review fixes are plain commits 6595f59..732a709 (not fixup!); d083225 is a fixup! of the release-check commit |
| rtkelly13/stack-03-analysis-level | #41 | 02 @ 6595f59 | 02 | fixups d73ff76 and earlier; carries cherry-picks of 02's 6595f59..d083225 (c4d6a61..647f124, drop on restack); `eng/api-nullable-0.2.0.txt` is a split of the api-diff list that exists only to avoid conflicts: merge it back into `eng/api-diff-0.2.0.txt` in the local pass |
| rtkelly13/stack-04-per-package-release | #43 | 03 @ be6b76a | 03 | lacks 02's commits after 6595f59 |
| rtkelly13/stack-04b-release-0.2 | | 04 @ 6eb5116 | 04 | may carry cherry-picks (-x) from 02/03 |
| rtkelly13/error-design (00) | #34 | main | 04b | moves to the bottom of stack B |
| rtkelly13/stack-04c-spec-harness | #42 | 00 @ ec55581 | 00 | wiring into root files happens in the local pass |
| rtkelly13/stack-04d-reference-app | #44 | 04c @ c4ea0e7 | 04c | carries cherry-picks of 04c a3c298d, 0d0c4c5, 5b8b23a (-x); one fixup! commit bd33431 |
| rtkelly13/stack-04d2-end-to-end | #45 | 04d @ bd33431 | 04d | |
| rtkelly13/stack-13a-generator-target | #46 | 04c @ 5b8b23a | 04e | drafted ahead; 9ed766b is an unchanged copy of #37 |
| rtkelly13/stack-12b-text-json | | 04c @ 5b8b23a | 12 | drafted ahead against target stubs; switch to the real core in the local pass |
| rtkelly13/stack-04e-publish-preview | #47 | 04d2 @ 1fedf62 | 04d2 | edits `ci.yml`, conflicts with stack A's CI changes are expected in the local pass |
| rtkelly13/stack-12b-text-json | #48 | 04c @ 5b8b23a | 12 | drafted ahead; tests use a temporary StandInCore |
| rtkelly13/stack-16a-analyzers-core | #50 | 04c @ f72ad89 | 15 | drafted ahead |
| rtkelly13/stack-05-named-methods | #51 | 04e @ a96a660 | 13a | cherry-picks of 04c c4d7ead..9a465b0 (-x) and the `///` lines of 03's be6b76a/3516048 (drop on restack); single PublicAPI pair (03's per-target split arrives in the restack) 2104c15 is an `-s ours` merge commit: drop it |

Note for the local pass: cherry-picked commits aren't ancestors, so a PR's merge base stays at the commit it started from. Files a base rewrote wholesale were split (e.g. `eng/api-nullable-0.2.0.txt`) to keep PRs mergeable; fold them back.
| rtkelly13/stack-06-error-record | | 05 | 05 | |
| rtkelly13/stack-21-fluentvalidation-diagnostics | #52 | 04c @ 9a465b0 | 20b | drafted ahead; shares a stand-in core in `tests/Resultful.Satellites.StandIn` |
| rtkelly13/stack-20a-aspnetcore-errors | #53 | 04c @ 9a465b0 | 17 | drafted ahead; spec: 8329250 |
| rtkelly13/stack-16b-analyzers-more | | 16a | 16a | drafted ahead |
