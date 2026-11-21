# Resultful

Utility classes for Railway Oriented Programming. The core package depends on nothing; `Resultful.OneOf` converts to and from OneOf unions.

Inspired by this article on [F# for fun and profit](https://fsharpforfunandprofit.com/posts/recipe-part2/)
Designed to be consumed for C#.

## Installation

Instructions to install using your appropriate package manager can be [found here](https://www.nuget.org/packages/Resultful/)

## Types

There are 5 fundamental types in this library

1. `Result<T, TError>` - Union of `T` and `TError`
2. `Result<T>` - Union of `T` and one `Error`
3. `Validation<T, TError>` - Union of `T` and a non-empty list of `TError`
4. `Validation<T>` - Union of `T` and a non-empty list of `Error`
5. `Option<T>` - Union of `T` and `None`

A step with no value is a `Result<Unit>` or `Result<Unit, TError>`: `Result.Ok()` and `Result.Ok<TError>()` return
one, and `return Unit.Value;` converts to one. `VoidResult` and `VoidResult<TError>` are obsolete since 0.3 (a CS0618
warning); they convert to and from `Result<Unit>` implicitly and are removed at 1.0.

## Usage

There are 3 parts to building a pipeline using this library.
I am going to write the majority of the examples against `Result<T, TError>` as it is the most generic type, but the idea is applicable to all types where.

1. `Result<T>` = `Result<T, Error>`
2. `Option<T>` = `Result<T, None>`

### Building

There are implicit converters to each of the result types from their Union element.

```cs
private Result<int, Error> ParseInt(string input)
{
    if (input?.All(char.IsDigit) ?? false)
    {
        return int.Parse(input);
    }
    return new Error($"{input} contains a character which is not a digit");
}
```

`Result<T>` converts from a `T` and from any `Error`, so `return row;` and `return new OrderNotFound(id);` both work.
Nothing converts a `string` to an error: `Result<string> r = "message"` is a success. Write
`Result.Fail<T>("message")` or `Error.From("message")` for a failure with only a message.

### Pipelining

There are three main pipelining operators which can be performed on a result type, going to write these examples against the

1. Map
2. Bind
3. Tee

```cs
private Result<int, Error> NumberIsGreaterThanZero(string input)
{
    if (input == null)
    {
        return new Error("Cannot return int value given is null");
    }
    return ParseInt(input).Bind<int>(x =>
    {
        if (x <= 0)
        {
            return new Error($"{x} must be a value greater than or equal to 0");
        }
        return x;
    });
}
```

### Consuming

When the value needs to be consumed by something which cannot be pipelined typically being the boundaries of the application such as the controller on a WebAPI.
There is a method called Match which is the core API in this library.

This method typically has the signature
`TResult Match<TResult>(Func<T, TResult> successFunc, Func<TError, TResult> errorFunc)` where `T` and `TError` are the cases of the `Result`.

It could be used as follows

```cs
private IActionResult Get(string input)
    => NumberIsGreaterThanZero(input)
        .Match<IActionResult>(
            success => Ok(success),
            error => BadRequest(error));
```

This way we can abstract the HTTP result from from domain logic.

### Named methods

When a callback is more than you need, ask the result directly. These read like the rest of .NET:

```cs
Result<Order, OrderError> result = FindOrder(id);

if (result.IsSuccess) Ship(result.Value);         // IsSuccess and IsFailure tell the nullable checker which side is set
if (result.TryGetError(out var error)) Log(error); // like Dictionary.TryGetValue; TryGetValue reads the value
var (ok, order, failure) = result;                 // Deconstruct

var fromCache = cached.OrElse(result);             // the first success wins
var retried = result.OrElse(e => Retry(e));        // runs only on a failure, with the error
var count = CountItems().ValueOr(0);               // the value, or a fallback
```

`Value` throws `InvalidOperationException` on a failure and `Error` throws on a success. `Result<T>` has the same
members, with `Error` as the error type.

The zero value (`default`, an array slot, an unassigned field) is neither a success nor a failure. `IsSuccess` and
`IsFailure` are both `false`, and every member that reads the value or the error (`Match`, `Map`, `Bind`, the async
and LINQ extensions, …) throws `InvalidOperationException`. `ToString` (`"<default Result>"`), `Equals` and
`GetHashCode` never throw. `new Result<T, TError>()` doesn't compile. `Fail(null)` and a `null` error list throw `ArgumentNullException`;
`Ok(null)` is a success holding `null`. A default `Option<T>` is `None`.

### Early return

When a step has branches, loops, `try` or `using`, plain C# reads better than a pipeline. `Failed` reads the case
once, and the error converts to the method's result, so a failure goes straight back:

```cs
Result<Receipt, Error> Ship(int id)
{
    if (FindOrder(id).Failed(out var order, out var error))
        return error;                // error is not null here

    return new Receipt(order.Id);    // order is not null here: no `!`
}
```

Every result type has it: `Result<T>`, `Result<T, TError>`, generated shapes, and `Validation`, whose
`Failed(out value, out errors)` gives the list. An async step needs brackets:
`if ((await FindOrderAsync(id)).Failed(out var order, out var error)) return error;`. The zero value throws, as
`TryGetValue` does. `return error;` needs `T` and `TError` to differ (the two conversions are ambiguous otherwise,
CS0457) and `TError` not to be an interface (C# applies no user-defined conversion from one, CS0029): use
`error.Fail<T, TError>()` then. For a value-type `T`, `order` is `default` inside the branch, which the
compiler can't flag; Resultful.Analyzers' RF0011 does.

### Errors

`Error` is an abstract record for one failure. Derive a record for each kind of failure and keep its data in typed
properties:

```cs
public sealed record OrderNotFound(int Id) : Error($"order {Id} was not found");
public sealed record SaveFailed(int Id) : Error($"order {Id} was not saved");

var plain = Error.From("something went wrong");               // a MessageError
var noted = new OrderNotFound(3).WithContext("checkout");      // still an OrderNotFound, with one note in Context
var wrapped = new SaveFailed(3) { Cause = new OrderNotFound(3) };
if (wrapped.TryFind<OrderNotFound>(out var missing)) { }       // searches the error, its Cause chain and Inner errors
var many = new ValidationFailed<Error>([plain, noted]);       // several errors as one; Message is "Validation failed."
```

Two errors are equal when their type, `Message`, `Cause` and `Context` (element by element) are equal, as well as
the derived record's own properties. `ToString()` is sealed: it prints the type name, `Message`, `Context`, inner
errors and the cause chain, and never a derived record's own properties, because they may hold personal data. There
is no conversion from `string`; write `Error.From("…")`. Deriving from `Error` needs C# 10 or later: C# 9
reports CS8912, because the base seals `ToString`.

`Result<T>` holds one `Error` and stops at the first failure. `MapError` on it takes a function from `Error` to
`Error`, so a note or a wrapper keeps it a `Result<T>`; `Map2` changes the error to another type. A
`Result<T, TError>` whose error derives from `Error` widens with `AsResult()`:

```cs
Result<Order> Save(Order o) =>
    db.Save(o).MapError(e => new SaveFailed(o.Id) { Cause = e });   // db.Save returns Result<Order>

Result<Order> Load(int id) =>
    repo.Find(id).AsResult().WithContext($"loading order {id}");      // Find returns Result<Order, OrderNotFound>
```

### Validation

`Validation<T, TError>` gathers: checks that don't depend on each other all run, and every failure is kept, in order.
`Validation.Combine` takes two to eight and names the values (`a & b` combines two into a tuple with the opt-in
operators). A failed `Validation` is never empty. It has no `Bind` and no `+`: a step that needs the
value comes after the checks, in a `Result`.

```cs
Validation<string, InputError> Name(string? v) => string.IsNullOrWhiteSpace(v) ? new Required("name") : v;

Validation<SignUp, InputError> Validate(SignUpRequest r) =>
    Validation.Combine(Name(r.Name), Email(r.Email), Password(r.Password),
        (name, email, password) => new SignUp(name, email, password));

Result<Account> Handle(SignUpRequest r) =>
    Validate(r).ToResult()        // the errors travel as one ValidationFailed<InputError>, which TryFind searches
        .Bind(Register);          // stops at the first failure
```

`ToResult(combine)` turns the list into one error your way; a `Result` converts to a `Validation` implicitly (one
error is a list of one), `AsValidation()` widens `Validation<T, TError>` to `Validation<T>` when `TError` derives
from `Error`, and the obsolete `VoidResult` converts to `Result<Unit>`. With both directions, `b ? voidResult : resultUnit`
reports CS0172; cast one side. `Plus`, `Fold`, `Reduce` and `Unroll` on
`Result<T>` and `VoidResult` are gone: gathering is `Validation`'s job.

### Attributes for the analyzers and the generator

`Resultful.CodeAnalysis` holds the attributes that the analyzers and the source generator read. The core's own types
carry them, and you can put them on yours to get the same checks:

```cs
using Resultful.CodeAnalysis;

[MustObserve]                                   // a value of this type must not be thrown away
public readonly struct ApiResponse { /* ... */ }

[ClosedHierarchy]                               // every derived type is in this assembly
public abstract record OrderError : Error
{
    private protected OrderError(string message) : base(message) { }
}

[ResultShape(typeof(OrderError), Success = typeof(Order))]   // a named result, written by the generator
public readonly partial struct PlaceOrderResult;
```

`Result<T>`, `Result<T, TError>` and `Option<T>` carry `[MustObserve]`. None of the attributes is inherited or
repeatable: put each one on every type it applies to. They are matched by full name, so a library that doesn't
reference Resultful can declare its own `internal` copy (`Resultful.CodeAnalysis.MustObserveAttribute`, and so on).
If it also references Resultful, each use of the copy gives warning CS0436 (the copy in its own source wins), not an
error.

A generated shape implements `IResultShape<T, TError>`, so code can work over any shape. `IErrorUnion` is for a
.NET 11 union of errors: its `Error` property gives the error it holds. It is on every target, but only `net11.0`
code can declare a union.

## Operators

### Map

Takes a `Result<T, TError>` and a function which accepts the success type and returns a type of your choosing `Func<T, TResult>`.
This then returns a `Result<TResult, TError>`and the function is only invoked if the input result was a success value.

### Bind

Takes a `Result<T, TError>` and a function which accepts the success type and returns a new `Result<TResult, TError>` of your choosing.
This then returns a `Result<TResult, TError>` and the function is only invoked if the input result was a success value.

### Tee

Takes a `Result<T, Error>` and an action which accepts the success type but does not return anything.
This returns the same input but has allowed an arbitrary piece of code to run against the successful value.

### Plus

Allows addition of two or more `Result<T, TError>` values, functions are passed in to add T, TError together.
If you add two results together of two different types for example `T1` and `T2` you can provide a `Func<T1, T2, TResult>` which will make the plus function return a `Result<TResult, TError>` if both values where successful.
If you do not want to provide a plus function upfront for your success values then the result will be a tuple of the result types `Result<(T1, T2), TError>` up to 8 values added together, You could then use a map to provide your addition function. Which might prove more readable for most people.

### Fold

This is a way to take an `IEnumerable<Result<T, TError>>` and return either `Result<T, TError>` or `Result<TResult, TError>` based on what you want for the result.
You always provide an accumulator function which should be `Func<TResult, T, TResult>` or `Func<T, T, T>`

### Then, Sequence and Check

`Plus` and `Fold` on `Result<T, TError>` collect every error, which suits independent checks. When each step depends on the one before,
use `Then`, `Sequence` and `Check`: every step runs only if everything before it succeeded, and the first failure is
returned unchanged.

```cs
Result<Unit> PlaceOrder(Order order)
    => Result.Sequence(
        () => Validate(order),
        () => Reserve(order.Items),
        () => Charge(order.Payment))
    .Then(() => Notify(order.Customer));

Result<string> NormaliseName(string input)
    => input.Ok()
        .Then(Trim, NotEmpty, ToTitleCase)                        // Func<string, Result<string>> steps
        .Check(name => name.Length <= 100 ? Result.Ok() : Result.Fail("name is too long")); // can fail, keeps the value
```

- `Sequence(...)` starts a chain of `Result<Unit>` steps (`VoidResult` before 0.3).
- `Then(...)` continues it, either with more `Result<Unit>` steps or with a single step that produces a value
  (`Result.Ok().Then(() => 42.Ok())`, with no `Unit` argument to ignore).
- On `Result<T>`, `Then` pipes the value through steps that return the same type, and `Check` runs steps that can
  fail without replacing the value.

Each method has the same forms for `Result<T, TError>` and `Result<Unit, TError>`, an `Async` variant whose steps return a
`Task`, and an overload on `Task<...>` so that sync and async steps can be mixed in one chain:

```cs
var result = await Result.SequenceAsync(() => LoadAsync(id))
    .Then(Validate)
    .ThenAsync(() => FetchNameAsync(id))
    .Then(Trim, NotEmpty)
    .CheckAsync(name => AuditAsync(name));
```

## Opt-in operators

`using Resultful.Operators;` adds C# 14 extension operators (decision 9). Each is a one-line call to the named
method beside it, which stays the API; without the `using` there are none. Consumers need C# 14 (the default on
`net10.0` and later). `==` and `!=` are on the types themselves and need no `using`.

| Operator | Named method | C# precedence, tightest first |
|---|---|---|
| `a >> f` | `a.Bind(f)` (`BindAsync` on a `Task<Result<…>>`) | shift |
| `a == b`, `a != b` | `a.Equals(b)` | equality |
| `a & b` (`Validation` only) | `Validation.Combine(a, b)`: a tuple, or every error | logical AND |
| `a \| b` | `a.OrElse(b)`: the first success wins | logical OR |
| `a \| value` | `a.ValueOr(value)`: a `T`, not a result (`a \| someError` is `OrElse`: the error converts to a failure, which becomes the fallback) | logical OR |
| `a \|\| b` | `a.OrElse(b)`, but `b` runs only when `a` fails | conditional OR |
| `if (a)`, `a ? x : y` | `a.IsSuccess` (`operator true`; `operator false` is `IsFailure`) | |

The same `|` and `>>` work on `Task<Result<T>>` and `Task<Result<T, TError>>`, with an async step on the right of `>>`.
There is no `+`, `-` or `*`, and no `&&` (decision 14: gathering is `Validation`'s job, and `&` is the tuple combine).

```cs
using Resultful.Operators;

Result<int> Port(string key) =>
    (Read(env, key) || Vault(key) | Read(file, key)) >> Parse >> InRange;   // Vault runs only if env has no key

Validation<(string, int)> fields = Host(request.Host) & PortField(request.Port);
int timeout = await (ReadAsync(file, "timeout") >> (s => ParseAsync(s)) | 30);
```

Precedence is C#'s, so `>>` binds tighter than `|`: `cached | Load(id) >> Parse` is `cached | (Load(id) >> Parse)`,
which parses only the fallback. Write `(cached | Load(id)) >> Parse` to parse whichever wins. A lambda on the right of
`>>` needs parentheses, and `a || b | c` is `a || (b | c)`.

When `T` is a base type of the result's own type (`Result<object>`), `r | "s"` is ambiguous (CS9342): cast the value
or call `ValueOr`. `a & b & c` nests (`((a, b), c)`); for three or more checks use `Validation.Combine(a, b, c)`.

## IPlus

When adding two or more items together of the same type. You have the option to pass in a addition function for both `TError` type and `T`, but as an alternative to this you can add the `IPlus<>` Interface to your type.
This is useful for creating custom Error types adding `IPlus<TError>` to the new type. This will reduce the amount of boilerplate when adding together multiple Results with the same error type.
Or anywhere that accepts `Func<T, T, T>` is typically written to remove that argument whenever IPlus is implemented for that type.

## Async

There are Async operations available which can either accept the input as a Task and/or the `Func` given returns a `Task`. These should be available for all standard operations of the library such as `Map`, `Bind`, `Tee`.
This has not been implemented for Plus or Fold as the Task could actually be in a variety of locations on the arguments. Everything desired could be achieved using other methods in the library or using methods in the TPL.
Also some special ones unique for Async such as ones that convert `Result<Task<T>>` into `Task<Result<T>>`.
