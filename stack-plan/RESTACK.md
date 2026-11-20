# Restack manifest

What a local agent needs to rebuild the run's branches into one clean stack after the run (GOAL.md, *Append only*).
One row per branch, updated by the orchestrator at each PR boundary. `fixup!` commits fold into the commit they
name with `git rebase --autosquash`; cherry-picked commits (`-x`) drop out where the original is already below.

| Branch | PR | Started from | Intended base in the final stack | Notes |
|---|---|---|---|---|
| rtkelly13/stack-01-consumption-tests | #38 | main 81d012b | main | |
| rtkelly13/stack-02-test-infra | #40 | 01 @ 24d079b | 01 | review fixes are plain commits 6595f59..732a709 (not fixup!); d083225 is a fixup! of the release-check commit |
| rtkelly13/stack-03-analysis-level | #41 | 02 @ 6595f59 | 02 | lacks 02's fixes 6595f59..732a709 |
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
| rtkelly13/stack-05-named-methods | | 04e @ a96a660 (+ cherry-picks of 04c's spec fixes) | 13a | first core PR |
