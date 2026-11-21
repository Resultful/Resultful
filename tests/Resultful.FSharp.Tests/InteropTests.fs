module Resultful.FSharp.Tests.InteropTests

open System
open Xunit
// Resultful.FSharp alone, not Resultful: Resultful's Result, Error and None types would hide F#'s Result type and its
// Error and None cases. Resultful's types are named in full where needed.
open Resultful.FSharp
open Resultful.FSharp.Tests.Domain

let service () = OrderService()

[<Fact>]
let ``a converted result matches as Ok and Error, with leaves matched by type`` () =
    let describe id =
        match service().Find(id).ToFSharp() with
        | Ok order -> $"found {order.Customer}"
        | Error(:? OrderNotFound as e) -> $"no order {e.Id}"
        | Error e -> e.Message

    Assert.Equal("found ada", describe 1)
    Assert.Equal("no order 9", describe 9)

[<Fact>]
let ``a converted result pipes into FSharp.Core's Result module`` () =
    let total id =
        service().Find(id).ToFSharp()
        |> Result.map (fun o -> o.Total)
        |> Result.mapError (fun e -> e.Message)

    Assert.Equal(Ok 120m, total 2)
    Assert.Equal(Error "order 9 was not found", total 9)

[<Fact>]
let ``an F# result goes back to Resultful, as a method or a function`` () =
    let back: Resultful.Result<int, string> = (Ok 3).ToResultful()
    Assert.Equal(3, back.Value)

    let failed = Error "boom" |> FSharpInterop.ToResultful<int, string>
    Assert.Equal("boom", failed.Error)

    let plain: Resultful.Result<int> =
        (Error(Required "name" :> Resultful.Error)).ToResultful() |> Resultful.Result.AsResult
    Assert.IsType<Required>(plain.Error) |> ignore

[<Fact>]
let ``options convert to Some and None, and voption`` () =
    match service().TryFind(1).ToFSharp(), service().TryFind(9).ToFSharp() with
    | Some order, None -> Assert.Equal("ada", order.Customer)
    | other -> failwithf "unexpected %A" other

    Assert.Equal(ValueNone, service().TryFind(9).ToFSharpValueOption())
    Assert.Equal(Resultful.Option<int>.None, None.ToResultful())
    Assert.Equal(Resultful.Option.Some 5, (ValueSome 5).ToResultful())

// This project builds with F# nullness checks on and warnings as errors, so a nullable annotation on ToFSharp fails here.
[<Fact>]
let ``a converted option pipes into FSharp.Core's Option module with nullness checks on`` () =
    let found = service().TryFind(1).ToFSharp()
    let missing = service().TryFind(9).ToFSharp()
    Assert.True(found |> Option.isSome)
    Assert.True(missing |> Option.isNone)
    Assert.Equal(Some 30m, found |> Option.map (fun o -> o.Total))
    Assert.Equal(0m, missing |> Option.map (fun o -> o.Total) |> Option.defaultValue 0m)
    Assert.Equal(ValueSome "ada", service().TryFind(1).ToFSharpValueOption() |> ValueOption.map (fun o -> o.Customer))

[<Fact>]
let ``a validation becomes a list of errors, in order, and comes back`` () =
    match service().Check("", 5000m).ToFSharp() with
    | Error [ :? Required as r; :? OutOfRange as o ] ->
        Assert.Equal("customer", r.Field)
        Assert.Equal(5000m, o.Value)
    | other -> failwithf "unexpected %A" other

    let back = (Error [ "a"; "b" ]: Result<int, string list>).ToValidation()
    Assert.Equal<string>([ "a"; "b" ], back.Errors :> seq<string>)

[<Fact>]
let ``a step with no value is Result of unit in F#`` () =
    let order = service().Find(1).Value

    match service().Save(order).ToFSharpUnit() with
    | Ok() -> ()
    | Error e -> failwith e.Message

    let back: Resultful.Result<Resultful.Unit, string> = (Ok()).ToResultfulUnit()
    Assert.Same(Resultful.Unit.Value, back.Value)

[<Fact>]
let ``the zero value throws as it does in the core`` () =
    let e = Assert.Throws<InvalidOperationException>(fun () -> Unchecked.defaultof<Resultful.Result<int, string>>.ToFSharp() |> ignore)
    Assert.Contains("zero value", e.Message)

[<Fact>]
let ``F#'s printing of a converted error shows no leaf property`` () =
    let charged = service().Charge(service().Find(2).Value, "4111-sentinel").ToFSharp()
    Assert.DoesNotContain("4111-sentinel", sprintf "%A" charged)
    Assert.DoesNotContain("4111-sentinel", string charged)
    Assert.Contains("payment for order 2 was declined", sprintf "%A" charged)

// A C# service consumed end to end: validate, find, charge and save, in F#'s own Result, then hand a Resultful result back.
let placeOrder (svc: OrderService) customer total card : Resultful.Result<Order, OrderError> =
    let checkedOrder =
        svc.Check(customer, total).ToFSharp()
        |> Result.mapError (fun errors -> errors |> List.map (fun e -> e.Message) |> String.concat "; ")

    match checkedOrder with
    | Error reasons -> Error(OrderRejected reasons :> OrderError) |> FSharpInterop.ToResultful
    | Ok order ->
        svc.Charge(order, card).ToFSharp()
        |> Result.bind (fun charged -> svc.Save(charged).ToFSharpUnit() |> Result.map (fun () -> charged))
        |> FSharpInterop.ToResultful

[<Fact>]
let ``a C# service result is consumed in F# and handed back`` () =
    let svc = service ()

    let placed = placeOrder svc "linus" 40m "card-1"
    Assert.True(placed.IsSuccess)
    Assert.Equal(Some "linus", svc.TryFind(placed.Value.Id).ToFSharp() |> Option.map (fun o -> o.Customer))

    // A fresh service holds orders 1 and 2, so the declined order is 3 whatever ran before.
    match (placeOrder (service ()) "linus" 400m "card-2").ToFSharp() with
    | Error(:? PaymentDeclined as d) -> Assert.Equal(3, d.Id)
    | other -> failwithf "unexpected %A" other

    let invalid = placeOrder svc "" 0m "card-3"
    Assert.Equal("customer is required; total is out of range", (invalid.Error :?> OrderRejected).Reasons)

[<Fact>]
let ``an async C# result awaits and converts in a task`` () =
    task {
        let! found = service().FindAsync(1)
        let! missing = service().FindAsync(7)

        match found.ToFSharp(), missing.ToFSharp() with
        | Ok o, Error(:? OrderNotFound) -> Assert.Equal(1, o.Id)
        | other -> failwithf "unexpected %A" other
    }
