# Running the stack on Claude Code on the web

How a cloud session differs from the local machine, and what to do instead. Every agent reads this after `GOAL.md`.

## SDKs

The session's proxy blocks the Microsoft download hosts (`dot.net`, `builds.dotnet.microsoft.com`), so
`dotnet-install.sh` can't run. The container registry is reachable, so pull the SDKs out of the official images:

```bash
python3 stack-plan/tools/pull-sdk.py dotnet/sdk 11.0 /root/.dotnet
python3 stack-plan/tools/pull-sdk.py dotnet/sdk 10.0.401-noble /root/.dotnet
python3 stack-plan/tools/pull-sdk.py dotnet/sdk 9.0.315-noble /root/.dotnet
export PATH=/root/.dotnet:$PATH DOTNET_ROOT=/root/.dotnet
```

Set the `export` in each shell command: shell state doesn't persist between commands.

## GitHub

There is no general `gh` CLI. Use the GitHub MCP tools instead of the commands `GOAL.md` names:

| `GOAL.md` says | Use |
|---|---|
| `gh pr create --draft --base B` | `mcp__github__create_pull_request` with `draft: true`, `base: B` |
| `gh pr checks <n> --json name,bucket` | `mcp__github__pull_request_read` method `get_check_runs` |
| `gh pr view 39 --comments`, `gh api .../pulls/39/comments` | `pull_request_read` methods `get_comments` and `get_review_comments` |
| `gh pr diff` | `pull_request_read` method `get_diff`, or `git diff base...head` locally |
| `gh run download`, run logs | `mcp__github__actions_list` (artifacts), `mcp__github__get_job_logs` |
| `gh workflow run ci.yml --ref B` | `mcp__github__actions_run_trigger` method `run_workflow` |

Git push and fetch work as usual over HTTPS.

## Waiting on CI

Foreground `sleep` is blocked. An implementer waits with a background command (`sleep 240` with
`run_in_background`) and checks `get_check_runs` when it returns. The orchestrator does the same between PRs.

## NuGet

This session has no permission to push to NuGet, and the agents never try: no `dotnet nuget push`, no dispatch
of a publishing job, previews included. Reading from nuget.org (restore) works.
