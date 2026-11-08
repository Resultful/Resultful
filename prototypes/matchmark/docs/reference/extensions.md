# Extensions

Behaviour the specification formats can't express is declared in extensions, each in a grammar that already exists
([COMPLETENESS.md](../COMPLETENESS.md) explains the choice). They can be written in the spec, or added by an Overlay so
a spec you don't own needn't change ([LAYERS.md](../LAYERS.md)).

| Extension | On | Grammar | Checked by | Enforced by |
|:--|:--|:--|:--|:--|
| `x-matchmark-rules` | a schema | CEL over `self` | `SchemaRule` | the generated server and client, with `Matchmark.Rules`; the twin |
| `x-matchmark-transitions` | a schema | CEL over `self` and `oldSelf` | `TransitionRule` | the twin, in workflows |
| `x-dependencies` | an operation | IDL | `InvalidRejected`, one case per dependency | the generated server and client, with `Matchmark.Rules`; the twin |
| `x-matchmark-ensures` | an operation | Arazzo criteria | `Postcondition` | |
| `x-matchmark-relations` | an operation | named relations | `RelationHolds` | |

All are **Built**. Without `Matchmark.Rules` referenced, the generator reports declared rules as not enforced
(`MM025`).

**Planned** (Q44): a fifth scope, invariants across a whole collection ([invariants](invariants.md)):

| Extension | On | Says |
|:--|:--|:--|
| `x-matchmark-unique` (**Built**) | a schema | no two resources share these values |
| `x-matchmark-references` (**Built**) | a schema | a value names a resource that exists; what a delete does to it |
| `x-matchmark-derived` (**Built**) | a schema | a value is computed (CEL over `request`, `self`, `oldSelf`) |
| `x-matchmark-conserved` (**Built**) | a schema | a total over the collection doesn't change |
| `x-matchmark-scoped` | a schema | a resource belongs to the principal that created it |
| `x-matchmark-limit` (**Built**) | a schema | at most n resources per scope |
| `x-matchmark-order` (**Built**) | a list operation | the list is sorted this way |

And `x-matchmark-rules` moves onto .NET's validation layer as `IValidatableObject` ([validation](validation.md)).

## x-matchmark-rules

A list of rules that must hold for every instance of the schema, in the subset of
[CEL](https://cel.dev/) Kubernetes uses for `x-kubernetes-validations`: comparisons, arithmetic, `has`, `size`,
`timestamp`, `all`, `exists`, `map`, `sum`.

```yaml
x-matchmark-rules:
  - rule: timestamp(self.startsAt) < timestamp(self.endsAt)
    message: A booking ends after it starts.
  - rule: "!has(self.roomId) || !has(self.deskId)"
    message: Book a room or a desk, not both.
```

A broken rule in a request is the spec's validation 400, naming the fields. The suite's valid cases satisfy every rule,
and each invalid case breaks exactly one.

## x-matchmark-transitions

Rules between two readings of the same resource (the same schema and id): `oldSelf` is the earlier reading.

```yaml
x-matchmark-transitions:
  - rule: "!has(oldSelf.version) || self.version >= oldSelf.version"
    message: A flag's version only goes up.
```

## x-dependencies

Dependencies between an operation's parameters, in [IDL](https://github.com/isa-group/IDL): `Or`, `OnlyOne`,
`AllOrNone`, `ZeroOrOne`, `IF … THEN …`, and comparisons.

```yaml
x-dependencies:
  - ZeroOrOne(roomId, deskId);
  - minMinutes <= maxMinutes;
```

## x-matchmark-ensures

Postconditions: [Arazzo criteria](https://spec.openapis.org/arazzo/v1.1.0.html#criterion-object) over the request and
the response, checked on every success of the operation.

```yaml
x-matchmark-ensures:
  - condition: $response.body#/name == $request.body#/name
  - condition: $response.body#/status == 'booked'
```

## x-matchmark-relations

Metamorphic relations: pairs of requests whose answers must relate. `prefix` names a parameter whose results with `n`
must be the start of its results with `n + 1`. Without a declaration, a paged list's limit parameter is assumed to have
one.

```yaml
x-matchmark-relations:
  - prefix: limit
    description: The first n products are the start of the first n + 1.
```

## AsyncAPI, read as OpenAPI

When an AsyncAPI document is read as webhooks (see [specifications](specifications.md)), each webhook carries:

| Extension | Value |
|:--|:--|
| `x-asyncapi-action` | `send` or `receive`, from the AsyncAPI operation. |
| `x-asyncapi-channel` | The channel's address. |
