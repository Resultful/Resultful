---
type: guide
title: "Errors in practice"
description: "Use cases from a consuming application: the code you write with Result, Validation, Error and the analyzer attributes, how to use them well, and what each one catches that strings or exceptions would not."
status: draft
tags: [resultful, guide, errors]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Errors in practice

> **Proposed API.** This shows how an application would use [the error shape](error-shape.md) (decisions
> [14](decisions/0014-result-and-validation.md) and [15](decisions/0015-analyzer-rules-read-attributes.md), both
> proposed, and [16](decisions/0016-source-generated-result-shapes.md), accepted). `Match`, `Bind`, `MapError`
> and `TeeError` exist today; `Validation`, `Error`, `TryFind`, the attributes, the generated shapes and the
> `Resultful.AspNetCore` calls don't yet. Behaviour marked **verified** was compiled on
> SDK 11.0.100; the rest is how it's designed to work.

Each use case has the same parts: the situation, the code, and **what it catches**: something the compiler, a
handler or a test now sees that strings or exceptions would hide.

The examples share one small domain: an online shop with orders, payments and customer sign-up.

---

## 1. An endpoint that returns the right status

**Situation.** `GET /orders/{id}` should return 200, 404 for an unknown order, or 409 for a locked one. Anything
unexpected is a 500.

```csharp
[ClosedHierarchy]
public abstract partial record OrderError : Error;   // Resultful.Generators adds a private protected constructor
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
public sealed record OrderLocked(int Id, string By) : OrderError($"order {Id} is locked by {By}");

public Result<Order, OrderError> Get(int id)
{
    var order = _store.Find(id);
    if (order is null) return new OrderNotFound(id);
    if (order.LockedBy is { } who) return new OrderLocked(id, who);
    return order;
}

app.MapGet("/orders/{id}", (int id, Orders orders) =>
    orders.Get(id).Match(
        order => Results.Ok(order),
        error => error switch
        {
            OrderNotFound => Results.NotFound(),
            OrderLocked l => Results.Conflict(new { l.By }),
        }));
```

**What it catches.** Someone adds `OrderCancelled` to the domain. The endpoint now warns:

```text
warning CS8509: The switch expression does not handle all possible values of its input type
```

The warning goes away only when `OrderCancelled` has an arm (**verified**). With an `_` arm, a string or an
enum, the new case would quietly become a 500.

**Using it well.** The signature says `Result<Order, OrderError>`, not `Result<Order>`. A caller can see the
exact set of failures, and the `switch` needs no `_`.

**With a generated shape.** When callers branch on every outcome, name the result after the operation
([decision 16](decisions/0016-source-generated-result-shapes.md)). On .NET 11 the success and every failure sit in
one flat `switch`, checked by the compiler:

```csharp
[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct GetOrderResult;

public GetOrderResult Get(int id) { /* the same body: Order and each leaf convert implicitly */ }

app.MapGet("/orders/{id}", (int id, Orders orders) => orders.Get(id) switch
{
    Order order => Results.Ok(order),
    OrderNotFound => Results.NotFound(),
    OrderLocked l => Results.Conflict(new { l.By }),
});
```

Removing an arm gives CS8509 from the compiler itself (**verified** with a hand-written shape). On `net10.0` and
`netstandard2.0`, the generated `Match` does the same job.

---

## 2. A sign-up form that reports every mistake at once

**Situation.** A sign-up has a name, an email and a password. A user who gets two of them wrong should hear about
both in one response, not one at a time.

```csharp
[ClosedHierarchy]
public abstract record InputError : Error
{
    private protected InputError(string field, string message) : base(message) => Field = field;
    public string Field { get; }
}
public sealed record Required(string Field) : InputError(Field, $"{Field} is required");
public sealed record TooShort(string Field, int Min) : InputError(Field, $"{Field} needs at least {Min} characters");
public sealed record NotAnEmail(string Field) : InputError(Field, $"{Field} is not an email address");

Validation<string, InputError> Name(string? v) =>
    string.IsNullOrWhiteSpace(v) ? new Required("name") : v;

Validation<Email, InputError> EmailOf(string? v) =>
    string.IsNullOrWhiteSpace(v) ? new Required("email")
    : Email.TryParse(v, out var e) ? e
    : new NotAnEmail("email");

Validation<string, InputError> Password(string? v) =>
    v is null ? new Required("password")
    : v.Length < 12 ? new TooShort("password", 12)
    : v;

Validation<SignUp, InputError> Validate(SignUpRequest r) =>
    Validation.Combine(Name(r.Name), EmailOf(r.Email), Password(r.Password),
        (name, email, password) => new SignUp(name, email, password));
```

