---
type: design
title: "The error shape"
description: "How Resultful models failure: the count in the type (Result or Validation), categories as your own records, context that wraps without hiding, hierarchies you close when you choose, and boundaries that map by type. With the shapes a consuming application builds from it."
status: draft
tags: [resultful, design, errors, analyzers]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# The error shape

> **Proposed**, not accepted. This brings together [decision 14](decisions/0014-result-and-validation.md)
> (`Result`, `Validation` and `Error`) and [decision 15](decisions/0015-analyzer-rules-read-attributes.md)
> (analyzer rules that read attributes). The reasoning is in [One error or many](explorations/one-error-or-many.md)
> and [Analyzers for your own types](explorations/analyzers-for-your-own-types.md); the libraries and languages it
> learned from are in [Error handling: prior art](references/error-handling-prior-art.md). Claims marked
> **verified** were compiled on SDK 11.0.100. Everything else is design. For the code an application writes, use
> case by use case, see [Errors in practice](errors-in-practice.md).

## How it differs

Most .NET result libraries decide what an error is for you: a record with a code and a category enum (ErrorOr),
a class with a metadata dictionary (FluentResults), or a status on the result (Ardalis.Result). Resultful gives
you a small base and a set of rules, and **your types are the model**.

| Question | Typical .NET library | Resultful |
|---|---|---|
| One error or many? | Always a list, or always one | **In the type**: `Result` holds one and stops; `Validation` holds a non-empty list and gathers |
| What is a category? | An enum or a string code | **Your record type** |
| Where does extra data go? | A `string → object` dictionary | **Typed properties** on your record |
| How does a lower layer's error travel up? | Lost, or a list of reasons | **Wrapped** with `WithContext`, still findable with `TryFind` |
| Is a `switch` over errors exhaustive? | No | **When you say so**, with `[ClosedHierarchy]` |
| How does HTTP see it? | A category enum built into the core | **A satellite maps your types**; the core knows nothing about HTTP |

The ideas aren't new. They come from F# and Haskell (the count in the type), Rust and Swift (your types are
the errors), and Go and Rust again (wrap, then search). What's new is putting them together in a C# library and
letting the analyzers check your types as strictly as Resultful's own.

## The pieces

```csharp
Result<T, TError>          // a T, or one TError; Bind stops at the first failure
Validation<T, TError>      // a T, or a non-empty list of TError; & gathers every failure
Result<T>     = Result<T, Error>
Validation<T> = Validation<T, Error>

public abstract record Error
{
    protected Error(string message);
    public string Message { get; }
    public Error? Cause { get; init; }
    public Error WithContext(string message);       // wraps this as the Cause of a new error
    public IEnumerable<Error> Causes();              // this, then each Cause
    public bool TryFind<TError>(out TError? found) where TError : Error;
}

// Resultful.CodeAnalysis: read by Resultful.Analyzers, matched by name
[MustObserve]        // a value of this type may not be thrown away
[ClosedHierarchy]    // a switch over this base is exhaustive when every derived type is matched
```

## The shapes

Each shape is a choice a consuming application makes. They combine.

### 1. Open errors

The starting point. Derive records from `Error`, and handle them with a `switch` that ends in `_`.

```csharp
public sealed record NotFound(string Entity, int Id) : Error($"{Entity} {Id} was not found");
public sealed record Conflict(string Reason) : Error(Reason);

string Describe(Error e) => e switch
{
    NotFound n => $"missing {n.Entity}",
    Conflict c => c.Reason,
    _ => e.Message,
};
```

Use it when errors are shared across the application and new ones appear often.

### 2. A closed error set per area

Close a base when an area has a fixed set of failures and you want the compiler to tell you about a new one.

```csharp
[ClosedHierarchy] public abstract record OrderError(string Message) : Error(Message);
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} not found");
public sealed record OrderLocked(int Id) : OrderError($"order {Id} is locked");

int Status(OrderError e) => e switch { OrderNotFound => 404, OrderLocked => 409 };   // no `_`
```

Add `OrderCancelled` and every `switch` over `OrderError` warns again (CS8509) until it handles it. **Verified**:
the warning is suppressed only when every type is matched. A missing type, a `when` guard, or a base without the
attribute keeps it.

Signatures say which set to expect: `Result<Order, OrderError>`.

### 3. Groups of closed sets

Closed bases nest. Match a group as a whole, or every case inside it.

```csharp
[ClosedHierarchy] public abstract record AppError(string Message) : Error(Message);
[ClosedHierarchy] public abstract record OrderFault(string Message) : AppError(Message);
public sealed record OrderMissing() : OrderFault("missing");
public sealed record OrderLocked() : OrderFault("locked");
public sealed record PaymentDeclined() : AppError("declined");

e switch { OrderFault => 404, PaymentDeclined => 402 }                             // exhaustive
e switch { OrderMissing => 404, OrderLocked => 409, PaymentDeclined => 402 }       // exhaustive
```

