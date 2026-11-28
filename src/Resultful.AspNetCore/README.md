# Resultful.AspNetCore

ASP.NET Core for [Resultful](https://github.com/Resultful/Resultful): a result becomes an `IResult`, and its error an
RFC 9457 ProblemDetails through an error-to-status map.

```csharp
builder.Services.AddResultful(errors => errors
    .Map<OrderNotFound>(StatusCodes.Status404NotFound)
    .Map<OrderLocked>(StatusCodes.Status409Conflict));

app.MapGet("/orders/{id}", (int id, Orders orders) => orders.Find(id).ToHttpResult());
// 200 {"id":1,"name":"teapot"}
// 404 {"type":"…rfc9110#section-15.5.5","title":"Not Found","status":404,"detail":"order 9 was not found","instance":"/orders/9","traceId":"…"}
```

- The error is walked outside in (`Error.Causes()`): the first error with an entry decides the status. An entry for
  a leaf beats one for its base type, so a closed hierarchy can be mapped by its base.
- A failed `Validation` (the result's own `ValidationFailed`) answers 400 with a validation problem: `errors` keyed by
  `ValidationKey`, the error's wire name by default (`ErrorWire.Name`, as JSON writes its `type`).
- Anything unmapped answers `DefaultStatusCode`, 500. A 5xx has a fixed title and no `detail` outside the Development
  environment, and is logged once at Error with `Error.ToString()`. A 4xx `detail` is the mapped error's `Message`.
- `ToHttpResult()` returns `ResultHttpResult<T, TError>`. A result whose error is an error union answers for the
  union's leaf, as a `ResultHttpResult<T, Error>`.

Native AOT needs nothing more: problems use ASP.NET Core's own metadata, and a success value the app's JSON options.

Every `Resultful.*` package shares one version: use the same version of `Resultful` and `Resultful.AspNetCore`.
