# Twins and fidelity

A **twin** is a fake that remembers, measured against the real API. It is the later layer of the vision, built on the
hosted copy ([sessions](sessions.md)).

## SpecTwin

**Built** (`Matchmark.Testing.SpecTwin`, an `HttpMessageHandler`; `matchmark twin` serves one).

- **Collections from paths.** `/pets` and `/pets/{petId}` are one collection. `POST` creates, `GET` reads and lists,
  `PUT` and `PATCH` change, `DELETE` removes.
- **Requests checked** as the spec says, with its rules and dependencies.
- **HTTP semantics.** ETags and `If-Match` (412 when stale), `Idempotency-Key` replay (422 on reuse with another
  body), paging, and `Location` on creation.
- **Seeding.** `twin.Seed("/pets", items)` adds starting data, keyed by each item's id.

### Owned behaviour

Behaviour the spec doesn't state is added in owned code (decision Q4):

| Method | Runs | Answer |
|:--|:--|:--|
| `twin.On(operationId, call => TwinAnswer?)` | after the request is checked, before what the twin infers | replaces the inferred answer; `null` lets it through; `TwinAnswer.Continue(body, effects)` lets it through with a rewritten body |
| `twin.OnRejected(rejection => TwinAnswer?)` | on every 4xx the twin answers, its own checks included | replaces it; a `null` body is an empty one |

`TwinCall` gives the operation, path values, query, body and the twin's stored resources, all as copies. Changing them
changes nothing. A behaviour changes the twin only by what its answer declares (**Built**: the seam narrowed to declared
effects, `TwinSeamTests`):

- `TwinAnswer.Continue(body)` lets the request through to the twin's own behaviour, with `body` in place of the one
  sent: what's stored is what the behaviour gave.
- `Effects` (on any 2xx answer, or a `Continue`) are `TwinEffect.Replace(collection, id, resource)` and
  `TwinEffect.Remove(collection, id)`. The twin checks each one before applying any of them. The resource must exist,
  and a replacement must fit its collection's schema as a response would. Conserved totals must still hold
  (`x-matchmark-conserved`). If any check fails, the answer is a 500 naming the refused effect, and nothing is applied.

That keeps code an agent writes in the fidelity loop inside what the spec allows the twin to hold. The Ledger behaviour
the agents wrote (`LedgerOwned`) is written this way, and still passes the held-out gate.

## Fidelity

**Built** (`RegressionReport.Fidelity`, `matchmark fidelity`).

The same suite runs against the real API and the twin from the same starting state. A step matches when its status
matches and its body matches once volatile values are left out (ids, names ending in `Id` or `At`, ETags, trace ids,
and any named). The **fidelity score** is the share of matching steps, per operation and overall.

- **Recordings.** `RegressionReport.Record(real)` (or `--record`) saves the real API's answers as JSON; a recording in
  place of the real URL is replayed, so pull-request builds need neither the real API nor its credentials, and score
  the same as the live run.
- **A floor.** `--floor` fails a run below a score, so the number only rises.

## The gated loop

**Built** (`FidelityLoop`, `matchmark fidelity --tasks --gate`). Vendor-neutral: Matchmark owns the tasks, the split and
the gate; any writer, a person or an agent, fills it (Q5).

1. **Split.** `FidelityLoop.Split(recording, heldOutPercent)` holds out a share of the sequences: whole workflows, or
   single cases, chosen by a stable hash of their names (FNV-1a), so the split is the same on every run and machine.
2. **Tasks.** `FidelityReport.Tasks` gives each training mismatch: the step, the twin's answer, the real one, and
   where they first differ. The held-out mismatches are never shown.
3. **Gate.** `FidelityLoop.Judge(before, after)` keeps a change only if the held-out score rose and no operation's
   held-out score fell.
4. **Declarations before code.** `FidelityLoop.ProposeUnique` turns 409 mismatches on repeated values into proposed
   `x-matchmark-unique` declarations ([invariants](invariants.md#unique)), which the twin then enforces with no owned code.

`AgentLoopTests` runs the loop on the Ledger twin. An independent agent, given only the training tasks and
`LedgerOwned.cs` to edit, raised held-out fidelity from 61.9% to 85.7% with its first attempt; four later guesses were
kept but moved nothing, and one was rejected because `getTransfer` fell.

## Who runs the loop

An agent session runs the loop, not CI (Q57): the repository holds no model key. The writer may edit only
`LedgerOwned.cs`, and the gate (`AgentLoopTests`) judges its change on the held-out sequences.

## Planned

| Item | Roadmap |
|:--|:--|
| Twins measured on a schedule against a real sandbox, with drops reported as upstream changes | step 6 |
| Twins published as packages, each with its fidelity score and the date it was measured | step 6 |