Each leaf's positional `Field` reuses the base's `Field` property rather than hiding it (**verified**: no CS0108).
With `using Resultful.Operators;`, `a & b` combines two checks; `Validation.Combine(a, b)` gives
the same tuple. `Combine` names the values and needs no nested tuples.

At the endpoint:

```csharp
app.MapPost("/sign-up", (SignUpRequest r) =>
    Validate(r).Match(
        signUp => Results.Ok(),
        errors => Results.ValidationProblem(
            errors.GroupBy(e => e.Field)
                  .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()))));
```

A request with no name and a bad email gets one response:

```json
{ "errors": { "name": ["name is required"], "email": ["email is not an email address"] } }
```

**What it catches.**
- The signature says it gathers: `Validation<…>` means "you'll get every failure". A `Result` would stop at the
  first, and the type tells you which one you have.
- A failed `Validation` is never empty, so `errors` always has at least one entry. There's no "failed with
  nothing wrong" to guard against.
- Each error knows its `Field` as a typed property. No parsing the field out of a message.

**With a generated shape.** `[ResultShape(typeof(InputError), Success = typeof(SignUp), ManyErrors = true)]` names
the result `SignUpResult`. Its `switch` has two arms, `SignUp` and `IReadOnlyList<InputError>`, because one
failure holds every mistake.

---

## 3. Validate first, then act

**Situation.** After the form is valid, the email must not already be registered, and only then is the account
created. Those steps depend on each other, so the first failure should stop them.

```csharp
public sealed record EmailTaken(Email Email) : Error($"{Email} is already registered");

Result<Account> Register(SignUp s) =>
    _accounts.Exists(s.Email)
        ? new EmailTaken(s.Email)
        : _accounts.Create(s);

app.MapPost("/sign-up", (SignUpRequest r) =>
    Validate(r).Match(
        signUp => Register(signUp).Match(
            account => Results.Created($"/accounts/{account.Id}", account),
            error => error.TryFind<EmailTaken>(out _) ? Results.Conflict() : Results.Problem()),
        errors => Results.ValidationProblem(ToDictionary(errors))));
```

**What it catches.** `Validation` has no `Bind`. You can't write "check the database" inside the validation
step by mistake, because the type won't let you chain a dependent step there. Gathering and stopping are two
types, and moving from one to the other is visible in the code.

**Using it well.** Keep validation pure (no database, no network) and put it first. Everything that depends on a
valid input goes in a `Result` after it.

---

## 4. A database failure, explained on the way up

**Situation.** Loading an order times out in the data layer. The log should say what the application was doing,
and the retry policy at the edge needs to know it was a timeout.

```csharp
public sealed record DbTimeout(TimeSpan After) : Error($"the database timed out after {After.TotalSeconds}s");

// data layer
Result<OrderRow> Find(int id) =>
    _db.TryQuery(id, out var row, out var elapsed) ? row : new DbTimeout(elapsed);

// service layer
Result<Order> LoadForInvoice(int id) =>
    Find(id)
        .Map(ToOrder)
        .MapError(e => e.WithContext($"loading order {id} for invoicing"));

// edge: a background invoicing job
var result = LoadForInvoice(id);
if (result.TryGetError(out var error))
{
    _log.LogWarning("{Error} ({Context})", error.Message, string.Join(" → ", error.Context));
    return error is DbTimeout timeout ? Retry.After(timeout.After * 2) : Retry.Never;
}
```

The log line:

```text
the database timed out after 30s (loading order 42 for invoicing)
```

