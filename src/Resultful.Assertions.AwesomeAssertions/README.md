# Resultful.Assertions.AwesomeAssertions

[AwesomeAssertions](https://awesomeassertions.org) (9.0 and later) for
[Resultful](https://github.com/Resultful/Resultful): `using AwesomeAssertions;` is all a test needs.

```csharp
result.Should().Succeed().Which.Total.Should().Be(12m);
result.Should().Fail().WithMessage("order * was not found").WithContext("loading");
result.Should().FailWith<PaymentDeclined>("the card is {0}", "blocked").Which.Code.Should().Be("51");
save.Should().Fail().WithCause<DbTimeout>();
validation.Should().Fail().Which.Should().HaveCount(2);
option.Should().BeSome().Which.Should().Be(4);
```

`Should()` works on `Result<T>`, `Result<T, TError>`, `Validation<T>`, `Validation<T, TError>`, `Option<T>` and every
generated `[ResultShape]`. `FailWith<TLeaf>()` and `WithError<TLeaf>()` find the leaf as `Error.TryFind` does (a
closed hierarchy's leaf, a `net11.0` union's case, or an error in the cause chain); `WithCause<T>()` looks only under the
error. `WithMessage` and `WithContext` match as the library's own `WithMessage` on an exception does
(`MatchEquivalentOf`: wildcards `*` and `?`, case ignored).

Failures go through `AssertionChain`, so an `AssertionScope` collects them, and the assertions chained on a failed one,
including those on `Which`, are skipped. `because`/`becauseArgs` read as in every other assertion. Messages print errors
with `Error.ToString()` only, never a leaf's own properties, and a success value by its runtime type only:

```text
Expected result to succeed because the card is valid, but it failed with "PaymentDeclined: the payment was declined".
```
