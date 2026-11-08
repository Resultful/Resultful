# Invariants across a collection

Decision Q44 ([decisions](../decisions.md#second-round)). **`unique`, `references`, `derived`, `order`, `limit` and
`conserved` are Built**, end to end; `scoped` is **Planned**. This page specifies a fifth scope of invariant, the rules that hold across every resource in a collection,
and the vocabulary Matchmark reads for it.

## Why

Matchmark reads invariants in four scopes today ([extensions](extensions.md)):

| Scope | Example | Extension |
|:--|:--|:--|
| One payload | a booking ends after it starts | `x-matchmark-rules` (CEL) |
| One request's parameters | a room or a desk, not both | `x-dependencies` (IDL) |
| A request and its response | the created pet has the name sent | `x-matchmark-ensures` (Arazzo criteria) |
| Two readings of one resource | a version only goes up | `x-matchmark-transitions` (CEL) |

None can say *no two pets share a name*. That needs the whole collection, and it is most of what a twin gets wrong:
- the Pet Store twin's only gap was a unique name, written by hand as owned code;
- on the Ledger, a derived balance took held-out fidelity from 61.9% to 85.7%, and what remains looks like transfers
  conserving money and referring to accounts that exist.

**Behaviour as data, built once.** Each trait below is a declaration, read by one implementation in Matchmark, so no
team writes it again for their API. One declaration feeds five consumers:

```mermaid
flowchart LR
    D["x-matchmark-unique<br/>on a schema, or in an overlay"] --> TW["Twin<br/>enforces it"]
    D --> SU["Suite<br/>generates cases, checks the service"]
    D --> AU["Audit<br/>suggests it, checks it's consistent"]
    D --> GE["Generator<br/>metadata for the service's storage"]
    FL["Fidelity loop<br/>mismatches"] -->|proposes| D
```

**Where declarations stop.** If a behaviour is a property of data visible through the API, declare it. If it needs
knowledge the API doesn't expose (pricing, fraud rules, time-driven work, side effects, other APIs' state), write it as
the twin's owned code, or use a [service session](sessions.md#service-sessions). Each trait must earn its place by the
fidelity it adds across the corpus.

## The traits

| Trait | Extension | Says | Twin | Suite |
|:--|:--|:--|:--|:--|
| [unique](#unique) (**Built**) | `x-matchmark-unique` | no two resources share these values | enforces | duplicates, case variants, renames into a taken value |
| [references](#references) (**Built**) | `x-matchmark-references` | this value names a resource that exists | enforces, and on delete restricts, cascades or clears | references to nothing; deleting what's referenced |
| [derived](#derived) (**Built**) | `x-matchmark-derived` | this value is computed from the request or the resource | computes it | checks each response's value |
| [conserved](#conserved) (**Built**) | `x-matchmark-conserved` | a total over the collection doesn't change | checks only; the change itself is owned code | reads the total before and after |
| [scoped](#scoped) | `x-matchmark-scoped` | a resource belongs to the principal that created it | filters by token | a second principal can't see or change it |
| [limit](#limit) (**Built**) | `x-matchmark-limit` | at most n resources per scope | enforces | the n + 1th create |
| [order](#order) (**Built**) | `x-matchmark-order` | a list is sorted this way | sorts | checks order, across pages too |

Rules that span a collection but fit no trait are not given a general escape hatch yet (a CEL rule over the whole
collection was considered: it can be checked, but neither the twin nor the suite can act on it). Immutable fields need
no trait: `x-matchmark-transitions` already says `self.sku == oldSelf.sku`.

## Common rules

These apply to every trait.

**Placement.** A trait goes on the component schema of the resource (`components.schemas.Pet`), except `order`, which
goes on a list operation. An overlay can add it, so a spec you don't own needn't change:

```yaml
overlay: 1.2.0
info: { title: Pet Store behaviour, version: 1.0.0 }
actions:
  - target: $.components.schemas.Pet
    update:
      x-matchmark-unique:
        - fields: [name]
          ignoreCase: true
```

**The collection.** A schema's collection is the path whose `POST` (or `PUT` to an item path) creates it and whose `GET`
lists it, as the twin infers today: `/pets` and `/pets/{petId}`. A nested path (`/owners/{ownerId}/pets`) is one
collection per parent; `scope: parent` limits a trait to it.

**Fields** are JSON property names as the spec writes them. A nested field is a dotted path (`details.kind`). An unknown
field is diagnostic `MM027`.

**The status a violation answers with** is taken from the operation that breaks the trait, never invented:

| Trait | Statuses tried, in order |
|:--|:--|
| unique, limit | 409, 422, 400 |
| references (an unknown target) | 422, 404, 400 |
| references (deleting what's still referenced, `restrict`) | 409, 422 |
| scoped (another principal's resource) | 404, 403 |

A trait may name its status (`status: 400`), which wins: the Ledger documents 422 for a reused Idempotency-Key, but answers
an unknown account with 400, so its references say so. Otherwise the first status the operation documents is used. When
it documents none of them, the trait still holds in the twin (answered with the first status in the list, as a problem),
and the generator reports the gap (`MM028`), unless the declaration names its status.

**The problem body** follows the documented response's schema; for a validation problem, the error is keyed by the
field: `{"errors": {"name": ["Must be unique: another pet has it."]}}`.

**What the generator does.** The generated server can't enforce a collection rule: it has no storage, and a check before
writing races with another request. Enforcement belongs to the service's storage (a unique index, a foreign key), and
the suite proves the service has it. The generator does emit the declarations as metadata (`{Api}Invariants`, below), so
a service can apply them to its storage from one source.

## unique

**Built.** The Pet Store and the Shelter declare their unique names. What it showed on the Pet Store: the twin from the
spec alone, with the declaration, scores 93.2% against the real service (96 of 103 steps), exactly what the hand-written
owned code scored on the spec without it, and the suite catches a twin of the bare spec on all three cases
(`TwinTests`).

```yaml
x-matchmark-unique:
  - fields: [name]          # one key; two or more fields make a compound key
    ignoreCase: false       # compare strings case-insensitively (ordinal, invariant culture)
    scope: collection       # collection (default), or parent for a nested collection
    nulls: distinct         # distinct (default, as SQL unique indexes do), or equal
```

A list, so a schema can have several keys (`[sku]` and `[name, category]`). `scope: parent` and `nulls` are read; the
twin compares within the collection it holds, which for a nested path is one parent's.

**Semantics.** For every two resources in the scope, the values of the key's fields differ. Under `nulls: distinct`, a
resource with any key field null or absent doesn't collide. Under `ignoreCase`, strings are compared after
`ToUpperInvariant`. Writes that can break it: a create (`POST` to the collection), a `PUT` that creates or replaces, a
`PATCH` that changes a key field. Changing a resource to its own current value never collides.

**Twin.** Before storing, finds any other resource in scope with the same key and answers the status above. Replaces
owned code such as the Pet Store's `Taken` behaviour in `TwinTests`.

**Suite.** Generated cases, each expecting the violation status:

| Case | Request |
|:--|:--|
| `{op}: unique {fields} taken` | create with a key another case already created |
| `{op}: unique {fields} taken, other case` | the same, in a different case, when `ignoreCase` |
| `{op}: unique {fields} renamed into` | `PUT` or `PATCH` a second resource to the first's key |

Each runs once per create operation, after its first successful valid case, with a fresh `Idempotency-Key` so a repeat
isn't a replay. A fresh key succeeding is already every valid case. Checked as `UniqueEnforced`: accepted is a failure,
refused with another status than the documented one a warning. The checks need a target that keeps state, so they run
only with workflows on (a stateless fake runs with them off).

**Audit.** Rule `unique-declared`: a create (a `POST` with a 201) that documents 409, whose created schema declares no
`x-matchmark-unique`, gets a note. Not when the create takes an `Idempotency-Key`: its 409 is the draft's "still being
processed" (the Orders sample).

**Fidelity loop.** `FidelityLoop.ProposeUnique(twin, fidelity)`: a mismatch where the twin answered 2xx and the real API
409, on a create whose body repeats a string an earlier create to the same collection sent, is proposed as a `unique`
declaration on that field (ignoring case when only the case differed), for a person to review.

**Generator.** The `Contract` part's `PetStoreInvariants.Unique` lists the keys
(`new PetStoreUniqueKey("Pet", new[] { "name" }, true)`), for a service to apply to its storage (Q46). When the project
also references EF Core, the `Server` part adds `modelBuilder.ApplyPetStoreUnique<PetRecord>("Pet", map)`: a unique
index per key, where `map` names the entity property for a JSON field (default: the field in Pascal case), since the
stored entity and the API model are different types. Ignoring case is the store's (the Pet Store's `NOCASE` column).
The helper is generated into the service, which already references EF Core, so Matchmark.Conformance needn't.

## references

**Built.** The Ledger declares a transfer's `from` and `to` (its overlay), and its real server passes the checks;
`TraitTests` shows each `onDelete` in the twin, and the suite catching a twin without the declaration.

```yaml
x-matchmark-references:
  ownerId:
    target: /owners/{ownerId}   # the item path of the collection referred to
    onDelete: restrict          # restrict (default), cascade, clear, or none
    required: true              # a null or absent value is allowed when false
    status: 400                 # optional: the status a reference to nothing answers with
```

Keyed by field. `target` is the item path; the referred resource is the one whose id is the field's value.

**Semantics.** On a write, the referred resource must exist (in the same parent scope for nested collections). When the
referred resource is deleted: `restrict` refuses the delete while anything refers to it; `cascade` deletes what refers to
it; `clear` sets the field to null (the field must be nullable, else `MM027`); `none` leaves dangling references, which
are then allowed.

**Twin.** Checks the target on writes; applies `onDelete` on deletes.

**Suite.** After the first successful create of a referring resource:
- `{op}: references {field} to nothing`: the same create with the field set to an id of its type that a read of the
  target confirms is missing (404), expecting the unknown-target status;
- `{op}: references {field} still referring`, for `restrict` when the target can be deleted: a fresh target and a fresh
  resource referring to it are created, then the target is deleted, which must be refused.

Checked as `ReferenceHolds`: an accepted reference to nothing, or an accepted restricted delete, is a failure. Both need
a target that keeps state, so they run with workflows on. Checking `cascade` and `clear` from the suite (reading the
referring resource after the delete) is **Planned**; the twin applies them.

**Generator.** `{Api}Invariants.References` lists them (`new LedgerReference("Transfer", "from", "/accounts/{accountId}",
"restrict")`), for a foreign key. MM027 reports an unknown field, a target path the spec doesn't have, an unknown
`onDelete`, and `clear` on a field that can't be null. A foreign-key helper for EF Core is **Planned**: it needs the
entity's navigation, which only the service knows.

## derived

```yaml
x-matchmark-derived:
  balance:
    on: create        # create, update, or always
    value: request.opening
  total:
    on: always
    value: self.lines.map(l, l.quantity * l.unitPrice).sum()
```

**Built.** The Ledger derives an account's balance from its opening amount, which the independent agent had had to
write as owned code; `DerivedAndOrderTests` shows the twin computing a field and the suite catching a twin without the
declaration.

**Semantics.** The field's value is the CEL expression, evaluated over `request` (the request body), `self` (the
resource as stored, before the field is set) and `oldSelf` (on update, the resource before the change). `create` sets it
once; `update` sets it on each change; `always` does both. A derived field on a schema a request body also uses must be
`readOnly`, or a request could set it; if not, `MM027`, as for a field the schema doesn't have.

**Twin.** Computes the field as it stores the resource. Replaces owned code such as the Ledger's opening-to-balance
behaviour, which the independent agent wrote.

**Suite.** Checks each create's response: the field equals the expression over the request sent and the response's
other fields. Checked as `DerivedHolds`. Checking updates, which needs the reading before, is **Planned**.

## conserved

```yaml
x-matchmark-conserved:
  - total: balance                       # a numeric field, decimal strings included
    except: [openAccount, closeAccount]  # operations that may change it
```

**Semantics.** Over the whole collection, the sum of the field is the same before and after every successful operation
not listed in `except`. A ledger's transfers conserve money; opening an account doesn't. Reads count: a service may do
deferred work when it's next looked at (the Ledger settles a transfer when it's read).

**Built.** The Ledger declares its balances conserved except by `openAccount`; `LedgerTests` injects settlement that
debits without crediting, and the suite catches it. `ConservedTests` shows the twin and the suite on a smaller spec.

**Twin.** Checks only. Which resources change, and by how much, is the operation's business logic, so it stays owned
code; the twin answers 500, naming the trait and the totals, when an owned behaviour's success breaks the total, so a
wrong behaviour is caught in the twin's own tests. What the twin infers itself never moves a total.

**Suite.** With workflows on, reads the total before and after each successful case and each links workflow (from its
first step to its last, so deferred work is inside it) whose operations aren't excepted. A failed call isn't checked.
The total is read through the collection's list, when it has an unpaged one; otherwise resource by resource, over the
ones the run has seen created or referred to (the Ledger has no list of accounts; its transfers' `from` and `to`, declared
as [references](#references), name them). Checked as `TotalConserved`. Walking a paged list whole is **Planned**, and
so are Arazzo workflows.

**Generator.** `MM027` for a total field the schema doesn't have, one that isn't a number or a string (decimal strings
count), or an operation in `except` the spec doesn't have.

## scoped

```yaml
x-matchmark-scoped:
  by: principal     # the security principal that created the resource
  visible: owner    # owner (default): others can't list, read or change it
```

**Semantics.** A resource belongs to the principal whose credential created it. Others don't see it in lists, and get
the scoped status when they read, change or delete it.

**Twin.** Records the creating token and filters by it. In [sessions](sessions.md), each session's token is a principal;
a session mints a second principal for a test with `POST /__fake/sessions/{id}/principals` (Q47).

**Suite.** Needs two credentials (`RegressionOptions.Principals`, `matchmark regress --principal <header>` twice, Q47). Creates
as the first, then reads, lists, changes and deletes as the second. Checked as `ScopeHolds`. Without two principals, the
cases are a gap, not a pass.

## limit

```yaml
x-matchmark-limit:
  max: 5
  scope: parent      # collection, parent, or principal
```

**Semantics.** At most `max` resources in the scope. The `max + 1`th create answers the violation status.

**Built.** `LimitTests` shows each scope in the twin and the suite catching a twin without the declaration.

**Twin.** Enforces: a create (a `POST`, or a `PUT` that creates) when the scope already holds `max` is refused. The twin
records each resource's scope as it's created: `parent` is the create's path values other than the resource's own id
(`/boards/1/pins` and `/boards/2/pins` are counted apart), and `principal` is the request's `Authorization` header, until
[scoped](#scoped) gives principals a fuller meaning.

**Suite.** After a create succeeds, creates more with fresh values (so a unique key doesn't refuse them first) until one
is refused, which must happen within `max + 1`: `{op}, limit {max} reached`. A refusal with a status other than the
documented one is a warning. What it created, but not the case's own resource, is deleted after, so later cases still
have room; so the check needs a `DELETE` on the item path, and isn't run without one. Checked as `LimitEnforced`, with
workflows on.

**Generator.** `MM027` for a `max` below 1 or an unknown scope; `MM028` for a create that documents none of 409, 422,
400, unless the limit names its `status`. **Audit.** Rule `limit-exercised`: a limit above 50 isn't exercised (the cases
would be slow), so nothing checks the service keeps it.

## order

```yaml
# on a list operation, e.g. paths./pets.get
x-matchmark-order:
  by: [createdAt desc, id]   # fields, each asc (default) or desc; ties broken by the next
```

**Semantics.** The list is sorted by the fields in order. A sort parameter the operation takes (`?sort=name`) overrides
it, when the parameter's values are field names.

**Built.** The Pet Store lists pets in id order. **Twin.** Sorts lists by the declared fields (numbers as numbers,
strings ordinally, missing values first). **Suite.** Checks every list response is in the declared order: an array body,
or the first array property of an object body. Checked as `ListOrdered`. Checking that pages continue the order across
pages, and a `sort` parameter overriding it, are **Planned**. `MM027` reports an order field the items don't have.

## Diagnostics

| Id | Severity | When |
|:--|:--|:--|
| MM027 (**Built** for unique) | Warning | A trait names an unknown field, a `target` path the spec doesn't have, `clear` on a non-nullable field, a derived field that isn't `readOnly`, or a `conserved` total that isn't numeric. The trait is ignored. |
| MM028 (**Built** for unique) | Info | An operation that can break a trait documents none of its statuses. The twin answers the first status in the list. |

## Order of work

| Trait | Order | Why |
|:--|:--|:--|
| unique | 1 | the commonest; the Pet Store's owned code is the proof |
| references | 2 | the Ledger's transfers, and most APIs with child resources |
| derived, order | 3 | small, and each removes a known mismatch |
| conserved, limit | 4 | both Built |
| scoped | 5 | needs a second principal in the suite and in sessions |

Each is done when the twin enforces it with no owned code, the suite catches a service that breaks it (an injected
fault, as every check is proven), the audit suggests it, and the fidelity of the corpus samples that need it doesn't
fall.
