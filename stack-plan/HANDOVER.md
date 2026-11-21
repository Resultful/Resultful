# Handover: resuming the Resultful stack run

Read this first, then `AGENTS.md` on `main` (`git show origin/main:AGENTS.md`), which overrides every other file here.
`GOAL.md`, `VERIFY.md` and `contracts/` describe the original plan; where they mention TargetApi stubs, a ledger,
spec fixtures, drafting ahead, `fixup!` commits or append-only rules, they are superseded (see the note at the top
of `GOAL.md`).

## How the run works now

- One PR at a time on the real preceding branch; one squashed commit per PR; update by amending and
  `git push --force-with-lease`; no merge commits. PR bodies use `.github/PULL_REQUEST_TEMPLATE.md` (≤ 30 lines).
- Per PR: an implementer agent builds it and opens a draft PR; a fresh reviewer agent reviews it (plus an adversary
  for 08a, 08b, 09, 13b, 15, 16a, 16b, 17); findings go back to the implementer; the PR is done when CI is green and
  the review's findings are fixed. Then the next PR starts from its head.
- A change to a lower PR means rebasing every branch above it (`git rebase --update-refs`) and force-pushing them
  together. Rewriting several branches at once needs the maintainer's go-ahead in the session.
- Agents never merge, never publish, never push to NuGet. Keep personal details out of commits and PR bodies.
- Environment (Claude Code on the web): SDKs 11.0.100, 10.0.401, 9.0.315 come from the MCR images
  (`tools/pull-sdk.py`, see `WEB-RUN.md`); use the GitHub MCP tools, not `gh`.

## State (2026-11-21)

Stack A (0.2.0, on `main`): #38 → #40 → #41 → #43 → #49, all green. #49 keeps AssemblyVersion 1.0.0.0, checks
AnyCPU and a beta05→0.2.0 upgrade, and its body has the merge steps (squash in order; rebase the next branch onto
`main` between merges). Ryan lands it and publishes 0.2.0.

Stack B (0.3, on #34): 04e #47 → 05 #51 → 06 #54 → 07 #58 → 08a #59 → 08b #60 → 09 #61 → 10 #62 → 11 #63 → 12 #64
→ 12b #65 → 18 #66 → 19 #67 → 13a #68 → 13b #69 → 13c #70 → 15 #71 → 16a #72 → 16b #73 → 16c #74 → 17 #75 → 20a #76.
All green after review, except:
- **#73–#76 were just force-pushed** (RF0004 changed to info in 16b, then 16c, 17, 20a rebased). Check CI on all four
  before going on. 20a's last changes (a `ToHttpResult` overload for error unions, `architecture.md` marking the
  endpoint filter and validation bridge as 20b) were reviewed only as a plan; give #76 a quick look.
- Parked (closed PRs, branches kept): 04c spec harness #42, 04d Shop #44, 04d2 end-to-end #45.

## Next, in order

1. **21** `fluentvalidation-diagnostics`, on 20a. Reuse the reviewed code of closed #52
   (`origin/rtkelly13/stack-21-fluentvalidation-diagnostics`, ignore its history and stand-in core): FluentValidation
   12.0.0 floor, `AttemptedValue`/`CustomState` dropped, logs/traces never carry leaf properties, `resultful.error.type`
   tag, docs on `RecordFailure(null)` and `Activity.Current`.
2. **23a** the Shop, moved ahead of 20b: one cohesive `samples/Resultful.Shop` built from the packed packages, with
   real HTTP cases (the HTTP map: 400 ValidationProblemDetails, 404, 409, 500 with no `Message` outside Development),
   an OpenAPI check and use cases from `docs/errors-in-practice.md`. 04d's branch has useful material.
3. **20b** endpoint filter, `WithResultful`, Minimal API validation bridge, OpenAPI statuses per route from
   `ResultHttpResult<T, TError>` (mapped statuses assignable to `TError`, 500, 400 only when `TError` can be a
   `ValidationFailed`), tested on the Shop.
4. **22** native-AOT publish of the Shop on net11.0, no IL2xxx/IL3xxx (use 12b's resolver pattern; never `AddResultful`).
5. **23b** samples: MediatR, Polly, EF Core.
6. **24** `[Union]` on the core result types on net11, correctness matrix, BenchmarkDotNet allocations net10 vs net11,
   a new decision recording the numbers (decision 3).
7. **25** docs sync: roadmap statuses, `docs/log.md`, decision statuses (15, 16 item 8, 17 accepted by Ryan), doc
   snippets match the Shop, migration notes, stable 0.3.0 prepared (not published).

Also owed: mutation runs 1 (core, at 19) and 2 (tooling, at 17), with survivors noted in those PR bodies.

## For Ryan (not agents)

Land stack A and publish 0.2.0; create the `nuget-preview` environment (branch rule `rtkelly13/stack-*`, himself as
required reviewer, secret `NUGET_PREVIEW_KEY`) and dispatch preview.1 (from 19) and preview.2 (from 17); move stack B
onto stack A in his local restack (port 11/12b/13c's `eng/*.cs` changes into `eng/build.cs`).

## Where decisions are

`DECISIONS-TAKEN.md` (newest rows at the end; Ryan accepted every tracker recommendation on 2026-11-21).
Live tracker: https://claude.ai/artifact/KPm6VvRnY2U7moMKKZufrE
