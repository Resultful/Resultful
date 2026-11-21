# Resultful.Diagnostics

Opt-in logging and tracing hooks for [Resultful](https://github.com/Resultful/Resultful) failures. Nothing is logged
or tagged unless a call asks for it, and the package never starts an activity of its own.

```csharp
using var activity = source.StartActivity("charge");
var charged = (await payments.Charge(order))
    .LogFailure(logger, LogLevel.Warning) // one entry: "Result failed with CardDeclined: the card was declined"
    .RecordFailure(activity); // status Error, tag resultful.error.type = "CardDeclined"
```

- A log entry has the leaf's type name, `Message`, `Context` and each cause as `Type: Message`, as text and as
  structured state (`ErrorType`, `ErrorMessage`, `ErrorContext`, `ErrorCauses`), event `1 ResultFailure`.
- A span gets status `Error` and the `resultful.error.type` tag only: no message, no description.
- Neither ever carries a leaf's own properties, which may hold personal data. Put what operators need in `Message`.
- A success, the zero value, `LogLevel.None` and a missing activity do nothing; each hook returns the result unchanged.
- `RecordFailure(null)` marks `Activity.Current`, the innermost span, which in ASP.NET Core is often the HTTP server
  span. For an expected failure (validation, not found), pass your own activity instead.
- A failed `Validation<T>` logs and tags as one `ValidationFailed<Error>`. A result whose error is an error union
  logs and tags the union's leaf. The obsolete `VoidResult` types convert to `Result<Unit>` first.

Every `Resultful.*` package shares one version: use the same version of `Resultful` and `Resultful.Diagnostics`.
