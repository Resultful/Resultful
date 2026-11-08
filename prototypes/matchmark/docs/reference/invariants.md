# Invariants across a collection

**Proposed** (decision P1, [decisions](../decisions.md#proposed)). Nothing on this page is built yet. It specifies a fifth
scope of invariant, the rules that hold across every resource in a collection, and the vocabulary Matchmark will read for
it.

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
| [unique](#unique) | `x-matchmark-unique` | no two resources share these values | enforces | duplicates, case variants, renames into a taken value |
| [references](#references) | `x-matchmark-references` | this value names a resource that exists | enforces, and on delete restricts, cascades or clears | references to nothing; deleting what's referenced |
| [derived](#derived) | `x-matchmark-derived` | this value is computed from the request or the resource | computes it | checks each response's value |
| [conserved](#conserved) | `x-matchmark-conserved` | a total over the collection doesn't change | checks only; the change itself is owned code | reads the total before and after |
| [scoped](#scoped) | `x-matchmark-scoped` | a resource belongs to the principal that created it | filters by token | a second principal can't see or change it |
| [limit](#limit) | `x-matchmark-limit` | at most n resources per scope | enforces | the n + 1th create |
| [order](#order) | `x-matchmark-order` | a list is sorted this way | sorts | checks order, across pages too |

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

The first status the operation documents is used. When it documents none of them, the trait still holds in the twin
(answered with the first status in the list, as a problem), and the audit reports the gap (`MM028`).

**The problem body** follows the documented response's schema; for a validation problem, the error is keyed by the
field: `{"errors": {"name": ["Must be unique: another pet has it."]}}`.

**What the generator does.** The generated server can't enforce a collection rule: it has no storage, and a check before
writing races with another request. Enforcement belongs to the service's storage (a unique index, a foreign key), and
the suite proves the service has it. The generator does emit the declarations as metadata (`{Api}Invariants`, below), so
a service can apply them to its storage from one source.

## unique

```yaml
x-matchmark-unique:
  - fields: [name]          # one key; two or more fields make a compound key
    ignoreCase: false       # compare strings case-insensitively (ordinal, invariant culture)
    scope: collection       # collection (default), or parent for a nested collection
    nulls: distinct         # distinct (default, as SQL unique indexes do), or equal
```

A list, so a schema can have several keys (`[sku]` and `[name, category]`).

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

and one valid case, `{op}: unique {fields} free`, with a fresh key, which must succeed. Checked as `UniqueEnforced`.

**Audit.** Rule `unique-declared`: an operation that creates and documents 409 with no `x-matchmark-unique` on its
schema gets a note: "say which fields must be unique; it adds 3 cases".

**Fidelity loop.** A training mismatch where the twin answered 2xx and the real API 409, on a create whose body repeats
an earlier body's value, is proposed as a `unique` declaration on that field, for a person to review.

**Generator.** `PetStoreInvariants.Unique` lists the keys (`new UniqueKey("Pet", ["name"], IgnoreCase: true)`), for a
service to apply as an index. For EF Core, Matchmark.Conformance will offer
`modelBuilder.ApplyMatchmarkUnique<PetRecord>(PetStoreInvariants.Unique, map)`, where `map` names the entity property
for each JSON field, since the stored entity and the API model are different types.

## references

```yaml
x-matchmark-references:
  ownerId:
    target: /owners/{ownerId}   # the item path of the collection referred to
    onDelete: restrict          # restrict (default), cascade, clear, or none
    required: true              # a null or absent value is allowed when false
```

Keyed by field. `target` is the item path; the referred resource is the one whose id is the field's value.

**Semantics.** On a write, the referred resource must exist (in the same parent scope for nested collections). When the
referred resource is deleted: `restrict` refuses the delete while anything refers to it; `cascade` deletes what refers to
it; `clear` sets the field to null (the field must be nullable, else `MM027`); `none` leaves dangling references, which
are then allowed.

**Twin.** Checks the target on writes; applies `onDelete` on deletes.

**Suite.** `{op}: references {field} to nothing` (a fresh id no resource has) expects the unknown-target status;
`{delete op}: references {field} still referring` expects the restrict status, or, for `cascade` and `clear`, reads the
referring resource after. Checked as `ReferenceHolds`.

**Generator.** `{Api}Invariants.References`, for a foreign key.

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

**Semantics.** The field's value is the CEL expression, evaluated over `request` (the request body), `self` (the
resource as stored, before the field is set) and `oldSelf` (on update, the resource before the change). `create` sets it
once; `update` sets it on each change; `always` holds whenever the resource is read. A derived field should be
`readOnly`; if not, `MM027`.

**Twin.** Computes the field as it stores the resource. Replaces owned code such as the Ledger's opening-to-balance
behaviour, which the independent agent wrote.

**Suite.** Checks each success response holding the resource: the field equals the expression. Checked as
`DerivedHolds`.

## conserved

```yaml
x-matchmark-conserved:
  - total: balance                       # a numeric field, decimal strings included
    except: [openAccount, closeAccount]  # operations that may change it
```

**Semantics.** Over the whole collection, the sum of the field is the same before and after every successful operation
not listed in `except`. A ledger's transfers conserve money; opening an account doesn't.

**Twin.** Checks only. Which resources change, and by how much, is the operation's business logic, so it stays owned
code; the twin refuses (500, naming the trait) an owned behaviour that breaks the total, so a wrong behaviour is caught
in the twin's own tests.

**Suite.** In workflows, lists the collection before and after each such step and compares the totals. Checked as
`TotalConserved`. A paged list is walked whole.

## scoped

```yaml
x-matchmark-scoped:
  by: principal     # the security principal that created the resource
  visible: owner    # owner (default): others can't list, read or change it
```

**Semantics.** A resource belongs to the principal whose credential created it. Others don't see it in lists, and get
the scoped status when they read, change or delete it.

**Twin.** Records the creating token and filters by it. In [sessions](sessions.md), each session's token is a principal;
a session can mint a second principal for a test (`POST /__fake/sessions/{id}/principals`, planned with this trait).

**Suite.** Needs two credentials (`RegressionOptions.Principals`, `matchmark regress --principal <header>` twice). Creates
as the first, then reads, lists, changes and deletes as the second. Checked as `ScopeHolds`. Without two principals, the
cases are a gap, not a pass.

## limit

```yaml
x-matchmark-limit:
  max: 5
  scope: parent      # collection, parent, or principal
```

**Semantics.** At most `max` resources in the scope. The `max + 1`th create answers the violation status.

**Twin.** Enforces. **Suite.** Creates up to the limit, then once more: `{op}: limit {max} reached`. Checked as
`LimitEnforced`. A limit above 50 isn't exercised (the cases would be slow); the audit says so.

## order

```yaml
# on a list operation, e.g. paths./pets.get
x-matchmark-order:
  by: [createdAt desc, id]   # fields, each asc (default) or desc; ties broken by the next
```

**Semantics.** The list is sorted by the fields in order. A sort parameter the operation takes (`?sort=name`) overrides
it, when the parameter's values are field names.

**Twin.** Sorts lists; today it lists in insertion order. **Suite.** Checks every
list response, and across pages that each page continues the order. Checked as `ListOrdered`.

## Diagnostics

| Id | Severity | When |
|:--|:--|:--|
| MM027 | Warning | A trait names an unknown field, a `target` path the spec doesn't have, `clear` on a non-nullable field, a derived field that isn't `readOnly`, or a `conserved` total that isn't numeric. The trait is ignored. |
| MM028 | Info | An operation that can break a trait documents none of its statuses. The twin answers the first status in the list. |

## Order of work

| Trait | Order | Why |
|:--|:--|:--|
| unique | 1 | the commonest; the Pet Store's owned code is the proof |
| references | 2 | the Ledger's transfers, and most APIs with child resources |
| derived, order | 3 | small, and each removes a known mismatch |
| conserved, limit | 4 | |
| scoped | 5 | needs a second principal in the suite and in sessions |

Each is done when the twin enforces it with no owned code, the suite catches a service that breaks it (an injected
fault, as every check is proven), the audit suggests it, and the fidelity of the corpus samples that need it doesn't
fall.
