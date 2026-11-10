# Draft issue for Resultful/Resultful: `default(Result<TValue, TError>)` reads as a success

To be filed by the maintainer. Found while building Matchmark (README, finding 6).

## What happens

`Result<TValue, TError>` is built on OneOf, whose default value is case 0. `default(Result<TValue, TError>)`, an
uninitialised field, or an array element therefore reads as a success holding `default(TValue)` (`null` for a
reference type). Nothing reports it.

```csharp
Result<string, string> result = default;
// result reads as a success, holding null
```

## Why it matters

A result that was never assigned is indistinguishable from a real success, so a missing assignment becomes a silent
`null` further on rather than a failure where it happened.

## Options

1. Make the default an invalid state that throws when matched or read (`InvalidOperationException: an uninitialised Result`).
2. Add an analyzer warning on `default(Result<,>)` and `new Result<,>()`.
3. Document it as a known limitation of the struct.
