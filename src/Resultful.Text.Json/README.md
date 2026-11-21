# Resultful.Text.Json

System.Text.Json converters for [Resultful](https://github.com/Resultful/Resultful): `Result<T>`,
`Result<T, TError>`, `Validation<T>`, `Validation<T, TError>`, `Unit`, every `Error`, and error unions.

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    .AddResultful()                                  // every converter (needs dynamic code)
    .AddErrorCases<OrderError>(OrderError.Cases);    // the leaves a "type" may name when reading

JsonSerializer.Serialize(Result.Ok(new Order(1, "tea")), options);
// {"ok":true,"value":{"id":1,"name":"tea"}}
JsonSerializer.Serialize(Result.Fail<Order>(new OrderNotFound(2).WithContext("loading")), options);
// {"ok":false,"error":{"type":"OrderNotFound","message":"order 2 was not found","context":["loading"],"id":2}}
```

The wire format is fixed: `ok` first, then `value`, `error` or (for a `Validation`) `errors`. An error is
`type` (the leaf's simple name, `ValidationFailed<X>` for a generic one), `message`, `context` (always), `cause`
(when set), then the leaf's own properties, named by the options' naming policy. An error union is written as its
innermost leaf. `Unit` is `{}`. The zero value (`default`) of a result throws on write.

Reading resolves `type` through the registered cases, with no reflection. An unknown `type`, a repeated key, or a
missing or non-string `message` throws `JsonException`.

**Native AOT.** Skip `AddResultful`. Give each result type converter-backed metadata from a resolver combined with
your source-generated context, and list the value types and leaves in that context:

```csharp
JsonMetadataServices.CreateValueInfo<Result<Order>>(options, new ResultJsonConverter<Order>());
// An error union also needs its own metadata, or reading Result<Order, OrderError> throws NotSupportedException:
JsonMetadataServices.CreateValueInfo<OrderError>(options, new ErrorUnionJsonConverter<OrderError>(OrderError.Cases, OrderError.Wrap));
```

**Escaping.** With the default encoder, `ValidationFailed<Error>` is written as `"ValidationFailed\u003CError\u003E"`:
the same JSON string value, read back the same. Set `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` to see
`<` and `>` as written.

Every `Resultful.*` package shares one version: use the same version of `Resultful` and `Resultful.Text.Json`.
