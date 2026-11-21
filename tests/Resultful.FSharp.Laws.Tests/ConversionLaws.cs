using CsCheck;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using FSharpUnit = Microsoft.FSharp.Core.Unit;

namespace Resultful.FSharp.Laws.Tests;

public sealed record Leaf(string Secret) : Error("a leaf failed");

/// <summary>Round trips both ways for every pair, cases and errors kept by reference, order kept, zero values and nulls.</summary>
public class ConversionLaws
{
    static readonly Gen<string> Text = Gen.String[Gen.Char['!', '~'], 0, 12];
    static readonly Gen<Error> Errors = Text.Select(s => (Error)new Leaf(s));
    static readonly Gen<Result<int, string>> Typed = Gen.Frequency(
        (1, Gen.Int.Select(v => v.Ok<int, string>())),
        (1, Gen.String[Gen.Char['a', 'z'], 1, 8].Select(e => e.Fail<int, string>())));
    static readonly Gen<Result<string>> Plain = Gen.Frequency(
        (1, Text.Select(v => v.Ok())),
        (1, Errors.Select(e => e.Fail<string>())));
    static readonly Gen<FSharpResult<int, string>> FsTyped = Gen.Frequency(
        (1, Gen.Int.Select(FSharpResult<int, string>.NewOk)),
        (1, Gen.String[Gen.Char['a', 'z'], 1, 8].Select(FSharpResult<int, string>.NewError)));
    static readonly Gen<Validation<int, Error>> Validations = Gen.Frequency(
        (1, Gen.Int.Select(Validation.Ok<int, Error>)),
        (1, Errors.List[1, 6].Select(es => Validation.Fail<int, Error>(es))));

    // The core's own message for reading a zero value, so the conversions throw exactly what the core throws.
    static string ZeroMessage(Action read) => Assert.Throws<InvalidOperationException>(read).Message;

    [Fact]
    public void A_typed_result_round_trips_through_FSharp() =>
        Typed.Sample(r => r.ToFSharp().ToResultful() == r);

    [Fact]
    public void An_FSharp_result_round_trips_through_Resultful() =>
        FsTyped.Sample(fs => fs.ToResultful().ToFSharp().Equals(fs));

    [Fact]
    public void ToFSharp_agrees_with_the_cores_Match_and_keeps_the_error_by_reference() =>
        Plain.Sample(r =>
        {
            var fs = r.ToFSharp();
            return fs.IsOk == r.IsSuccess
                && r.Match(v => fs.IsOk && fs.ResultValue == v, e => fs.IsError && ReferenceEquals(fs.ErrorValue, e));
        });

    [Fact]
    public void A_plain_result_round_trips_through_AsResult() =>
        Plain.Sample(r => r.ToFSharp().ToResultful().AsResult() == r);

    [Fact]
    public void ToResultful_keeps_the_case_value_and_error_by_reference() =>
        Gen.Select(Gen.Bool, Gen.Int, Errors).Sample((ok, v, e) =>
        {
            var fs = ok ? FSharpResult<int, Error>.NewOk(v) : FSharpResult<int, Error>.NewError(e);
            var r = fs.ToResultful();
            return ok ? r.IsSuccess && r.Value == v : r.IsFailure && ReferenceEquals(r.Error, e);
        });

    [Fact]
    public void A_step_with_no_value_converts_to_FSharp_unit_and_back() =>
        Gen.Select(Gen.Bool, Errors).Sample((ok, e) =>
        {
            var typed = ok ? Result.Ok<Error>() : e.Fail();
            var plain = ok ? Result.Ok() : Result.Fail(e);
            var fs = typed.ToFSharpUnit();
            var fsPlain = plain.ToFSharpUnit();
            return fs.IsOk == ok && fsPlain.IsOk == ok
                && (ok ? fs.ResultValue is null : ReferenceEquals(fs.ErrorValue, e) && ReferenceEquals(fsPlain.ErrorValue, e))
                && fs.ToResultfulUnit() == typed
                && fsPlain.ToResultfulUnit().AsResult() == plain;
        });

    [Fact]
    public void FSharp_unit_Ok_becomes_the_Unit_value()
    {
        var r = FSharpResult<FSharpUnit?, string>.NewOk(null).ToResultfulUnit();
        Assert.Same(Unit.Value, r.Value);
    }

