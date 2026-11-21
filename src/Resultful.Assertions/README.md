# Resultful.Assertions

Test assertions for [Resultful](https://github.com/Resultful/Resultful) with no assertion library. Each returns what
the test reads next, and a failure throws `ResultAssertionException`, which every test framework reports.

```csharp
using Resultful.Assertions;

var order = service.Place(request).ShouldSucceed();                    // the value
var error = service.Place(empty).ShouldFail("the basket is empty");    // the error
var notFound = service.Load(99).ShouldFail().ShouldFind<OrderNotFound>();   // the leaf, as Error.TryFind finds it

form.Check().ShouldFail().ShouldFind<Required>();   // a Validation: the errors, searched in order
service.Find(1).ShouldBeSome();                     // an Option: the value; ShouldBeNone() for none
```

They work on `Result<T>`, `Result<T, TError>`, `Validation<T>`, `Validation<T, TError>`, `Option<T>`, and every
generated `[ResultShape]` (through `IResultShape<T, TError>` and `IValidationShape<T, TError>`, so no reflection).

`ShouldFind<TLeaf>()` is declared on `Error`, `IErrorUnion` and `IEnumerable<Error>`, which have no type arguments of
their own, so C# lets the leaf's be written alone. It finds a closed hierarchy's leaf, a `net11.0` union's case, or any
error under the one returned (its cause chain and inner errors). A union is a struct, so a validation over one isn't an
`IEnumerable<Error>`: search the held errors, `validation.ShouldFail().Select(u => u.Error).ShouldFind<CardDeclined>()`.

A failure message names the expected and the actual case, and prints errors with `Error.ToString()`, which never
prints a derived record's own properties. A success value is described by its runtime type, never printed; an error
that isn't an `Error` (such as a `string`) is shown by its type name only:

```text
Expected PlaceOrderResult to succeed because the card is valid, but it failed with:
  PaymentDeclined: the payment was declined
Expected Result<Order> to fail, but it succeeded with a value of type Order.
Expected the error to be or contain OrderNotFound, but it is:
  SaveFailed: order 13 was not saved ---> DbTimeout: the database timed out
```
