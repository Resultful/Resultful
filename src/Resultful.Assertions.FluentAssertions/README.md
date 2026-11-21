# Resultful.Assertions.FluentAssertions

[FluentAssertions](https://fluentassertions.com) **7.x** for [Resultful](https://github.com/Resultful/Resultful):
`using FluentAssertions;` is all a test needs. It depends on `[7.0.0, 8.0.0)`: it fails at runtime on FluentAssertions
8.x (`Execute.Assertion` was removed), and 8.x is commercially licensed. On a newer stack, use
`Resultful.Assertions.AwesomeAssertions`, which has the same API.

```csharp
result.Should().Succeed().Which.Total.Should().Be(12m);
result.Should().Fail().WithMessage("order * was not found").WithContext("loading");
result.Should().FailWith<PaymentDeclined>("the card is {0}", "blocked").Which.Code.Should().Be("51");
save.Should().Fail().WithCause<DbTimeout>();
validation.Should().Fail().Which.Should().HaveCount(2);
option.Should().BeSome().Which.Should().Be(4);
```

It is compiled from the AwesomeAssertions adapter's sources, so the assertions and the messages are the same: errors
are printed with `Error.ToString()` only, an `AssertionScope` collects failures, and `because`/`becauseArgs` read as in
every other assertion. One difference: FluentAssertions 7 can't skip an assertion made on `Which` after a failure inside
an `AssertionScope`, so `Succeed().Which.Should().Be(5)` on a failure reports both.
