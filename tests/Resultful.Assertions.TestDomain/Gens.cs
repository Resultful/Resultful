using CsCheck;

namespace Resultful.Assertions.TestDomain;

/// <summary>CsCheck generators over the real core types and the domain's errors.</summary>
public static class Gens
{
    /// <summary>Personal data that leaves may hold, sentinel included.</summary>
    public static readonly Gen<string> Holders = Gen.OneOf(Gen.Const(Sentinels.CardHolder), Gen.String.AlphaNumeric.Select(s => Sentinels.CardHolder + s));

    public static readonly Gen<OrderError> Leaves = Gen.OneOf<OrderError>(
        Gen.Int[0, 100].Select(id => (OrderError)new OrderNotFound(id)),
        Gen.String.AlphaNumeric.Select(sku => (OrderError)new OutOfStock(sku)),
        Holders.Select(holder => (OrderError)new PaymentDeclined(holder)));

    /// <summary>Any error: a leaf, a plain message, a leaf as a cause, a list of leaves, with or without context.</summary>
    public static readonly Gen<Error> Errors = Gen.OneOf<Error>(
        Leaves.Select(leaf => (Error)leaf),
        Gen.String.AlphaNumeric.Select(m => Error.From(m)),
        Gen.Select(Gen.Int[0, 100], Leaves).Select((id, leaf) => (Error)new SaveFailed(id) { Cause = leaf }),
        Leaves.Array[1, 3].Select(leaves => (Error)new ValidationFailed<OrderError>(leaves)),
        Gen.Select(Leaves, Gen.String.AlphaNumeric).Select((leaf, note) => (Error)leaf.WithContext(note)));

    public static readonly Gen<Result<int>> Results = Gen.Frequency(
        (1, Gen.Int.Select(v => v.Ok())),
        (2, Errors.Select(e => e.Fail<int>())));

    public static readonly Gen<Result<int, OrderError>> TypedResults = Gen.Frequency(
        (1, Gen.Int.Select(v => v.Ok<int, OrderError>())),
        (2, Leaves.Select(e => e.Fail<int, OrderError>())));

    public static readonly Gen<Validation<int>> Validations = Gen.Frequency(
        (1, Gen.Int.Select(v => Validation.Ok(v))),
        (2, Errors.Array[1, 4].Select(es => Validation.Fail<int>(es))));

    public static readonly Gen<Validation<int, OrderError>> TypedValidations = Gen.Frequency(
        (1, Gen.Int.Select(v => Validation.Ok<int, OrderError>(v))),
        (2, Leaves.Array[1, 4].Select(es => Validation.Fail<int, OrderError>(es))));

    public static readonly Gen<PlaceOrderResult> Shapes = Gen.Frequency(
        (1, Gen.Int.Select(v => (PlaceOrderResult)new Order(v, "tea", 1))),
        (2, Leaves.Select(e => PlaceOrderResult.Fail(e))));

    public static readonly Gen<Option<int>> Options = Gen.Frequency(
        (1, Gen.Int.Select(v => (Option<int>)v)),
        (1, Gen.Const(Option<int>.None)));
}
