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

## Parallel batch (2026-11-21)

Built as siblings on 20a's head, each reviewed and fixed, all green: 21 #78 (`stack-21-fv-diagnostics`; the old
`stack-21-fluentvalidation-diagnostics` branch is obsolete), 21b #79 (`stack-21b-fsharp`, F# interop only), 21c #81
(`stack-21c-assertions`, `ShouldFind<TLeaf>()`), 20c #80 (`stack-20c-hotchocolate`, HotChocolate ≥ 16.6.0, leaf
fields opt-in). Before going on: restack them linearly on 20a in the order 21 → 21b → 21c (20c moves above 20b when
20b exists, or stays on 21c until then) with `git rebase --onto`, resolving the shared-file blocks
(`Directory.Packages.props`, `Resultful.slnx`, `docs/architecture.md`, `eng/verify-package.cs`, `README.md`,
`RELEASE_NOTES.md`) by keeping every block; retarget each PR's base; force-push needs Ryan's go-ahead.

## Next, in order (consolidated 2026-11-21 after an independent scope cross-check)

Decisions 18–21 are merged on `main` (#77). Decision 22 (native C# paths) is accepted, in #84. The 0.3 core is
finished at 24 (preview.1 moves there from 19).

1. **Restack, once** (after Ryan lands stack A and says go; one pass, not two): stack B onto stack A, then the
   siblings into a line: 20a → 21 #78 → 21b #79 → 21c #81 → 20c #80 → 24b #86 → 24c #88 → 24d #85 (24e #87 is docs only; it can go anywhere). Keep every shared-file
   block; port `eng/*.cs` script edits (incl. 21b/21c's `eng/verify-package.cs`) into `eng/build.cs`; fold 21b's
   three test projects into one (AGENTS.md: one per satellite); switch satellite tests above 21c to 21c's assertions.
2. **24b** #86, **24c** #88, **24d** #85, **24e** #87: all reviewed, fixed and green (2026-11-21). Nothing left but
   the restack. Notes for it: 24b changed the generator (nullable attributes only when the consumer can see them);
   24c adds `ShapeLinqExtensions`; 24d fixed `LogSink` (snapshot) in the AspNetCore tests; the probe code for 24e is
   kept on `rtkelly13/archive/24e-await-probe`.
3. **24** `[Union]` on the core types (net11), with a `benchmarks/` project. If it slips, it moves after 0.3.
4. **23a** the Shop: HTTP host and worker, one handler per style, tests on 21c's assertions.
5. **20b** endpoint filter, `WithResultful`, Minimal API validation bridge, OpenAPI statuses per route, on the Shop.
6. **20d** the Shop's GraphQL host on `Resultful.HotChocolate`.
7. **22** native-AOT publish of the Shop's HTTP host only.
8. **25** docs sync and release prep (row in `GOAL.md`): decisions sync, guide, migration page, release notes per
   package, API pass, AssemblyVersion, mutation run 3.

Off the release path: **24e** (the `await` probe; findings note only) and **23b** (samples). Packed-consumer checks
for every new satellite (`tests/Compatibility`) are a 0.3 must-have, in 25 or alongside the restack. The future list
(after 0.3, candidates, ideas) is in the tracker's Future scope section.

Also owed: mutation runs 1 (core, at 19) and 2 (tooling, at 17), with survivors noted in those PR bodies.

## For Ryan (not agents)

Land stack A and publish 0.2.0; create the `nuget-preview` environment (branch rule `rtkelly13/stack-*`, himself as
required reviewer, secret `NUGET_PREVIEW_KEY`) and dispatch preview.1 (from 19) and preview.2 (from 17); move stack B
onto stack A in his local restack (port 11/12b/13c's `eng/*.cs` changes into `eng/build.cs`); merge #77 (decisions 18, 19).

## Where decisions are

`DECISIONS-TAKEN.md` (newest rows at the end; Ryan accepted every tracker recommendation on 2026-11-21).
Live tracker: https://claude.ai/artifact/KPm6VvRnY2U7moMKKZufrE

The tracker's "Handing over" section mirrors this file in short; each PR row there has its branch and what is
left to do, and "Partly done" marks a green PR that still owes part of its scope (21b, 21c, 20c). Agent briefs to
reuse are in `stack-plan/briefs/`.
