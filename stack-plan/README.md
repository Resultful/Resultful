# The stack plan (never merged)

This branch is the working copy of the plan for the two-stack Resultful build:

- **Stack A** (01–04b) ships 0.2.0.
- **Stack B** (00, 04c–25) builds 0.3.

PR #39 shows it for review. The PR is never merged and is closed when the run ends.

## Starting or resuming the run

Check out `rtkelly13/stack-plan`. Read `GOAL.md`, then `PROGRESS.md`, and continue from the first PR that isn't
done. `GOAL.md` says how to keep this branch current and how to read Ryan's comments on #39.

## Files

| File | What it holds |
|---|---|
| `GOAL.md` | The outcome, the rules for every PR, and the stack table. Start here. |
| `WEB-RUN.md` | SDKs and GitHub on Claude Code on the web. |
| `VERIFY.md` | How each gate works and which PR owns it. |
| `DECISIONS-TAKEN.md` | Ryan's planning decisions, and defaults taken where the docs are silent. |
| `PROGRESS.md` | One row per PR, plus run notes. |
| `contracts/NN.md` | The scope handed to one PR's implementer, written just before it starts. |
| `reports/NN.json` | An implementer's claims, which the orchestrator checks. |
| `probes/` | The small programs behind decision 17 (unions on .NET 11), and the Paket consumer. |
| `reviews/` | The planning reviews (agy/Gemini, Fable). History only. |

Review comments on #39 are read at each PR boundary and treated as Ryan's instructions.
