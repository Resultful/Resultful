using CsCheck;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Resultful.HotChocolate.Tests;

// The same results from a resolver returning FieldResult (through ToFieldResult) and one returning the result.
public sealed class ParityQuery
{
    public Result<User> Native([GlobalState("result")] Result<User> result) => result;

    public FieldResult<User, UserNotFound, UserSuspended, ResultfulValidationError> Field([GlobalState("result")] Result<User> result) =>
        result.ToFieldResult<User, UserNotFound, UserSuspended, ResultfulValidationError>();

    public FieldResult<User, UserNotFound> Strict([GlobalState("result")] Result<User> result) =>
        result.ToFieldResult<User, UserNotFound>();

    public Result<User, UserError> TypedNative([GlobalState("result")] Result<User, UserError> result) => result;

    public FieldResult<User, UserNotFound> TypedField([GlobalState("result")] Result<User, UserError> result) =>
        result.ToFieldResult<User, UserError, UserNotFound>();

    public Validation<User, Error> ValidationNative([GlobalState("result")] Validation<User, Error> result) => result;

    public FieldResult<User, ResultfulValidationError> ValidationField([GlobalState("result")] Validation<User, Error> result) =>
        Validation.ToResult(result).ToFieldResult<User, ResultfulValidationError>();
}

public class FieldResultTests
{
    private static readonly Order Placed = new(1, "SKU", 2);

    // The first five leaves are declared by the five-leaf overload; the sixth is never declared.
    private static readonly ShippingError[] Leaves =
    [
        new AddressMissing(), new PostcodeInvalid("X1"), new CountryBlocked("Atlantis"), new CarrierDown("Owl"), new ParcelTooHeavy(41m), new ParcelTooLarge(300),
    ];

    private static readonly Func<Result<Order, ShippingError>, IFieldResult>[] Typed =
    [
        r => r.ToFieldResult(),
        r => r.ToFieldResult<Order, ShippingError, AddressMissing>(),
        r => r.ToFieldResult<Order, ShippingError, AddressMissing, PostcodeInvalid>(),
        r => r.ToFieldResult<Order, ShippingError, AddressMissing, PostcodeInvalid, CountryBlocked>(),
        r => r.ToFieldResult<Order, ShippingError, AddressMissing, PostcodeInvalid, CountryBlocked, CarrierDown>(),
        r => r.ToFieldResult<Order, ShippingError, AddressMissing, PostcodeInvalid, CountryBlocked, CarrierDown, ParcelTooHeavy>(),
    ];

    private static readonly Func<Result<Order>, IFieldResult>[] Untyped =
    [
        r => r.ToFieldResult(),
        r => r.ToFieldResult<Order, AddressMissing>(),
        r => r.ToFieldResult<Order, AddressMissing, PostcodeInvalid>(),
        r => r.ToFieldResult<Order, AddressMissing, PostcodeInvalid, CountryBlocked>(),
        r => r.ToFieldResult<Order, AddressMissing, PostcodeInvalid, CountryBlocked, CarrierDown>(),
        r => r.ToFieldResult<Order, AddressMissing, PostcodeInvalid, CountryBlocked, CarrierDown, ParcelTooHeavy>(),
    ];

    public static TheoryData<int> Arities() => [0, 1, 2, 3, 4, 5];

    [Theory]
    [MemberData(nameof(Arities))]
    public void A_typed_result_keeps_its_value_and_declared_leaves_and_throws_for_the_rest(int arity)
    {
        var convert = Typed[arity];

        AssertSuccess(convert(Result.Ok<Order, ShippingError>(Placed)));
        for (var i = 0; i < arity; i++) AssertError(convert(Result.Fail<Order, ShippingError>(Leaves[i])), Leaves[i]);
        AssertUndeclared(() => convert(Result.Fail<Order, ShippingError>(Leaves[arity])), Leaves[arity]);
    }

    [Theory]
    [MemberData(nameof(Arities))]
    public void An_untyped_result_keeps_its_value_and_declared_leaves_and_throws_for_the_rest(int arity)
    {
        var convert = Untyped[arity];

        AssertSuccess(convert(Result.Ok(Placed)));
        for (var i = 0; i < arity; i++) AssertError(convert(Result.Fail<Order>(Leaves[i])), Leaves[i]);
        AssertUndeclared(() => convert(Result.Fail<Order>(Leaves[arity])), Leaves[arity]);
    }