**What it catches.** Context doesn't change what the error is. `WithContext` adds a note and keeps the type, so
the timeout is still a `DbTimeout` (**verified**), and the service layer can add context without breaking the
retry policy. With strings, the retry policy would have to look for "timed out" in a sentence someone else
wrote.

**Using it well.** Add notes with `WithContext`. When a layer turns a failure into a different one, wrap it
instead, `new InvoiceFailed(id) { Cause = error }`, and the edge finds the cause with
`error.TryFind<DbTimeout>(out var timeout)`, which walks the `Cause` chain.

---

## 5. A payment provider, translated at the adapter

**Situation.** The payment provider returns its own error codes. The rest of the application shouldn't know
them. Some failures are worth retrying and some aren't.

```csharp
[ClosedHierarchy]
public abstract partial record PaymentError : Error;
public sealed record CardDeclined(string Reason) : PaymentError($"card declined: {Reason}");
public sealed record ProviderUnavailable() : PaymentError("the payment provider is unavailable");
public sealed record FraudSuspected() : PaymentError("payment blocked");

// the adapter: the only code that knows the provider's codes
Result<PaymentId, PaymentError> Charge(Order o)
{
    var response = _provider.Charge(o.Total, o.Card);
    return response.Code switch
    {
        "ok" => new PaymentId(response.Id),
        "card_declined" => new CardDeclined(response.Message),
        "fraud" => new FraudSuspected(),
        _ => new ProviderUnavailable(),
    };
}

// the checkout: decides what to do, and must decide for every case
CheckoutStep Next(PaymentError error) => error switch
{
    CardDeclined => CheckoutStep.AskForAnotherCard,
    ProviderUnavailable => CheckoutStep.RetryLater,
    FraudSuspected => CheckoutStep.ContactSupport,
};
```

**What it catches.** When the provider adds a code and the adapter maps it to a new `PaymentError`, `Next` stops
being exhaustive and warns until checkout decides what to do. The decision can't be forgotten, and the
provider's strings never leave the adapter.

---

## 6. A message handler that acks, retries or dead-letters

**Situation.** A queue consumer must choose, for every failure, whether to retry the message or send it to the
dead-letter queue. Getting it wrong either loses orders or retries forever.

```csharp
public async Task<Disposition> Handle(OrderPlaced message) =>
    (await _fulfilment.Start(message.OrderId)).Match(
        _ => Disposition.Ack,
        error => error switch                       // FulfilmentError is [ClosedHierarchy]
        {
            StockUnavailable => Disposition.Retry(TimeSpan.FromMinutes(5)),
            WarehouseOffline => Disposition.Retry(TimeSpan.FromSeconds(30)),
            OrderNotFound => Disposition.DeadLetter("unknown order"),
            AddressInvalid => Disposition.DeadLetter("bad address"),
        });
```

**What it catches.** The same as use case 1, where it matters more. A new failure mode can't fall into a default
retry or a default dead-letter without someone choosing. The `switch` is the policy, and the compiler checks it's
complete.

---

## 7. A result nobody looked at

**Situation.** A call returns a result and the caller forgets to check it. With exceptions, the failure would at
least be thrown. With a result, it disappears.

```csharp
public void CancelAbandonedBaskets()
{
    foreach (var basket in _baskets.Abandoned())
        _orders.Cancel(basket.OrderId);             // returns Result<Unit, OrderError>
}
```

```text
warning RF0001: The Result<Unit, OrderError> returned by Cancel is never observed
```

The fix is a decision about what a failure means here:

```csharp
_orders.Cancel(basket.OrderId)
       .TeeError(e => _log.LogWarning("could not cancel {Id}: {Error}", basket.OrderId, e.Message));
```

**What it catches.** `Result`, `Validation` and `Option` are `[MustObserve]`, so a thrown-away result is a
warning. The rule number is illustrative: the rules are planned for `Resultful.Analyzers` (roadmap M4).

---

## 8. Tests that survive a reworded message

**Situation.** A test checks that locking works. Later, someone improves the message wording.

