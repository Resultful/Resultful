# Resultful.FSharp

Conversions between [Resultful](https://github.com/Resultful/Resultful) and FSharp.Core, so F# code can consume
results from C# libraries, and hand its own back, with no hand-written glue.

| Resultful | FSharp.Core | To F# | Back |
|---|---|---|---|
| `Result<T, TError>` | `Result<'T, 'E>` | `ToFSharp()` | `ToResultful()` |
| `Result<T>` | `Result<'T, Error>` | `ToFSharp()` | `ToResultful() \|> Resultful.Result.AsResult` |
| `Result<Unit, TError>`, `Result<Unit>` | `Result<unit, 'E>` | `ToFSharpUnit()` | `ToResultfulUnit()` |
| `Validation<T, TError>`, `Validation<T>` | `Result<'T, 'E list>` | `ToFSharp()` | `ToValidation()`, then `\|> Resultful.Validation.AsValidation` for `Validation<T>` |
| `Option<T>` | `'T option`, `'T voption` | `ToFSharp()`, `ToFSharpValueOption()` | `ToResultful()` |

```fsharp
open Resultful.FSharp   // not Resultful: its Result, Error and None types would hide F#'s Result, Error and None

match orders.Find(7).ToFSharp() with          // a C# service returning Result<Order, OrderError>
| Ok order -> printfn "%s" order.Name
| Error (:? OrderNotFound as e) -> printfn "no order %d" e.Id
| Error e -> printfn "%s" e.Message

let total =
    orders.Find(7).ToFSharp()
    |> Result.map (fun o -> o.Total)            // FSharp.Core's own Result module
    |> Result.mapError (fun e -> e.Message)

let back : Resultful.Result<int, OrderError> = (Ok 3).ToResultful()
```

Every conversion is also a plain function: `r |> FSharpInterop.ToFSharp`.

- Values and error objects are kept by reference. A validation's error list is copied, in order.
- Go back to `Result<T>` and `Validation<T>` with `.ToResultful().AsResult()` and `.ToValidation().AsValidation()`
  (from F#, `|> Resultful.Result.AsResult`). No overload returning `Result<T>` will be added: it would silently change
  the type existing calls return.
- Use `ToResultfulUnit()` for `Result<unit, _>`: `(Ok ()).ToResultful()` gives a `Result<FSharp.Core.Unit, _>`.
- A result's or a validation's zero value (`default`) has no case: converting it throws
  `InvalidOperationException`, as the core does. An option's zero value is `None`.
- Going back goes through `Result.Fail` and `Validation.Fail`, so `Error null` throws `ArgumentNullException`,
  `Error []` throws `ArgumentException`, and on `net11.0` so does an error union holding no case (its `default`).
  `Ok null` is a success holding `null`.
- `Some null` stays a value; F#'s `None` (which is `null` at run time) becomes `None`. `Option<T>.ToFSharp()` is
  annotated non-null, as F# sees `'T option`, so it pipes into `Option` functions with F# nullness checks on.

This package converts and nothing more. For F# composition (computation expressions, `traverse`, applicative
validation), convert and use FSharp.Core or a library such as FsToolkit.ErrorHandling.

**Source generators and analyzers don't run in F#.** `Resultful.Generators` and `Resultful.Analyzers` are C#-only.
Define closed hierarchies, error unions and `[ResultShape]` types in a C# project; F# code consumes them, matches
leaves with `:? NotFound`, and converts a shape with `AsResult()` first.

FSharp.Core 4.5.0 or later (the first with `voption`). Targets `netstandard2.0`, `net10.0` and `net11.0`, as the core.
