using CsCheck;
using Resultful.Assertions.TestDomain;

namespace Resultful.Assertions.Tests;

// The assertions agree with the core's own Match and TryFind on every generated value, and never print personal data.
public class AssertionLawTests
{
    private static bool Throws(Action assertion)
    {
        try
        {
            assertion();
            return false;
        }
        catch (ResultAssertionException)
        {
            return true;
        }
    }

    [Fact]
    public void ShouldSucceed_throws_iff_the_result_is_a_failure_and_returns_exactly_the_value()
    {
        Gens.Results.Sample(result =>
        {
            Assert.Equal(result.IsFailure, Throws(() => result.ShouldSucceed()));
            if (result.IsSuccess)
            {
                Assert.Equal(result.Match(v => v, _ => throw new InvalidOperationException()), result.ShouldSucceed());
            }
        });
        Gens.TypedResults.Sample(result => Assert.Equal(result.IsFailure, Throws(() => result.ShouldSucceed())));
        Gens.Shapes.Sample(shape => Assert.Equal(shape.AsResult().IsFailure, Throws(() => shape.ShouldSucceed())));
        Gens.Shapes.Sample(shape => Assert.Equal(shape.AsResult().IsSuccess, Throws(() => shape.ShouldFail())));
    }

    [Fact]
    public void ShouldFail_throws_iff_the_result_is_a_success_and_returns_exactly_the_error()
    {
        Gens.Results.Sample(result =>
        {
            Assert.Equal(result.IsSuccess, Throws(() => result.ShouldFail()));
            if (result.IsFailure)
            {
                Assert.Same(result.Match(_ => null!, e => e), result.ShouldFail());
            }
        });
        Gens.TypedResults.Sample(result =>
        {
            Assert.Equal(result.IsSuccess, Throws(() => result.ShouldFail()));
            if (result.IsFailure)
            {
                Assert.Same(result.Error, result.ShouldFail());
            }
        });
    }

    [Fact]
    public void ShouldFind_throws_iff_TryFind_finds_nothing_and_returns_what_it_finds()
    {
        Gens.Errors.Sample(error =>
        {
            var expected = error.TryFind(out OrderNotFound? f) ? f : null;
            Assert.Equal(expected is null, Throws(() => error.ShouldFind<OrderNotFound>()));
            if (expected is not null)
            {
                Assert.Same(expected, error.ShouldFind<OrderNotFound>());
            }
        });
        Gens.Leaves.Sample(leaf => Assert.Equal(!leaf.TryFind(out PaymentDeclined? _), Throws(() => leaf.ShouldFind<PaymentDeclined>())));
        Gens.Errors.Array[0, 4].Sample(errors =>
        {
            var expected = errors.Select(e => e.TryFind(out OutOfStock? f) ? f : null).FirstOrDefault(f => f is not null);
            Assert.Equal(expected is null, Throws(() => errors.ShouldFind<OutOfStock>()));
            if (expected is not null)
            {
                Assert.Same(expected, errors.ShouldFind<OutOfStock>());
            }
        });
    }

    [Fact]
    public void On_a_validation_ShouldSucceed_and_ShouldFail_agree_with_the_case()
    {
        Gens.Validations.Sample(validation =>
        {
            Assert.Equal(validation.IsFailure, Throws(() => validation.ShouldSucceed()));
            Assert.Equal(validation.IsSuccess, Throws(() => validation.ShouldFail()));
            if (validation.IsFailure)
            {
                Assert.Same(validation.Errors, validation.ShouldFail());
            }
        });
        Gens.TypedValidations.Sample(validation =>
        {
            Assert.Equal(validation.IsFailure, Throws(() => validation.ShouldSucceed()));
            if (validation.IsFailure)
            {
                Assert.Same(validation.Errors, validation.ShouldFail());
            }
        });
    }

    [Fact]
    public void ShouldBeSome_and_ShouldBeNone_agree_with_Match()
    {
        Gens.Options.Sample(option =>
        {
            var isSome = option.Match(_ => true, _ => false);
            Assert.Equal(!isSome, Throws(() => option.ShouldBeSome()));
            Assert.Equal(isSome, Throws(() => option.ShouldBeNone()));
        });
    }

    [Fact]
    public void No_failure_message_ever_contains_a_leafs_own_properties()
    {
        static IEnumerable<string> Messages(Action[] assertions)
        {
            foreach (var assertion in assertions)
            {
                var message = Record.Exception(assertion)?.Message;
                if (message is not null)
                {
                    yield return message;
                }
            }
        }

        Gens.Results.Sample(result =>
        {
            foreach (var message in Messages([() => result.ShouldSucceed(), () => result.ShouldFail(), () => result.ShouldFail().ShouldFind<OutOfStock>()]))
            {
                Assert.DoesNotContain(Sentinels.CardHolder, message, StringComparison.Ordinal);
            }
        });
        Gens.Validations.Sample(validation =>
        {
            foreach (var message in Messages([() => validation.ShouldSucceed(), () => validation.ShouldFail().ShouldFind<GatewayStub>()]))
            {
                Assert.DoesNotContain(Sentinels.CardHolder, message, StringComparison.Ordinal);
            }
        });
    }

    // An error type no generated value holds, so ShouldFind always fails and its message is checked too.
    private sealed record GatewayStub() : Error("never");
}
