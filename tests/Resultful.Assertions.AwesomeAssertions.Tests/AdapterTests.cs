using CsCheck;
using Resultful.Assertions.TestDomain;
#if RESULTFUL_FLUENTASSERTIONS
using FluentAssertions;
using FluentAssertions.Execution;
#else
using AwesomeAssertions;
using AwesomeAssertions.Execution;
#endif

namespace Resultful.Assertions.AdapterTests;

// Every assertion of the adapter, pass and fail, with the message pinned. These sources compile twice: against
// AwesomeAssertions here and FluentAssertions 7 in Resultful.Assertions.FluentAssertions.Tests. The subject is always a
// local named after its default context ("result", "validation", "option"), so the message is the same whether or not
// the library finds the test's source line.
public class AdapterTests
{
    private static readonly Error Boom = Error.From("boom");
    private static readonly OrderError NotFound = new OrderNotFound(7);
    private static readonly Error Wrapped = new SaveFailed(13) { Cause = new DbTimeout() }.WithContext("saving");

    // The message of the exception the library throws for this test framework.
    private static string Fails(Action assertion)
    {
        var exception = Record.Exception(assertion);
        Assert.NotNull(exception);
        return exception.Message;
    }

    public class OnResult
    {
        [Fact]
        public void Succeed_passes_and_Which_is_the_value()
        {
            Result<int> result = 5.Ok();
            result.Should().Succeed().Which.Should().Be(5);
            result.Should().Succeed("it is {0}", "cached").And.Succeed().Which.Should().Be(5);
        }

        [Fact]
        public void Succeed_on_a_failure_prints_the_error_with_ToString()
        {
            Result<int> result = Wrapped.Fail<int>();
            Assert.Equal(
                "Expected result to succeed, but it failed with \"SaveFailed: order 13 was not saved (context: saving) ---> DbTimeout: the database timed out\".",
                Fails(() => result.Should().Succeed()));
        }

        [Fact]
        public void Because_and_its_arguments_are_formatted_in_the_librarys_style()
        {
            Result<int> result = Boom.Fail<int>();
            Assert.Equal(
                "Expected result to succeed because order 7 exists, but it failed with \"MessageError: boom\".",
                Fails(() => result.Should().Succeed("order {0} exists", 7)));
            Assert.Equal(
                "Expected result to succeed because it was saved, but it failed with \"MessageError: boom\".",
                Fails(() => result.Should().Succeed("because it was saved")));
        }