**Verified** for both forms, and a gap still warns.

### 4. Layers with context

A lower layer's error becomes the cause of a higher one. It isn't lost, and a handler can still find it.

```csharp
public sealed record DbTimeout(TimeSpan After) : Error($"database timed out after {After}");

Result<Order> Load(int id) =>
    db.Find(id)                                         // Result<Order>, may fail with DbTimeout
      .MapError(e => e.WithContext($"loading order {id}"));

// at the edge
if (error.TryFind<DbTimeout>(out var timeout)) return Retry(timeout.After);
```

A `switch` on the top value sees only the context message once an error is wrapped (**verified**). Handlers that
may see wrapped errors use `TryFind`. A typed wrapper works too: `new SaveFailed(orderId) { Cause = dbError }`.

### 5. Validation

Independent checks that should all run, with every failure reported.

```csharp
[ClosedHierarchy] public abstract record InputError(string Field, string Message) : Error(Message);
public sealed record Required(string Field) : InputError(Field, $"{Field} is required");
public sealed record TooLong(string Field, int Max) : InputError(Field, $"{Field} is longer than {Max}");

Validation<Registration, InputError> Validate(Request r) =>
    (Name(r.Name) & Email(r.Email))
        .Map(t => new Registration(t.Item1, t.Item2));
```

The result type says it gathers, and the closed set says what it gathers. A satellite turns a failed
`Validation` into a validation problem with one entry per field.

### 6. Mapping at a boundary

The core knows nothing about HTTP, gRPC or messaging. A satellite reads your types, or its own attribute on them.

```csharp
// Resultful.AspNetCore (design): a table you extend
errors.Map<OrderNotFound>(StatusCodes.Status404NotFound)
      .Map<OrderLocked>(StatusCodes.Status409Conflict);

// or, if that package defines it, its own attribute on your type
[HttpStatus(404)] public sealed record OrderNotFound(int Id) : OrderError($"order {Id} not found");
```

The mapping uses `TryFind`, so it still works after context has been added.

### 7. Your own result type

The analyzer rules aren't tied to Resultful's types. A library with its own result type, and no reference to
Resultful, declares the attribute itself and gets the same checks.

```csharp
namespace Resultful.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Method)]
    internal sealed class MustObserveAttribute : Attribute;
}

[MustObserve] public readonly struct ApiResponse<T> { /* … */ }

client.Send(request);   // the analyzer reports the ignored ApiResponse
```

**Verified** for `[ClosedHierarchy]`: an `internal` copy in the consuming project drove the suppression. Types you
can't edit are listed in `.editorconfig`.

## Choosing a shape

| If… | Use |
|---|---|
| a failure stops the work | `Result` |
| every failure should be reported | `Validation` |
| the set of failures is open or shared | open records (shape 1) |
| an area has a fixed set and you want to hear about new ones | `[ClosedHierarchy]` (shapes 2 and 3) |
| a lower layer failed and the caller needs to know why | `WithContext` or a typed wrapper with `Cause`, and `TryFind` at the edge (shape 4) |
| failures meet HTTP or another protocol | a satellite's map or attribute (shape 6) |
| you already have your own result type | the attributes on it (shape 7) |

## Rules of thumb

- **Name errors after what happened**, not after a status code: `OrderLocked`, not `Conflict409`.
- **Put data in properties**, not in the message. The message is for people; the properties are for code.
- **Seal the leaves.** Derived error records are `sealed`; only the bases you mean to extend are `abstract`.
- **Close a base only when its set is really fixed.** Closing it means a new case breaks every `switch` over it
  until it's handled. That's the point, so use it where you want that.
- **Search, don't match, at the edge.** Inside a layer, `switch` on what that layer produces. At a boundary that
  sees wrapped errors, use `TryFind`.
- **Exceptions stay exceptions.** A bug, a broken invariant or a lost connection the code can't recover from is
  thrown, not returned ([vision](vision.md)).

## Still open

From [decision 14](decisions/0014-result-and-validation.md):
- whether `Error` keeps an implicit conversion from `string`, or uses `Error.From` and `Fail<T>(string)` overloads
  only
- the name `Validation`, whether it is a new type, and whether it has `Bind`
- how many errors become one inside a `Result` chain
- whether the base seals `ToString` so typed data stays out of logs

From [decision 15](decisions/0015-analyzer-rules-read-attributes.md):
- closed hierarchies across assemblies, IDE support, `switch` statements and nested patterns