    [Fact]
    public void A_validation_failure_matches_ResultfulValidationError_by_type_name()
    {
        var failed = Result.Fail<Order>(new ValidationFailed<Error>([new Required("sku"), new TooLong("note", 5), new Required("qty")]));

        var converted = failed.ToFieldResult<Order, ResultfulValidationError>();

        var error = Assert.IsType<ResultfulValidationError>(Assert.Single(converted.Errors!));
        Assert.Equal("Validation failed.", error.Message);
        Assert.Equal(["Required", "TooLong"], error.Fields.Select(f => f.Field));
        Assert.Equal(["sku is required.", "qty is required."], error.Fields[0].Messages);
        Assert.Equal(["note is longer than 5 characters."], error.Fields[1].Messages);
    }

    [Fact]
    public void A_validation_failure_without_ResultfulValidationError_is_undeclared()
    {
        var error = new ValidationFailed<Required>([new Required("sku")]);

        AssertUndeclared(() => Result.Fail<Order>(error).ToFieldResult<Order, AddressMissing>(), error);
    }

    [Fact]
    public void A_leaf_that_is_not_an_Error_converts_too_and_an_undeclared_one_throws_with_its_type_name()
    {
        var converted = Result.Fail<Order, string>("taken").ToFieldResult<Order, string, string>();
        var thrown = Assert.ThrowsAny<Exception>(() => Result.Fail<Order, int>(42).ToFieldResult());

        Assert.Equal("taken", Assert.Single(converted.Errors!));
        Assert.Equal("ResultfulErrorException", thrown.GetType().Name);
        Assert.Equal("Int32", thrown.Message);
    }

    [Fact]
    public void The_zero_value_throws_as_reading_it_does()
    {
        Assert.Throws<InvalidOperationException>(() => default(Result<Order, ShippingError>).ToFieldResult<Order, ShippingError, AddressMissing>());
        Assert.Throws<InvalidOperationException>(() => default(Result<Order>).ToFieldResult<Order, AddressMissing>());
    }

    // Law: ToFieldResult agrees with the core's Match: the same success value, or the same error object; a failure
    // that isn't one of the leaf types (UserSuspended here) throws, with Error.ToString() as its message.
    [Fact]
    public void ToFieldResult_agrees_with_Match() =>
        Generators.UserResult.Sample(result =>
        {
            var expected = result.Match<object>(value => value, error => error);
            if (expected is UserSuspended suspended)
            {
                AssertUndeclared(() => result.ToFieldResult<User, UserError, UserNotFound>(), suspended);
                return;
            }

            IFieldResult converted = result.ToFieldResult<User, UserError, UserNotFound>();
            Assert.Equal(result.IsSuccess, converted.IsSuccess);
            Assert.Equal(expected, converted.IsSuccess ? converted.Value : Assert.Single((IReadOnlyList<object>)converted.Value!));
        }, iter: 300);

    public static TheoryData<string, bool> Settings() => new()
    {
        { Graph.Production, false },
        { Graph.Development, false },
        { Graph.Production, true },
    };

    // Law: for any result, a FieldResult resolver and a result resolver answer the same JSON, top-level errors included:
    // for Result<T>, Result<T, TError> with an undeclared leaf, and a Validation, in Production and Development, with
    // the default options and with leaf fields and details shown. ValidationKey stays the default: ToFieldResult has no
    // options and always keys a validation failure by type name.
    [Theory]
    [MemberData(nameof(Settings))]
    public async Task A_FieldResult_resolver_and_a_result_resolver_answer_the_same(string environment, bool showMore)
    {
        var executor = await Graph.BuildAsync(
            b => b.AddQueryType<ParityQuery>(),
            o =>
            {
                o.AddErrorCases<Error>([typeof(UserNotFound), typeof(UserSuspended)]).AddErrorCases<UserError>([typeof(UserNotFound)]);
                o.ExposeLeafFields = showMore;
                o.IncludeDetails = showMore;
            },
            environment);
        var leafFields = showMore ? "... on UserNotFound { id details } ... on UserSuspended { reason details }" : "";
        var untyped = $"__typename ... on User {{ id name }} ... on Error {{ message }} {leafFields} ... on ValidationError {{ fields {{ field messages }} }}";
        var typed = $"__typename ... on User {{ id name }} ... on Error {{ message }} {(showMore ? "... on UserNotFound { id details }" : "")}";
        var validation = "__typename ... on User { id } ... on ValidationError { message fields { field messages } }";

        await Generators.UntypedUserResult.SampleAsync(async result =>
            Assert.Equal(
                await Graph.RunJsonAsync(executor, $"{{ r: native {{ {untyped} }} }}", result),
                await Graph.RunJsonAsync(executor, $"{{ r: field {{ {untyped} }} }}", result)), iter: 60);
        await Generators.UserResult.SampleAsync(async result =>
            Assert.Equal(
                await Graph.RunJsonAsync(executor, $"{{ r: typedNative {{ {typed} }} }}", result),
                await Graph.RunJsonAsync(executor, $"{{ r: typedField {{ {typed} }} }}", result)), iter: 60);
        await Generators.UserValidation.SampleAsync(async result =>
            Assert.Equal(
                await Graph.RunJsonAsync(executor, $"{{ r: validationNative {{ {validation} }} }}", result),
                await Graph.RunJsonAsync(executor, $"{{ r: validationField {{ {validation} }} }}", result)), iter: 40);
    }