```csharp
[Test]
public void A_locked_order_cannot_be_changed()
{
    var result = _orders.Rename(42, "new name");

    Assert.That(result.TryGetError(out var error) && error is OrderLocked { Id: 42, By: "alice" });
}
```

**What it catches.** The test checks the type and the data, not the text. Rewording `OrderLocked`'s message
doesn't break it; changing what the error *means* does. With string errors, every message change touches the
tests, so people stop improving messages.

---

## 9. A library with its own result type

**Situation.** A team ships an HTTP client library with its own `ApiResponse<T>`. They don't want a dependency
on Resultful, but they want callers warned when a response is ignored.

```csharp
// inside the library: its own copy, matched by name
namespace Resultful.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Method)]
    internal sealed class MustObserveAttribute : Attribute;
}

[MustObserve]
public readonly struct ApiResponse<T> { /* … */ }
```

In an application that uses both the library and `Resultful.Analyzers`:

```csharp
client.Post(order);     // warning: the ApiResponse<Receipt> returned by Post is never observed
```

**What it catches.** The library gets Resultful's checks without referencing it. An `internal` copy of
`[ClosedHierarchy]` in a consuming project drove the exhaustiveness check in the probe (**verified**);
`[MustObserve]` works the same way by design.

---

## 10. Moving from string errors

**Situation.** An application written against today's `Result<T>` fails with strings.

Before:

```csharp
Result<Order> Get(int id) =>
    _store.Find(id) is { } o ? o : $"order {id} was not found";

// the caller
result.Match(
    o => Results.Ok(o),
    errors => errors.First().Contains("not found") ? Results.NotFound() : Results.Problem());
```

After:

```csharp
Result<Order, OrderError> Get(int id) =>
    _store.Find(id) is { } o ? o : new OrderNotFound(id);

result.Match(
    o => Results.Ok(o),
    error => error switch { OrderNotFound => Results.NotFound(), OrderLocked => Results.Conflict() });
```

**What changes, and what catches it.**
- `Result<int> F() => "bad";` no longer compiles (CS0029, **verified**). Each place that returned a bare string
  shows up as an error, and becomes a typed error or `Error.From("bad")`.
- `Result<string> F() => "bad";` still compiles, but now means **success**. That's the dangerous case, and the
  analyzer rule for a bare string is planned to ship in the same release to flag every one.
- Code that gathered errors with `Plus` moves to `Validation` and `&` (use case 2).

**Using it well.** Migrate one area at a time. Start with the errors a caller makes decisions on (not found,
conflict, retryable), and leave a message-only error where nobody branches on it.

---

## What the use cases show

| Benefit | Where |
|---|---|
| A new failure can't be silently mishandled; the compiler points at every decision | 1, 5, 6 |
| An operation's result is one named type, with success and every failure in one `switch` | 1, 2 |
| The signature says whether failures stop or gather, and which ones to expect | 1, 2, 3 |
| Users see every input mistake at once, and the list is never empty | 2 |
| Dependent steps can't hide inside validation | 3 |
| Context explains the failure without hiding its cause from code | 4 |
| Third-party error codes stay in the adapter | 5 |
| An ignored result is a warning, not a lost failure | 7, 9 |
| Tests check meaning, not wording | 8 |
| The migration's hazards surface as compile errors or analyzer warnings | 10 |

## Habits

- **Type the signature narrowly.** `Result<Order, OrderError>` where callers branch on the error; `Result<T>`
  where they only report it.
- **Close a base when callers make decisions on it**, and leave it open when errors are only reported.
- **Validate before you act.** Pure checks in `Validation`; anything that needs the outcome of a check in
  `Result`.
- **Name the result of an operation callers branch on** with `[ResultShape]`; use the core `Result` for the
  steps in between.
- **Add context at layer boundaries.** It keeps the type. Wrap with `Cause` only when the error becomes a
  different one, and search with `TryFind` at the edge.
- **Translate other people's errors at the adapter**, into your own closed set.
- **Keep messages for people and properties for code.**
- **Throw for bugs.** A null where there can't be one, or a broken invariant, is an exception, not an error
  value.
