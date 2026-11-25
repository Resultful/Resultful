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
> (analyzer rules that read attributes), and shows where [decision 16](decisions/0016-source-generated-result-shapes.md)
> (generated result shapes, **accepted**) fits. The reasoning is in [One error or many](explorations/one-error-or-many.md)
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
| How does a lower layer's error travel up? | Lost, or a list of reasons | **Noted** with `WithContext`, keeping its type; or **wrapped** with `Cause` and found with `TryFind` |
| Is a `switch` over errors exhaustive? | No | **When you say so**, with `[ClosedHierarchy]` |
| How does HTTP see it? | A category enum built into the core | **A satellite maps your types**; the core knows nothing about HTTP |
| What does a method return? | The library's result type | The core `Result`, or **a struct named after the operation** that switches flat over success and every error |

The ideas aren't new. They come from F# and Haskell (the count in the type), Rust and Swift (your types are
the errors), and Go and Rust again (wrap, then search). What's new is putting them together in a C# library and
letting the analyzers check your types as strictly as Resultful's own.

## The pieces

```csharp
Result<T, TError>          // a T, or one TError; Bind stops at the first failure
Validation<T, TError>      // a T, or a non-empty list of TError; & gathers every failure
Result<T>                  // means Result<T, Error>, but a separate type; AsResult() widens to it
Validation<T>              // means Validation<T, Error>
Result<Unit, TError>       // a step with no value; return Unit.Value

public abstract record Error
{
    protected Error(string message);
    public string Message { get; }
    public Error? Cause { get; init; }                 // the lower error this one wraps
    public IReadOnlyList<string> Context { get; init; } // notes added on the way up, copied when set
    public static Error From(string message);          // a plain MessageError
    public virtual IEnumerable<Error> Inner();         // errors held inside, such as ValidationFailed's list
    public IEnumerable<Error> Causes();                 // this, then its Cause's tree, then each Inner error's
    public bool TryFind<TError>(out TError? found) where TError : Error;
    public sealed override string ToString();           // type, Message, Context, causes; never the record's own data
}
public static TError WithContext<TError>(this TError error, string message) where TError : Error; // keeps the type
public sealed record ValidationFailed<TError>(IReadOnlyList<TError> Errors) : Error; // a Validation inside a Result

// Resultful.CodeAnalysis: read by Resultful.Analyzers and Resultful.Generators, matched by name
[MustObserve]                                  // a value of this type may not be thrown away
[ClosedHierarchy]                              // a switch over this base is exhaustive when every derived type is matched
[ResultShape(typeof(TError), Success = typeof(T))] // generate a narrow result struct (decision 16)
```

There's no implicit conversion from `string`: C# applies one user-defined conversion at a time, so it never let
`return "bad";` compile against a `Result`. Write `Error.From("bad")`, or use your own record.

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
[ClosedHierarchy]
public abstract record OrderError : Error
{
    private protected OrderError(string message) : base(message) { }
}
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} not found");
public sealed record OrderLocked(int Id) : OrderError($"order {Id} is locked");

int Status(OrderError e) => e switch { OrderNotFound => 404, OrderLocked => 409 };   // no `_`
```

The `private protected` constructor stops other assemblies deriving the ordinary way; a positional
`abstract record OrderError(string Message)` gets a `protected` one, which they can call (**verified**). It
doesn't close the set alone: a record's copy constructor must stay `protected`, and another assembly can derive
through it (**verified**). The analyzer reports a case added outside the base's assembly. Declare the base
`partial` and reference `Resultful.Generators`, and the constructor is written for you
([decision 16](decisions/0016-source-generated-result-shapes.md)):

```csharp
[ClosedHierarchy] public abstract partial record OrderError : Error;
```

Add `OrderCancelled` and every `switch` over `OrderError` warns again (CS8509) until it handles it. **Verified**:
the warning is suppressed only when every type is matched. A missing type, a `when` guard, or a base without the
attribute keeps it.

Signatures say which set to expect: `Result<Order, OrderError>`.

### 3. Groups of closed sets

Closed bases nest. Match a group as a whole, or every case inside it.

```csharp
[ClosedHierarchy] public abstract partial record AppError : Error;
[ClosedHierarchy] public abstract partial record OrderFault : AppError;
public sealed record OrderMissing() : OrderFault("missing");
public sealed record OrderLocked() : OrderFault("locked");
public sealed record PaymentDeclined() : AppError("declined");

e switch { OrderFault => 404, PaymentDeclined => 402 }                             // exhaustive
e switch { OrderMissing => 404, OrderLocked => 409, PaymentDeclined => 402 }       // exhaustive
```

**Verified** for both forms, and a gap still warns.

### 4. Layers with context

Two ways to say more on the way up.

**A note.** `WithContext` adds a line to the error's `Context` and keeps its type, so a closed `switch` still
matches it (**verified**: a `NotFound` with a note is still a `NotFound`, and still an `OrderError`).

```csharp
Result<Order, OrderError> Load(int id) =>
    repo.Find(id).MapError(e => e.WithContext($"loading order {id}"));

// at the edge: the switch is unchanged; the notes go to the log
```

**A wrapper.** When a lower layer's error should become a different one, make it the `Cause` of yours. It isn't
lost, and a handler can still find it.

```csharp
public sealed record DbTimeout(TimeSpan After) : Error($"database timed out after {After}");
public sealed record SaveFailed(int OrderId) : Error($"order {OrderId} was not saved");