        [Fact]
        public void Succeed_and_Fail_on_the_zero_value()
        {
            var result = default(Result<int>);
            Assert.Equal(
                "Expected result to succeed, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => result.Should().Succeed()));
            Assert.Equal(
                "Expected result to fail, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => result.Should().Fail()));
            Assert.Equal(
                "Expected result to fail with OrderNotFound, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => result.Should().FailWith<OrderNotFound>()));
        }

        [Fact]
        public void Fail_passes_and_Which_is_the_error()
        {
            Result<int> result = Boom.Fail<int>();
            result.Should().Fail().Which.Should().BeSameAs(Boom);
            result.Should().Fail("the disk is full").Which.Should().BeSameAs(Boom);
        }

        [Fact]
        public void Fail_on_a_success()
        {
            Result<int> result = 5.Ok();
            Assert.Equal("Expected result to fail, but it succeeded with a value of type Int32.", Fails(() => result.Should().Fail()));
            Assert.Equal("Expected result to fail because the disk is full, but it succeeded with a value of type Int32.", Fails(() => result.Should().Fail("the disk is full")));
        }

        [Fact]
        public void FailWith_finds_the_leaf_itself_or_under_the_error()
        {
            Result<int> result = Wrapped.Fail<int>();
            result.Should().FailWith<DbTimeout>().Which.Should().BeOfType<DbTimeout>();
            result.Should().FailWith<SaveFailed>("it was saving").Which.Should().BeOfType<SaveFailed>();

            Result<int, OrderError> typed = NotFound.Fail<int, OrderError>();
            typed.Should().FailWith<OrderNotFound>().Which.Should().BeSameAs(NotFound);
        }

        [Fact]
        public void FailWith_names_the_leaf_and_prints_the_actual_error()
        {
            Result<int> result = Wrapped.Fail<int>();
            Assert.Equal(
                "Expected result to fail with OrderNotFound because order 7 is gone, but it failed with \"SaveFailed: order 13 was not saved (context: saving) ---> DbTimeout: the database timed out\".",
                Fails(() => result.Should().FailWith<OrderNotFound>("order 7 is gone")));
            Result<int> ok = 5.Ok();
            result = ok;
            Assert.Equal("Expected result to fail with OrderNotFound, but it succeeded with a value of type Int32.", Fails(() => result.Should().FailWith<OrderNotFound>()));
        }

        [Fact]
        public void A_typed_result_with_an_error_that_is_not_an_Error_prints_only_its_type()
        {
            Result<int, string> result = "card 4111 for Jane".Fail<int, string>();
            result.Should().Fail().Which.Should().Be("card 4111 for Jane");
            Assert.Equal(
                "Expected result to succeed, but it failed with \"String (not an Error, so only its type is shown)\".",
                Fails(() => result.Should().Succeed()));
            Assert.Equal(
                "Expected the error to have a message matching \"card*\", but it is a String, not an Error.",
                Fails(() => result.Should().Fail().WithMessage("card*")));
            Assert.Equal(
                "Expected the error to have a cause of type DbTimeout, but it is a String, not an Error.",
                Fails(() => result.Should().Fail().WithCause<DbTimeout>()));
        }
    }

    public class OnTheError
    {
        [Fact]
        public void WithMessage_matches_as_the_librarys_exception_WithMessage_does()
        {
            Result<int, OrderError> result = NotFound.Fail<int, OrderError>();

            // MatchEquivalentOf, as the library's own WithMessage on an exception: wildcards, and case is ignored.
            result.Should().Fail().WithMessage("order 7 was not found").WithMessage("order * was not found").WithMessage("ORDER ? WAS *");
            Assert.Equal(
                "Expected error message to match the equivalent of \"order 8 *\" because messages are exact, but \"order 7 was not found\" does not.",
                Fails(() => result.Should().Fail().WithMessage("order 8 *", "messages are exact")));
            Assert.Throws<ArgumentNullException>(() => result.Should().Fail().WithMessage(null!));
        }

        [Fact]
        public void WithContext_matches_any_note()
        {
            Result<int> result = Wrapped.Fail<int>().MapError(e => e.WithContext("checkout"));
            result.Should().Fail().WithContext("saving").WithContext("CHECK*");
            Assert.Equal(
                "Expected the error to have a context note matching \"loading\", but found {\"saving\", \"checkout\"}.",
                Fails(() => result.Should().Fail().WithContext("loading")));
            Assert.Throws<ArgumentNullException>(() => result.Should().Fail().WithContext(null!));
        }

        [Fact]
        public void WithCause_looks_only_under_the_error()
        {
            Result<int> result = Wrapped.Fail<int>();
            result.Should().Fail().WithCause<DbTimeout>().WithMessage("the database * out").Which.Should().BeOfType<DbTimeout>();
            Assert.Equal(
                "Expected the error to have a cause of type SaveFailed, but found \"SaveFailed: order 13 was not saved (context: saving) ---> DbTimeout: the database timed out\".",
                Fails(() => result.Should().Fail().WithCause<SaveFailed>()));
        }

        [Fact]
        public void WithError_finds_the_error_itself_or_under_it()
        {
            Result<int> result = new ValidationFailed<OrderError>([new OutOfStock("mug"), NotFound]).Fail<int>();
            result.Should().Fail().WithError<OrderNotFound>().Which.Should().BeSameAs(NotFound);
            result.Should().Fail().WithError<ValidationFailed<OrderError>>().Which.Should().BeOfType<ValidationFailed<OrderError>>();
            Assert.Equal(
                "Expected the error to be or contain PaymentDeclined because the card was refused, but found \"ValidationFailed<OrderError>: Validation failed. [OutOfStock: mug is out of stock; OrderNotFound: order 7 was not found]\".",
                Fails(() => result.Should().Fail().WithError<PaymentDeclined>("the card was {0}", "refused")));
        }
    }

    public class OnValidation
    {
        [Fact]
        public void Untyped_pass_and_fail_cases()
        {
            Validation<int> validation = Validation.Fail<int>([new Required("sku"), new TooMany("quantity", 10)]);
            validation.Should().Fail().Which.Should().HaveCount(2);
            validation.Should().FailWith<TooMany>().Which.Field.Should().Be("quantity");
            Assert.Equal(
                "Expected validation to succeed because the form is complete, but it failed with {\"Required: sku is required\", \"TooMany: quantity is more than 10\"}.",
                Fails(() => validation.Should().Succeed("the form is complete")));
            Assert.Equal(
                "Expected validation to fail with OrderNotFound, but it failed with {\"Required: sku is required\", \"TooMany: quantity is more than 10\"}.",
                Fails(() => validation.Should().FailWith<OrderNotFound>()));

            validation = Validation.Ok(3);
            validation.Should().Succeed().Which.Should().Be(3);
            Assert.Equal("Expected validation to fail, but it succeeded with a value of type Int32.", Fails(() => validation.Should().Fail()));
            Assert.Equal("Expected validation to fail with Required, but it succeeded with a value of type Int32.", Fails(() => validation.Should().FailWith<Required>()));
        }

        [Fact]
        public void Typed_pass_and_fail_cases_and_the_zero_value()
        {
            Validation<int, OrderError> validation = Validation.Fail<int, OrderError>([new OutOfStock("mug"), NotFound]);
            validation.Should().Fail("both checks fail").Which.Should().Equal(new OutOfStock("mug"), NotFound);
            validation.Should().FailWith<OrderNotFound>("it is gone").Which.Should().BeSameAs(NotFound);

            validation = Validation.Ok<int, OrderError>(3);
            validation.Should().Succeed("stock is there").Which.Should().Be(3);

            validation = default;
            Assert.Equal(
                "Expected validation to succeed, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => validation.Should().Succeed()));
            Assert.Equal(
                "Expected validation to fail, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => validation.Should().Fail()));
            Assert.Equal(
                "Expected validation to fail with OutOfStock, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => validation.Should().FailWith<OutOfStock>()));
        }
    }

    public class OnOption
    {
        [Fact]
        public void BeSome_and_BeNone()
        {
            Option<int> option = 4;
            option.Should().BeSome().Which.Should().Be(4);
            Assert.Equal("Expected option to be None because nothing was stored, but it was Some.", Fails(() => option.Should().BeNone("nothing was stored")));

            option = Option<int>.None;
            option.Should().BeNone().And.BeNone();
            Assert.Equal("Expected option to be Some, but it was None.", Fails(() => option.Should().BeSome()));
        }
    }

    public class OnGeneratedShapes
    {
        [Fact]
        public void A_result_shape_and_a_gathering_shape()
        {
            PlaceOrderResult result = new Order(1, "tea", 2);
            result.Should().Succeed().Which.Sku.Should().Be("tea");
            result = new PaymentDeclined(Sentinels.CardHolder);
            result.Should().FailWith<PaymentDeclined>().Which.CardHolder.Should().Be(Sentinels.CardHolder);
            Assert.Equal(
                "Expected result to succeed, but it failed with \"PaymentDeclined: the payment was declined\".",
                Fails(() => result.Should().Succeed()));

            CheckOrderResult validation = CheckOrderResult.Fail([new OutOfStock("mug"), new PaymentDeclined(Sentinels.CardHolder)]);
            validation.Should().FailWith<OutOfStock>().Which.Sku.Should().Be("mug");
            Assert.Equal(
                "Expected validation to fail with OrderNotFound, but it failed with {\"OutOfStock: mug is out of stock\", \"PaymentDeclined: the payment was declined\"}.",
                Fails(() => validation.Should().FailWith<OrderNotFound>()));
        }

        [Fact]
        public void A_null_shape_throws_ArgumentNullException()
        {
            IResultShape<Order, OrderError> result = null!;
            IValidationShape<Order, OrderError> validation = null!;
            Assert.Equal("shape", Assert.Throws<ArgumentNullException>(() => result.Should()).ParamName);
            Assert.Equal("shape", Assert.Throws<ArgumentNullException>(() => validation.Should()).ParamName);
        }

#if NET11_0_OR_GREATER
        [Fact]
        public void A_union_shape_finds_its_case_and_reads_the_held_error()
        {
            CheckoutResult result = new PaymentDeclined(Sentinels.CardHolder);
            result.Should().Fail().WithMessage("the payment was *").WithError<PaymentDeclined>().Which.Should().BeOfType<PaymentDeclined>();
            result.Should().FailWith<PaymentDeclined>().Which.CardHolder.Should().Be(Sentinels.CardHolder);
            Assert.Equal(
                "Expected result to fail with GatewayTimeout, but it failed with \"PaymentDeclined: the payment was declined\".",
                Fails(() => result.Should().FailWith<GatewayTimeout>()));
        }
#endif
    }

    public class InAnAssertionScope
    {
        [Fact]
        public void Which_continues_with_the_librarys_own_assertions()
        {
            // The subject's name comes from the test's source line when the library can read it (not in a CI build, whose
            // paths are mapped), so only the rest of the message is pinned. AwesomeAssertions adds the ".Value" postfix.
            Result<int> result = 5.Ok();
            var message = Fails(() => result.Should().Succeed().Which.Should().Be(6));
#if RESULTFUL_FLUENTASSERTIONS
            Assert.EndsWith(" to be 6, but found 5.", message, StringComparison.Ordinal);
#else
            Assert.EndsWith(".Value to be 6, but found 5.", message, StringComparison.Ordinal);
#endif
        }

        [Fact]
        public void A_follow_up_on_Which_after_a_failure_in_a_scope_is_reported_only_by_FluentAssertions_7()
        {
            Result<int> result = Boom.Fail<int>();
            var message = Fails(() =>
            {
                using var scope = new AssertionScope();
                result.Should().Succeed().Which.Should().Be(5);
            });
            Result<int> ok = 5.Ok();
            var second = Fails(() =>
            {
                using var scope = new AssertionScope();
                ok.Should().FailWith<OrderNotFound>().Which.Should().NotBeNull();
            });

            const string Failed = "Expected result to succeed, but it failed with \"MessageError: boom\".";
#if RESULTFUL_FLUENTASSERTIONS
            // FluentAssertions 7's AndWhichConstraint takes no scope, so the follow-up runs on the default subject.
            Assert.StartsWith(Failed + Environment.NewLine + "Expected ", message, StringComparison.Ordinal);
            Assert.EndsWith(" to be 5, but found 0 (difference of -5)." + Environment.NewLine, message, StringComparison.Ordinal);
            Assert.EndsWith(" not to be <null>." + Environment.NewLine, second, StringComparison.Ordinal);
#else
            // AwesomeAssertions carries the failed chain into Which, so the follow-up is skipped.
            Assert.Equal(Failed + Environment.NewLine, message);
            Assert.EndsWith(" to fail with OrderNotFound, but it succeeded with a value of type Int32." + Environment.NewLine, second, StringComparison.Ordinal);
            Assert.Single(second.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
#endif
        }

        [Fact]
        public void Two_failing_assertions_are_both_reported()
        {
            Result<int> result = Boom.Fail<int>();
            Option<int> option = Option<int>.None;
            var message = Fails(() =>
            {
                using var scope = new AssertionScope();
                result.Should().Succeed();
                option.Should().BeSome();
            });
            Assert.Equal(
                "Expected result to succeed, but it failed with \"MessageError: boom\"." + Environment.NewLine + "Expected option to be Some, but it was None." + Environment.NewLine,
                message);
        }

        [Fact]
        public void Assertions_chained_on_a_failed_one_add_nothing()
        {
            Result<int> result = 5.Ok();
            var message = Fails(() =>
            {
                using var scope = new AssertionScope();
                result.Should().Fail().WithMessage("x").WithContext("y").WithCause<DbTimeout>().WithError<DbTimeout>();
                result.Should().FailWith<OrderNotFound>().WithMessage("x");
            });
            Assert.Equal(
                "Expected result to fail, but it succeeded with a value of type Int32." + Environment.NewLine + "Expected result to fail with OrderNotFound, but it succeeded with a value of type Int32." + Environment.NewLine,
                message);
        }
    }

    public class Laws
    {
        private static bool Throws(Action assertion) => Record.Exception(assertion) is not null;

        [Fact]
        public void Succeed_and_Fail_throw_exactly_when_the_core_says_the_other_case()
        {
            Gens.Results.Sample(result =>
            {
                Assert.Equal(result.IsFailure, Throws(() => result.Should().Succeed()));
                Assert.Equal(result.IsSuccess, Throws(() => result.Should().Fail()));
                if (result.IsSuccess)
                {
                    result.Should().Succeed().Which.Should().Be(result.Value);
                }
                else
                {
                    result.Should().Fail().Which.Should().BeSameAs(result.Error);
                }
            });
            Gens.Validations.Sample(validation => Assert.Equal(validation.IsFailure, Throws(() => validation.Should().Succeed())));
            Gens.Shapes.Sample(result => Assert.Equal(result.AsResult().IsSuccess, Throws(() => result.Should().Fail())));
        }

        [Fact]
        public void FailWith_throws_exactly_when_TryFind_finds_nothing()
        {
            Gens.Results.Sample(result =>
            {
                var found = result.IsFailure && result.Error.TryFind(out OrderNotFound? _);
                Assert.Equal(!found, Throws(() => result.Should().FailWith<OrderNotFound>()));
            });
            Gens.TypedValidations.Sample(validation =>
            {
                var found = validation.IsFailure && validation.Errors.Any(e => e.TryFind(out PaymentDeclined? _));
                Assert.Equal(!found, Throws(() => validation.Should().FailWith<PaymentDeclined>()));
            });
        }

        [Fact]
        public void No_message_contains_a_leafs_own_properties()
        {
            Gens.Results.Sample(result =>
            {
                var message = Record.Exception(() =>
                {
                    using var scope = new AssertionScope();
                    result.Should().Succeed();
                    result.Should().FailWith<GatewayStub>();
                    result.Should().Fail().WithMessage("never").WithContext("never").WithCause<GatewayStub>();
                })?.Message ?? string.Empty;
                Assert.DoesNotContain(Sentinels.CardHolder, message, StringComparison.Ordinal);
            });
            Gens.TypedValidations.Sample(validation =>
            {
                var message = Record.Exception(() => validation.Should().Succeed())?.Message ?? string.Empty;
                Assert.DoesNotContain(Sentinels.CardHolder, message, StringComparison.Ordinal);
            });
        }

        private sealed record GatewayStub() : Error("never");
    }
}