    [Fact]
    public void A_validation_round_trips_and_keeps_its_errors_in_order() =>
        Validations.Sample(v =>
        {
            var fs = v.ToFSharp();
            var orderKept = v.Match(
                value => fs.IsOk && fs.ResultValue == value,
                errors => fs.IsError && fs.ErrorValue.Length == errors.Count && fs.ErrorValue.Zip(errors).All(p => ReferenceEquals(p.First, p.Second)));
            return orderKept && fs.ToValidation() == v;
        });

    [Fact]
    public void A_plain_validation_round_trips_through_AsValidation() =>
        Validations.Select(v => v.AsValidation()).Sample(v => v.ToFSharp().ToValidation().AsValidation() == v);

    [Fact]
    public void An_FSharp_error_list_round_trips_through_Validation() =>
        Gen.String[Gen.Char['a', 'z'], 1, 4].List[1, 6].Sample(es =>
        {
            var fs = FSharpResult<int, FSharpList<string>>.NewError(ListModule.OfSeq(es));
            return fs.ToValidation().ToFSharp().Equals(fs) && fs.ToValidation().Errors.SequenceEqual(es);
        });

    [Fact]
    public void An_option_round_trips_through_option_and_voption() =>
        Gen.Frequency((1, Text.Select(v => v.Some())), (1, Gen.Const(Option<string>.None))).Sample(o =>
            o.ToFSharp().ToResultful() == o && o.ToFSharpValueOption().ToResultful() == o);

    [Fact]
    public void FSharp_options_round_trip_through_Resultful() =>
        Gen.Frequency((1, Text.Select(FSharpOption<string>.Some)), (1, Gen.Const((FSharpOption<string>)null!))).Sample(fs =>
            Equals(fs.ToResultful().ToFSharp(), fs)
            && fs.ToResultful().ToFSharpValueOption().Equals(fs is null ? FSharpValueOption<string>.ValueNone : FSharpValueOption<string>.NewValueSome(fs.Value)));

    [Fact]
    public void FSharp_value_options_round_trip_through_Resultful() =>
        Gen.Frequency((1, Text.Select(FSharpValueOption<string>.NewValueSome)), (1, Gen.Const(FSharpValueOption<string>.ValueNone))).Sample(fs =>
            fs.ToResultful().ToFSharpValueOption().Equals(fs));

    [Fact]
    public void None_null_and_the_zero_value_are_handled_like_the_core()
    {
        // F#'s None is null at run time; an option holding null is still a value, on both sides.
        Assert.Equal(Option<string>.None, ((FSharpOption<string>)null!).ToResultful());
        Assert.Equal(Option<string>.None, FSharpValueOption<string>.ValueNone.ToResultful());
        Assert.Null(default(Option<string>).ToFSharp());
        Assert.True(default(Option<string>).ToFSharpValueOption().IsValueNone);

        var someNull = FSharpOption<string?>.Some(null).ToResultful();
        Assert.Equal(((string?)null).Some(), someNull);
        var fsSomeNull = someNull.ToFSharp();
        Assert.NotNull(fsSomeNull);
        Assert.Null(fsSomeNull.Value);
        Assert.True(FSharpValueOption<string?>.NewValueSome(null).ToResultful().ToFSharpValueOption().IsValueSome);
    }

    [Fact]
    public void Converting_a_zero_value_throws_what_the_core_throws()
    {
        Assert.Equal(ZeroMessage(() => default(Result<int, string>).Match(_ => 0, _ => 0)), ZeroMessage(() => default(Result<int, string>).ToFSharp()));
        Assert.Equal(ZeroMessage(() => default(Result<int>).Match(_ => 0, _ => 0)), ZeroMessage(() => default(Result<int>).ToFSharp()));
        Assert.Equal(ZeroMessage(() => default(Result<Unit, string>).Match(_ => 0, _ => 0)), ZeroMessage(() => default(Result<Unit, string>).ToFSharpUnit()));
        Assert.Equal(ZeroMessage(() => default(Result<Unit>).Match(_ => 0, _ => 0)), ZeroMessage(() => default(Result<Unit>).ToFSharpUnit()));
        Assert.Equal(ZeroMessage(() => default(Validation<int, string>).Match(_ => 0, _ => 0)), ZeroMessage(() => default(Validation<int, string>).ToFSharp()));
        Assert.Equal(ZeroMessage(() => default(Validation<int>).Match(_ => 0, _ => 0)), ZeroMessage(() => default(Validation<int>).ToFSharp()));
        Assert.Contains("zero value", ZeroMessage(() => default(Result<int, string>).ToFSharp()), StringComparison.Ordinal);
    }

