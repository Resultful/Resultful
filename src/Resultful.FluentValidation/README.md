# Resultful.FluentValidation

A [FluentValidation](https://docs.fluentvalidation.net) adapter for [Resultful](https://github.com/Resultful/Resultful):
a validator's result becomes a `Validation<T>`, one `FluentValidationError` per failure.

```csharp
Validation<SignUp> checkedSignUp = new SignUpValidator().ValidateToResult(signUp);
Validation<SignUp> checkedAsync = await new SignUpValidator().ValidateToResultAsync(signUp, cancellationToken);

Result<Account> account = checkedSignUp.ToResult() // the failures travel as one ValidationFailed<Error>
    .Bind(accounts.Open);
```

- Plumbing, not rules: every failure FluentValidation reports becomes one error, in order, whatever its severity,
  because FluentValidation's own `IsValid` is false for any failure. A success holds the instance given.
- `FluentValidationError` keeps `PropertyName` (`Address.Line1`, `Tags[1]`), `ErrorCode`, `Severity` and the
  formatted message. The attempted value and custom state are dropped.
- Exceptions and cancellation propagate. FluentValidation 12.0.0 or later.

**Messages are FluentValidation's, and some built-in ones contain values.** `InclusiveBetween`, `ExclusiveBetween`
and `IsInEnum` include the attempted value ("You entered 7."); `Equal`, `NotEqual`, `GreaterThan`,
`GreaterThanOrEqualTo`, `LessThan` and `LessThanOrEqualTo` include the value compared against, which for
`Equal(x => x.Password)` is another property's value. The message reaches `Error.ToString()`, logs and HTTP problem
bodies. For rules over personal data, set the message yourself, or install a `ValidatorOptions.Global.LanguageManager`
whose messages don't use `{PropertyValue}` or `{ComparisonValue}`:

```csharp
RuleFor(s => s.ConfirmPassword).Equal(s => s.Password).WithMessage("The passwords don't match.");
```

With `Resultful.AspNetCore`, key the 400's `errors` by property:

```csharp
services.AddResultful(o => o.ValidationKey = e => (e as FluentValidationError)?.PropertyName ?? "");
```

Every `Resultful.*` package shares one version: use the same version of `Resultful` and `Resultful.FluentValidation`.