    [Fact]
    public async Task An_undeclared_validation_failure_from_ToFieldResult_has_a_generic_code()
    {
        var executor = await Parity(Graph.Development);

        var response = await Graph.RunAsync(executor, "{ strict { __typename } }", Result.Fail<User>(new ValidationFailed<Required>([new Required("name")])));

        var entry = Assert.Single(response["errors"]!.AsArray())!;
        Assert.Equal("ValidationFailed<Required>", (string?)Assert.Single(entry["extensions"]!.AsObject(), p => p.Key == "code").Value);
        Assert.Single(entry["extensions"]!.AsObject());
        Assert.Equal("ValidationFailed<Required>: Validation failed. [Required: name is required.]", (string?)entry["message"]);
    }

    private static Task<IRequestExecutor> Parity(string environment) =>
        Graph.BuildAsync(b => b.AddQueryType<ParityQuery>(), o => o.AddErrorCases<Error>([typeof(UserNotFound), typeof(UserSuspended)]).AddErrorCases<UserError>(UserError.Cases), environment);

    private static void AssertSuccess(IFieldResult converted)
    {
        Assert.True(converted.IsSuccess);
        Assert.False(converted.IsError);
        Assert.Equal(Placed, converted.Value);
    }

    private static void AssertError(IFieldResult converted, object expected)
    {
        Assert.True(converted.IsError);
        Assert.Same(expected, Assert.Single((IReadOnlyList<object>)converted.Value!));
    }

    private static void AssertUndeclared(Action convert, Error error)
    {
        var thrown = Assert.ThrowsAny<Exception>(convert);
        Assert.Equal("ResultfulErrorException", thrown.GetType().Name);
        Assert.Equal(error.ToString(), thrown.Message);
    }
}

/// <summary>Random results over the test domain, declared and undeclared failures alike.</summary>
public static class Generators
{
    private static readonly Gen<int> Id = Gen.Int[1, 999];
    private static readonly Gen<string> Word = Gen.String[Gen.Char.AlphaNumeric, 1, 8];

    public static readonly Gen<User> AnyUser = Gen.Select(Id, Word, (id, name) => new User(id, name));

    public static readonly Gen<UserError> AnyUserError = Gen.OneOf<UserError>(
        Id.Select(id => (UserError)new UserNotFound(id)),
        Gen.Select(Id, Word, (id, reason) => (UserError)new UserSuspended(id, reason)));

    public static readonly Gen<Result<User, UserError>> UserResult = Gen.Frequency(
        (1, AnyUser.Select(u => Result.Ok<User, UserError>(u))),
        (2, AnyUserError.Select(e => Result.Fail<User, UserError>(e))));

    public static readonly Gen<Error> Invalid = Gen.OneOf<Error>(
        Word.Select(f => (Error)new Required(f)),
        Gen.Select(Word, Gen.Int[1, 20], (f, max) => (Error)new TooLong(f, max)));

    public static readonly Gen<Validation<User, Error>> UserValidation = Gen.Frequency(
        (1, AnyUser.Select(u => Validation.Ok<User, Error>(u))),
        (2, Invalid.Array[1, 5].Select(errors => Validation.Fail<User, Error>(errors))));

    public static readonly Gen<Result<User>> UntypedUserResult = Gen.Frequency(
        (2, AnyUser.Select(u => Result.Ok(u))),
        (3, AnyUserError.Select(e => Result.Fail<User>(e))),
        (1, Invalid.Array[1, 5].Select(errors => Result.Fail<User>(new ValidationFailed<Error>(errors)))),
        (1, Word.Select(server => Result.Fail<User>(new DatabaseDown(server).WithContext("saving")))));
}
