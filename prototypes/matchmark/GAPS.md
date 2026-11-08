# Gaps

Where the prototype falls short of what the [VISION](VISION.md) needs, found in a review of the branch at
`e17d210`. Each gap says what is missing, why it matters, and the evidence. The review read the docs and the
generator; it did not build the solution or run the tests.

## 1. The fake has no state

The fake answers with the spec's examples, with samples built to pass the schema, or with what a test scripted.
Nothing it receives changes what it returns: `createPet` followed by `getPet` does not return the pet just created.

That is the right default for a consumer's unit test, where each call is scripted. It is not enough for:

- **A frontend developer** running the app against the fake, who expects a form submission to show up in the list.
- **A fake of a third-party API**, where the test exercises a flow (create, update, list) rather than one call.
- **Exploratory and end-to-end tests**, which nobody scripts call by call.

The contract already has the seam. A stateful fake is one more implementation of `IPetsHandlers`, generated beside
`PetStoreFake`: resources inferred from paths (`/pets` and `/pets/{petId}` are one collection keyed by `petId`),
an in-memory store per collection, the spec's conventions for ids, paging and ETags, and the existing seed scenarios
as its starting data. Operations that are not plain CRUD stay scripted, or get behaviour written for them (gap 2).

*Status:* done, from the spec alone rather than generated per API. `SpecTwin` (Matchmark.Testing) infers collections
from the paths and keeps resources in memory: POST creates (assigning the id and what the response requires), GET
reads and lists, PUT replaces or creates, PATCH merges, DELETE removes, and requests are checked as the spec says;
ETags with If-Match, and Idempotency-Key, behave as its headers say. `Seed` gives it starting data;
`matchmark twin <spec>` and `app.UseMatchmarkTwin(spec)` serve it. The Pet Store twin passes the whole generated
suite but one step: a second pet may not take a name already used, which the spec doesn't say (`TwinTests`).

## 2. Nothing measures a fake against the real API

Every check in the prototype compares code with the spec: the compiler, the round-trip document, client
conformance, outcome coverage. None compares the fake with the service it stands in for. A fake can conform to the
spec and still behave differently from the real API: which of two documented outcomes it picks, what a list returns
after a delete, what an error says.

What is missing is a **fidelity score**:

1. Run the same request sequences against the real API (a sandbox, on a schedule, never production) and against the
   fake, from the same seed state.
2. Normalise volatile fields (ids, timestamps, ETags) and compare status, outcome case and body.
3. Score the share of steps that match, per operation and overall, and store a floor that only rises.
4. PR builds replay the last recorded real responses, so they stay fast and need no credentials.

Sequences come from the spec's `links` and Arazzo workflows, from Schemathesis stateful runs, and from the contract
suite itself, which already runs against any implementation.

The roadmap's differential runner (stage 5) compares two of our own implementations. This gap is the same machinery
pointed at someone else's API.

*Status:* the score, done: `RegressionReport.Fidelity(real)` (and `matchmark fidelity <spec> <real> <fake> --floor`)
pairs the suite's steps, case by case and workflow step by step, and counts those whose status and body match, a
problem compared by which values it names rather than its wording, volatile values left out. The real API's steps are
the denominator, so a step the fake never reaches is a mismatch. The Pet Store twin, seeded with the service's data,
scores 87%; its mismatches are the unique-name rule, two problems worded differently, and a list after the two have
diverged. PR builds replay a recording (`RegressionReport.Record`, `matchmark fidelity --record`, then the recording in
place of the real URL), which scores the same as the live run.

## 3. Behaviour beyond CRUD has no author

VISION leaves "the behaviour inside each operation" to people or to an agent. For a service that is right. For a fake
of someone else's API, nobody on the team knows the behaviour well enough to write it, but the fidelity score
(gap 2) can tell whether a guess is right. That makes it a job an agent can do safely:

- Each mismatch becomes a task: the operation, the sequence, the real response and the fake's.
- The agent writes behaviour only in the fake's owned code. The contract and the compiler bound what it can return.
- A change is kept only if the score rises on a held-out set of sequences the agent never saw, and no operation's
  score falls. The agent never edits generated code, the comparison rules or the floor.

*Status:* the seam and the loop exist. `FidelityReport.Mismatches` is the task list (each names the step, the twin's
answer and the real one); `SpecTwin.On(operationId, behaviour)` is where owned behaviour goes, run after the request is
checked; the per-operation scores are what a change must not lower. `TwinTests` writes the Pet Store's unique-name rule
there: the score rises from 87% to 93% and every 409 mismatch goes. ✓ The loop around the writer: `FidelityLoop.Split`
holds out a share of the sequences (whole workflows, by a stable hash), `FidelityReport.Tasks` gives the training
mismatches as structured tasks, and `FidelityLoop.Judge` keeps a change only if the held-out score rises and no
operation falls (`matchmark fidelity --tasks --gate`). `TwinTests` keeps the unique-name rule and rejects a change
that refuses every new pet. ✓ A run with an agent writing
(`AgentLoopTests`, Ledger): three attempts from the training tasks alone, one kept (held out 61.9% to 85.7%), one
rejected because an operation fell, one rejected because only training rose (LAYERS.md has the log). Then an independent
agent, limited to `LedgerOwned.cs` and the training tasks, reached the same 85.7% on its own in six attempts
(`The_owned_behaviour_passes_the_gate`). `matchmark-agent-loop.yml` runs that loop unattended, given an
`ANTHROPIC_API_KEY` secret.

## 4. Only Petstore has been through it

The sample spec is the only one the generator has run on. Constructs real specs use heavily are skipped with
`MM002`: inline object schemas, `allOf`, `oneOf` without a discriminator, `default` responses, and operations with
more than six responses (`Results<...>` takes at most six). Large public specs, the obvious targets for gap 1's fakes,
use all of these.

A run against one large spec is already on the roadmap (stage 1). It should be a **corpus**, not one spec: a set of
real specs built in CI, with the share of operations generated without a diagnostic tracked as a number that only
rises.

*Status:* done. The corpus (samples/) holds six specs in its ledger, each compiled, scaffolded and, for four, run as
a generated server against its suite; inline objects, `allOf`, `oneOf` without a discriminator, `default` responses,
more than six responses, forms, deep-object parameters and redirects are all generated. GitHub's and Stripe's specs
are fetched and generated in CI (`.github/workflows/matchmark.yml`): 98.7% and 99.8% of their operations, compiling.

## 5. The generator does more work than it needs to

- `Emitter.cs` is about 2,400 lines in one class. Stage 2's semantic model should come before more outputs are added,
  or each new part makes that refactor harder.
- The Verify part re-parses the whole spec whenever any hand-written endpoint changes, because the parsed document is
  not an incremental step of its own. `Endpoint` also carries a `Location`, so editing above a controller action
  re-runs verification. On a large spec with Verify on, that is a re-parse on most edits to a controller file.
- All output is emitted together, so any change to the spec regenerates every file. Splitting the output per tag
  would keep the rest cached.

## 6. Smaller findings

- **Coupling.** Generated contracts depend on Resultful and OneOf. That fits this repository, but it is a cost for a
  team that only wants the fake or the testing framework. A contract that doesn't depend on Resultful (plain unions,
  with Resultful as an adapter) would widen who can adopt it.
- **`default(Result<,>)`** reads as a success holding `null` (README, finding 6). That is a bug in Resultful itself,
  not in the prototype, and is worth an issue of its own.