    [Fact]
    public void Going_back_refuses_what_Fail_refuses()
    {
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => FSharpResult<int, string>.NewError(null!).ToResultful()).ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => FSharpResult<FSharpUnit?, string>.NewError(null!).ToResultfulUnit()).ParamName);
        var empty = Assert.Throws<ArgumentException>(() => FSharpResult<int, FSharpList<string>>.NewError(FSharpList<string>.Empty).ToValidation());
        Assert.StartsWith("A failed Validation needs at least one error.", empty.Message, StringComparison.Ordinal);
        Assert.Equal("value", empty.ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => FSharpResult<int, FSharpList<string>>.NewError(ListModule.OfSeq(new[] { "a", null! })).ToValidation()).ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => FSharpResult<int, FSharpList<string>>.NewError(null!).ToValidation()).ParamName);
    }

    [Fact]
    public void Ok_null_goes_back_to_a_success_holding_null()
    {
        var r = FSharpResult<string?, string>.NewOk(null).ToResultful();
        Assert.True(r.IsSuccess);
        Assert.Null(r.Value);
        var v = FSharpResult<string?, FSharpList<string>>.NewOk(null).ToValidation();
        Assert.True(v.IsSuccess);
        Assert.Null(v.Value);
    }

    [Fact]
    public void The_conversions_are_plain_functions_too()
    {
        Func<Result<int, string>, FSharpResult<int, string>> toFSharp = FSharpInterop.ToFSharp;
        Func<FSharpResult<int, string>, Result<int, string>> back = FSharpInterop.ToResultful;
        Assert.Equal(7.Ok<int, string>(), back(toFSharp(7.Ok<int, string>())));
    }

    // Error.ToString never prints a leaf's own properties; the F# side checks %A too in tests/Resultful.FSharp.Tests.
    [Fact]
    public void A_converted_error_prints_no_leaf_property()
    {
        var fs = new Leaf("sentinel-5f1c").Fail<int>().ToFSharp();
        Assert.DoesNotContain("sentinel-5f1c", fs.ToString(), StringComparison.Ordinal);
        Assert.Contains("a leaf failed", fs.ErrorValue.ToString(), StringComparison.Ordinal);
        var list = Validation.Fail<int, Error>(new Leaf("sentinel-5f1c")).ToFSharp();
        Assert.DoesNotContain("sentinel-5f1c", list.ToString(), StringComparison.Ordinal);
    }

#if NET11_0_OR_GREATER
    public sealed record NotFound(int Id) : Error("not found");
    public sealed record Locked(int Id) : Error("locked");
    public union OrderError(NotFound, Locked);

    // A C# 15 error union as TError: the case object travels by reference, and a union holding no case is refused going
    // back, as Result.Fail refuses it on net11.0.
    [Fact]
    public void A_CSharp_15_error_union_converts_both_ways_and_its_zero_value_is_refused_going_back()
    {
        var leaf = new Locked(3);
        OrderError error = leaf;
        var fs = error.Fail<int, OrderError>().ToFSharp();
        Assert.True(fs.IsError);
        Assert.Same(leaf, fs.ErrorValue.Value);
        Assert.Same(leaf, fs.ToResultful().Error.Value);
        Assert.Equal(error.Fail<int, OrderError>(), fs.ToResultful());

        var zero = Assert.Throws<ArgumentException>(() => FSharpResult<int, OrderError>.NewError(default).ToResultful());
        Assert.Contains("OrderError", zero.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => FSharpResult<FSharpUnit?, OrderError>.NewError(default).ToResultfulUnit());
        Assert.Throws<ArgumentException>(() => FSharpResult<int, FSharpList<OrderError>>.NewError(ListModule.OfSeq(new[] { default(OrderError) })).ToValidation());
    }
#endif
}
