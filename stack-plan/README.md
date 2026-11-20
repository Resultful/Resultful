# The stack plan (review only, never merged)

This PR exists so the plan for the two-stack Resultful build can be reviewed and commented on. It is never merged.

The working copy lives in `temp/stack/` (gitignored) on the orchestrating machine; agents read it from there, so the
paths inside these files say `temp/stack/...`. The orchestrator mirrors every change to `GOAL.md`, `VERIFY.md`,
`DECISIONS-TAKEN.md` and `contracts/` into this branch as a new commit, so the history shows how the plan moved.
`PROGRESS.md`, implementer reports and review transcripts stay local.

- `GOAL.md`: the outcome, the rules for every PR, and the stack table (what each PR delivers).
- `VERIFY.md`: how each gate works and who owns it.
- `DECISIONS-TAKEN.md`: defaults taken without asking, and decisions Ryan made during planning.
- `contracts/NN.md`: the exact scope handed to each PR's implementer, written just before it starts.

Comments on this PR are read by the orchestrator and folded into the working copy.
