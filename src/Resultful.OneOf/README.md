# Resultful.OneOf

Converts [Resultful](https://github.com/Resultful/Resultful)'s results and options to and from
[OneOf](https://github.com/mcintyre321/OneOf) unions. The core `Resultful` package depends on nothing; reference this
package where you need OneOf.

```csharp
using Resultful;

OneOf<int, Error> union = 1.Ok().ToOneOf();          // Result<T> -> OneOf<T, Error>
Result<int> result = union.ToResult();               // and back
Option<Item> item = (await catalog.FindAsync(sku)).ToOption();   // OneOf<T, OneOf.Types.None> -> Option<T>
OneOf.Types.None none = default(Resultful.None).ToOneOf();
```

`ToOneOf` covers `Result<T>`, `Result<T, TError>` and `Option<T>`, and each has a `Task` form. `ToResult` and
`ToOption` convert a `OneOf` (or a `Task` of one) back; a step with no value is a `Result<Unit>`, so a
`OneOf<Unit, Error>` comes back through `ToResult`. The `VoidResult` forms and `ToVoidResult` still work but are
obsolete (decision 14). A result's zero
value (`default`) throws `InvalidOperationException`, as every other member that reads it does.

Every `Resultful.*` package shares one version: use the same version of `Resultful` and `Resultful.OneOf`.