Result<Order> Save(Order o) =>                        // db.Save returns Result<Order>
    db.Save(o).MapError(e => new SaveFailed(o.Id) { Cause = e });

// at the edge
if (error.TryFind<DbTimeout>(out var timeout)) return Retry(timeout.After);
```

On a `Result<T>`, `MapError` takes a function from `Error` to `Error`, so the wrapper keeps it a `Result<Order>`
(stack PR 09). From a `Result<T, TError>`, the same lambda keeps the wrapper's type (`Result<Order, SaveFailed>`);
call `.AsResult()` to widen it. A `switch` sees the wrapper, not its cause. Handlers that may see wrapped errors use `TryFind`, which walks the
error, then its `Cause` chain and any `Inner` errors.

### 5. Validation

Independent checks that should all run, with every failure reported.

```csharp
[ClosedHierarchy]
public abstract record InputError : Error
{
    private protected InputError(string field, string message) : base(message) => Field = field;
    public string Field { get; }
}
public sealed record Required(string Field) : InputError(Field, $"{Field} is required");
public sealed record TooLong(string Field, int Max) : InputError(Field, $"{Field} is longer than {Max}");

Validation<Registration, InputError> Validate(Request r) =>
    Validation.Combine(Name(r.Name), Email(r.Email), Age(r.Age),
        (name, email, age) => new Registration(name, email, age));
```

The result type says it gathers, and the closed set says what it gathers. `&` works for two; `Combine` takes up
to eight with no nested tuples. To carry on in a `Result` chain, `ToResult()` turns the list into one
`ValidationFailed<InputError>`, which `TryFind` can search. A satellite turns either into a validation problem
with one entry per field.

### 6. Mapping at a boundary

The core knows nothing about HTTP, gRPC or messaging. A satellite reads your types, or its own attribute on them.

```csharp
// Resultful.AspNetCore (design): a table you extend
errors.Map<OrderNotFound>(StatusCodes.Status404NotFound)
      .Map<OrderLocked>(StatusCodes.Status409Conflict);

// or, if that package defines it, its own attribute on your type
[HttpStatus(404)] public sealed record OrderNotFound(int Id) : OrderError($"order {Id} not found");
```

The mapping walks the error outer-to-inner (the error, then its `Cause` chain and `Inner` errors) and uses the
first type it knows. So a typed wrapper like `SaveFailed` picks its own status before its cause's, and a note
from `WithContext` changes nothing, because the type is the same.

The table keeps HTTP out of your domain. The attribute is shorter but puts an HTTP fact on a domain type; use it
when the type already lives at the edge.

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

### 8. A result named after the operation

The core experience [decision 16](decisions/0016-source-generated-result-shapes.md) chose. Declare a struct for
an operation, and `Resultful.Generators` writes it. On .NET 11 it's a native union, so the `switch` is flat and
checked by the compiler with no `_` arm.

```csharp
[ResultShape(typeof(OrderError), Success = typeof(Order))]
public readonly partial struct PlaceOrderResult;

PlaceOrderResult Place(Cart cart) =>
    Load(cart.OrderId).Bind(Reserve);          // Result<Order, OrderError> converts into the shape

var status = Place(cart) switch
{
    Order o => Created(o),
    OrderNotFound nf => NotFound(nf.Id),
    OrderLocked => Conflict(),
    CardDeclined => PaymentRequired(),
};
```

**Verified** on SDK 11.0.100: the switch is exhaustive, removing an arm gives CS8509, and the conversion from
`Result<Order, OrderError>` compiles next to the union conversions. The shape is narrow, holding one success type,
so the generator reports a success type that overlaps an error case where it's declared. It widens implicitly to
`Result<Order, OrderError>` and `Result<Order>` at a layer boundary. On `net10.0` and `netstandard2.0` the same
declaration gives a `Match` method instead of the native switch.

Use it for the operations a caller branches on. The steps between use the core `Result`.

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
| a caller branches on every outcome of an operation | a generated result shape (shape 8) |

## Rules of thumb

- **Name errors after what happened**, not after a status code: `OrderLocked`, not `Conflict409`.
- **Put data in properties**, not in the message. The message is for people; the properties are for code.
- **Seal the leaves.** Derived error records are `sealed`; only the bases you mean to extend are `abstract`.
- **Close a base only when its set is really fixed.** Closing it means a new case breaks every `switch` over it
  until it's handled. That's the point, so use it where you want that.
- **Note, or wrap.** `WithContext` adds a note and keeps the type. Wrap with `Cause` only when the error becomes a
  different one.
- **Search, don't match, at the edge.** Inside a layer, `switch` on what that layer produces. At a boundary that
  sees wrapped errors, use `TryFind`.
- **Exceptions stay exceptions.** A bug, a broken invariant or a lost connection the code can't recover from is
  thrown, not returned ([vision](vision.md)).

## Still open

The open questions live in one place for each decision:
[decision 14](decisions/0014-result-and-validation.md#still-open) (the name `Validation`, `Bind` on it) and
[decision 15](decisions/0015-analyzer-rules-read-attributes.md) (IDE support, `switch` statements and nested
patterns).
