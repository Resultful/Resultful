using Resultful.Assertions.TestDomain;

namespace Resultful.Assertions.Tests;

// Every assertion's pass and fail case on every receiver, with the failure message pinned. A message names the expected
// and actual case, prints errors with Error.ToString only, and a success value by its runtime type only (decision 18).
public class AssertionTests
{
    private static readonly Error Boom = Error.From("boom");
    private static readonly OrderError NotFound = new OrderNotFound(7);
    private static readonly Error Wrapped = new SaveFailed(13) { Cause = new DbTimeout() }.WithContext("saving");

    private static string Fails(Action assertion) => Assert.Throws<ResultAssertionException>(assertion).Message;

    public class OnResult
    {
        [Fact]
        public void ShouldSucceed_returns_the_value()
        {
            Assert.Equal(5, Result.Ok(5).ShouldSucceed());
            Assert.Equal(5, Result.Ok(5).ShouldSucceed("it is cached"));
        }

        [Fact]
        public void ShouldSucceed_on_a_failure_prints_the_error()
        {
            Assert.Equal(
                """
                Expected Result<Int32> to succeed, but it failed with:
                  SaveFailed: order 13 was not saved (context: saving) ---> DbTimeout: the database timed out
                """,
                Fails(() => Wrapped.Fail<int>().ShouldSucceed()));
        }

        [Fact]
        public void ShouldSucceed_on_the_zero_value_says_so()
        {
            Assert.Equal(
                "Expected Result<Int32> to succeed, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => default(Result<int>).ShouldSucceed()));
        }

        [Theory]
        [InlineData("the order exists", " because the order exists")]
        [InlineData("because the order exists", " because the order exists")]
        [InlineData("  Because it was saved  ", " Because it was saved")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData(null, "")]
        public void Because_is_added_after_the_expectation_once(string? because, string expected)
        {
            Assert.Equal(
                $"Expected Result<Int32> to succeed{expected}, but it failed with:\n  MessageError: boom",
                Fails(() => Boom.Fail<int>().ShouldSucceed(because)));
        }

        [Fact]
        public void ShouldFail_returns_the_error_itself()
        {
            Assert.Same(Boom, Boom.Fail<int>().ShouldFail());
            Assert.Same(Boom, Boom.Fail<int>().ShouldFail("the disk is full"));
        }

        [Fact]
        public void ShouldFail_on_a_success_names_the_values_runtime_type_never_the_value()
        {
            Assert.Equal("Expected Result<Int32> to fail, but it succeeded with a value of type Int32.", Fails(() => Result.Ok(5).ShouldFail()));
            Assert.Equal(
                "Expected Result<Object> to fail because the disk is full, but it succeeded with a value of type Order.",
                Fails(() => Result.Ok<object>(new Order(1, Sentinels.CardHolder, 1)).ShouldFail("the disk is full")));
            Assert.Equal("Expected Result<String> to fail, but it succeeded with null.", Fails(() => Result.Ok<string>(null!).ShouldFail()));
            Assert.Equal(
                "Expected Result<Int32> to fail, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => default(Result<int>).ShouldFail()));
        }
    }

    public class OnTypedResult
    {
        [Fact]
        public void Pass_cases_return_the_value_and_the_error()
        {
            Assert.Equal(5, 5.Ok<int, OrderError>().ShouldSucceed());
            Assert.Equal(5, 5.Ok<int, OrderError>().ShouldSucceed("stock is there"));
            Assert.Same(NotFound, NotFound.Fail<int, OrderError>().ShouldFail());
            Assert.Same(NotFound, NotFound.Fail<int, OrderError>().ShouldFail("it is gone"));
        }

        [Fact]
        public void Fail_cases_name_the_type_with_its_error_type()
        {
            Assert.Equal(
                "Expected Result<Int32, OrderError> to succeed, but it failed with:\n  OrderNotFound: order 7 was not found",
                Fails(() => NotFound.Fail<int, OrderError>().ShouldSucceed()));
            Assert.Equal(
                "Expected Result<Int32, OrderError> to fail, but it succeeded with a value of type Int32.",
                Fails(() => 5.Ok<int, OrderError>().ShouldFail()));
            Assert.Equal(
                "Expected Result<Int32, OrderError> to succeed, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => default(Result<int, OrderError>).ShouldSucceed()));
            Assert.Equal(
                "Expected Result<Int32, OrderError> to fail, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => default(Result<int, OrderError>).ShouldFail()));
        }

        [Fact]
        public void An_error_that_is_not_an_Error_prints_only_its_type()
        {
            var legacy = "card 4111 for Jane".Fail<int, string>();
            Assert.Equal("card 4111 for Jane", legacy.ShouldFail());
            Assert.Equal(
                "Expected Result<Int32, String> to succeed, but it failed with:\n  String (not an Error, so only its type is shown)",
                Fails(() => legacy.ShouldSucceed()));
        }
    }

    public class OnShouldFind
    {
        [Fact]
        public void On_an_error_it_finds_the_leaf_itself_a_cause_or_an_inner_error()
        {
            Result<int> direct = NotFound;
            Assert.Same(NotFound, direct.ShouldFail().ShouldFind<OrderNotFound>());
            Assert.IsType<DbTimeout>(Wrapped.Fail<int>().ShouldFail().ShouldFind<DbTimeout>());

            var inner = new OutOfStock("mug");
            Result<int> gathered = new ValidationFailed<OrderError>([new OrderNotFound(1), inner]);
            Assert.Same(inner, gathered.ShouldFail().ShouldFind<OutOfStock>("the mug is sold out"));

            // A base type finds the first error of it, outer to inner, as Error.TryFind does.
            Assert.IsType<OrderNotFound>(gathered.ShouldFail().ShouldFind<OrderError>());
            Assert.Same(NotFound, NotFound.Fail<int, OrderError>().ShouldFail().ShouldFind<OrderNotFound>());
        }

        [Fact]
        public void On_an_error_a_miss_prints_the_error_not_the_result()
        {
            Assert.Equal(
                """
                Expected the error to be or contain OrderNotFound because order 7 is gone, but it is:
                  SaveFailed: order 13 was not saved (context: saving) ---> DbTimeout: the database timed out
                """,
                Fails(() => Wrapped.ShouldFind<OrderNotFound>("order 7 is gone")));
            Assert.Equal(
                "Expected the error to be or contain OutOfStock, but it is:\n  OrderNotFound: order 7 was not found",
                Fails(() => NotFound.ShouldFind<OutOfStock>()));
        }

        [Fact]
        public void On_a_list_it_searches_each_error_in_order()
        {
            var failed = Validation.Fail<int>([new Required("sku"), new TooMany("quantity", 10)]);
            Assert.Equal("quantity", failed.ShouldFail().ShouldFind<TooMany>().Field);
            Assert.Equal("sku", failed.ShouldFail().ShouldFind<Required>("sku is missing").Field);

            // A list of a hierarchy's errors is an IEnumerable<Error> by covariance.
            var typed = Validation.Fail<int, OrderError>([new OutOfStock("mug"), NotFound]);
            Assert.Same(NotFound, typed.ShouldFail().ShouldFind<OrderNotFound>());
            Assert.Equal("mug", CheckOrderResult.Fail([new OutOfStock("mug")]).ShouldFail().ShouldFind<OutOfStock>().Sku);

            Assert.Equal(
                """
                Expected one of the errors to be or contain OrderNotFound, but they are:
                  Required: sku is required
                  TooMany: quantity is more than 10
                """,
                Fails(() => failed.ShouldFail().ShouldFind<OrderNotFound>()));
            Assert.Equal(
                "Expected the error to be or contain OrderNotFound because it was checked, but it is:\n  Required: sku is required",
                Fails(() => Validation.Fail<int>(new Required("sku")).ShouldFail().ShouldFind<OrderNotFound>("it was checked")));
            Assert.Equal("Expected one of the errors to be or contain OrderNotFound, but there are none.", Fails(() => Array.Empty<Error>().ShouldFind<OrderNotFound>()));
        }

        [Fact]
        public void A_null_receiver_throws_ArgumentNullException()
        {
            Assert.Equal("error", Assert.Throws<ArgumentNullException>(() => ((Error)null!).ShouldFind<MessageError>()).ParamName);
            Assert.Equal("union", Assert.Throws<ArgumentNullException>(() => ((IErrorUnion)null!).ShouldFind<MessageError>()).ParamName);
            Assert.Equal("errors", Assert.Throws<ArgumentNullException>(() => ((IEnumerable<Error>)null!).ShouldFind<MessageError>()).ParamName);
        }
    }

    public class OnValidation
    {
        [Fact]
        public void Untyped_pass_and_fail_cases()
        {
            var failed = Validation.Fail<int>([new Required("sku"), new TooMany("quantity", 10)]);
            Assert.Equal(3, Validation.Ok(3).ShouldSucceed());
            Assert.Equal(3, Validation.Ok(3).ShouldSucceed("the form is complete"));
            Assert.Equal(2, failed.ShouldFail().Count);
            Assert.Equal(2, failed.ShouldFail("the form is empty").Count);

            Assert.Equal(
                """
                Expected Validation<Int32> to succeed because the form is complete, but it failed with 2 errors:
                  Required: sku is required
                  TooMany: quantity is more than 10
                """,
                Fails(() => failed.ShouldSucceed("the form is complete")));
            Assert.Equal(
                "Expected Validation<Int32> to succeed, but it failed with 1 error:\n  Required: sku is required",
                Fails(() => Validation.Fail<int>(new Required("sku")).ShouldSucceed()));
            Assert.Equal("Expected Validation<Int32> to fail, but it succeeded with a value of type Int32.", Fails(() => Validation.Ok(3).ShouldFail()));
            Assert.Equal(
                "Expected Validation<Int32> to succeed, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => default(Validation<int>).ShouldSucceed()));
        }

        [Fact]
        public void Typed_pass_and_fail_cases()
        {
            var failed = Validation.Fail<int, OrderError>([new OutOfStock("mug"), NotFound]);
            Assert.Equal(3, Validation.Ok<int, OrderError>(3).ShouldSucceed());
            Assert.Equal(3, Validation.Ok<int, OrderError>(3).ShouldSucceed("stock is there"));
            Assert.Equal([new OutOfStock("mug"), NotFound], failed.ShouldFail());
            Assert.Equal(2, failed.ShouldFail("both are wrong").Count);

            Assert.Equal(
                "Expected Validation<Int32, OrderError> to succeed, but it failed with 2 errors:\n  OutOfStock: mug is out of stock\n  OrderNotFound: order 7 was not found",
                Fails(() => failed.ShouldSucceed()));
            Assert.Equal(
                "Expected Validation<Int32, OrderError> to fail, but it succeeded with a value of type Int32.",
                Fails(() => Validation.Ok<int, OrderError>(3).ShouldFail()));
            Assert.Equal(
                "Expected Validation<Int32, OrderError> to fail, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => default(Validation<int, OrderError>).ShouldFail()));
        }
    }

    public class OnGeneratedShapes
    {
        private static readonly Order Tea = new(1, "tea", 2);

        [Fact]
        public void A_result_shape_is_asserted_through_the_result_it_wraps_and_named_by_its_own_type()
        {
            PlaceOrderResult placed = Tea;
            PlaceOrderResult declined = new PaymentDeclined(Sentinels.CardHolder);
            Assert.Same(Tea, placed.ShouldSucceed());
            Assert.Same(Tea, placed.ShouldSucceed("tea is in stock"));
            Assert.IsType<PaymentDeclined>(declined.ShouldFail());
            Assert.IsType<PaymentDeclined>(declined.ShouldFail("the card is blocked").ShouldFind<PaymentDeclined>());

            Assert.Equal(
                "Expected PlaceOrderResult to succeed, but it failed with:\n  PaymentDeclined: the payment was declined",
                Fails(() => declined.ShouldSucceed()));
            Assert.Equal("Expected PlaceOrderResult to fail, but it succeeded with a value of type Order.", Fails(() => placed.ShouldFail()));
        }

        [Fact]
        public void A_gathering_shape_is_asserted_through_the_validation_it_wraps()
        {
            CheckOrderResult ok = Tea;
            var failed = CheckOrderResult.Fail([new OutOfStock("mug"), new PaymentDeclined(Sentinels.CardHolder)]);
            Assert.Same(Tea, ok.ShouldSucceed());
            Assert.Same(Tea, ok.ShouldSucceed("all checks pass"));
            Assert.Equal(2, failed.ShouldFail().Count);
            Assert.IsType<PaymentDeclined>(failed.ShouldFail("two checks fail").ShouldFind<PaymentDeclined>());

            Assert.Equal(
                """
                Expected CheckOrderResult to succeed, but it failed with 2 errors:
                  OutOfStock: mug is out of stock
                  PaymentDeclined: the payment was declined
                """,
                Fails(() => failed.ShouldSucceed()));
            Assert.Equal("Expected CheckOrderResult to fail, but it succeeded with a value of type Order.", Fails(() => ok.ShouldFail()));
        }

        [Fact]
        public void The_zero_value_of_a_shape_fails_every_assertion()
        {
            Assert.Equal(
                "Expected PlaceOrderResult to succeed, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => default(PlaceOrderResult).ShouldSucceed()));
            Assert.Equal(
                "Expected CheckOrderResult to fail, but it is the zero value (default), which is neither a success nor a failure.",
                Fails(() => default(CheckOrderResult).ShouldFail()));
        }

        [Fact]
        public void A_null_shape_throws_ArgumentNullException()
        {
            IResultShape<Order, OrderError> result = null!;
            IValidationShape<Order, OrderError> validation = null!;
            Assert.Equal("shape", Assert.Throws<ArgumentNullException>(() => result.ShouldSucceed()).ParamName);
            Assert.Equal("shape", Assert.Throws<ArgumentNullException>(() => result.ShouldFail()).ParamName);
            Assert.Equal("shape", Assert.Throws<ArgumentNullException>(() => validation.ShouldSucceed()).ParamName);
            Assert.Equal("shape", Assert.Throws<ArgumentNullException>(() => validation.ShouldFail()).ParamName);
        }
    }

#if NET11_0_OR_GREATER
    public class OnErrorUnions
    {
        [Fact]
        public void A_union_shape_fails_with_the_union_and_ShouldFind_reads_the_held_error()
        {
            CheckoutResult declined = new PaymentDeclined(Sentinels.CardHolder);
            CheckoutResult paid = new Receipt(1, "R-1");

            Assert.Equal("R-1", paid.ShouldSucceed().Code);
            CheckoutError union = declined.ShouldFail();
            Assert.IsType<PaymentDeclined>(union.Error);
            Assert.Equal(Sentinels.CardHolder, declined.ShouldFail().ShouldFind<PaymentDeclined>().CardHolder);

            Assert.Equal(
                "Expected CheckoutResult to succeed, but it failed with:\n  PaymentDeclined: the payment was declined",
                Fails(() => declined.ShouldSucceed()));
            Assert.Equal(
                "Expected the error to be or contain GatewayTimeout, but it is:\n  PaymentDeclined: the payment was declined",
                Fails(() => declined.ShouldFail().ShouldFind<GatewayTimeout>()));
            Assert.Equal("Expected CheckoutResult to fail, but it succeeded with a value of type Receipt.", Fails(() => paid.ShouldFail()));
        }

        [Fact]
        public void A_bare_result_over_a_union_works_the_same()
        {
            var result = ((CheckoutError)new GatewayTimeout()).Fail<Receipt, CheckoutError>();
            Assert.IsType<GatewayTimeout>(result.ShouldFail().ShouldFind<GatewayTimeout>("the gateway is down"));
            Assert.Equal(
                "Expected Result<Receipt, CheckoutError> to succeed, but it failed with:\n  GatewayTimeout: the payment gateway timed out",
                Fails(() => result.ShouldSucceed()));
        }

        [Fact]
        public void A_list_of_union_structs_is_searched_through_each_held_error()
        {
            // IReadOnlyList<CheckoutError> is no IEnumerable<Error> (the union is a struct): select the held errors.
            var errors = new[] { (CheckoutError)new OrderNotFound(3), (CheckoutError)new GatewayTimeout() };
            Assert.IsType<GatewayTimeout>(errors.Select(u => u.Error).ShouldFind<GatewayTimeout>());
        }
    }
#endif

    public class OnOption
    {
        [Fact]
        public void ShouldBeSome_and_ShouldBeNone()
        {
            Assert.Equal(4, ((Option<int>)4).ShouldBeSome());
            Assert.Equal(4, ((Option<int>)4).ShouldBeSome("it was stored"));
            Option<int>.None.ShouldBeNone();
            Option<int>.None.ShouldBeNone("nothing was stored");
            default(Option<int>).ShouldBeNone();

            Assert.Equal("Expected Option<Int32> to be Some, but it was None.", Fails(() => Option<int>.None.ShouldBeSome()));
            Assert.Equal("Expected Option<Int32> to be Some because it was stored, but it was None.", Fails(() => Option<int>.None.ShouldBeSome("it was stored")));
            Assert.Equal("Expected Option<String> to be None, but it was Some.", Fails(() => ((Option<string>)Sentinels.CardHolder).ShouldBeNone()));
            Assert.Equal("Expected Option<Int32> to be None because nothing was stored, but it was Some.", Fails(() => ((Option<int>)4).ShouldBeNone("nothing was stored")));
        }
    }

    public class OnResultInspection
    {
        [Fact]
        public void DescribeError_prints_ToString_the_held_error_or_only_a_type()
        {
            Assert.Equal(Wrapped.ToString(), ResultInspection.DescribeError(Wrapped));
            Assert.Equal("null", ResultInspection.DescribeError<Error?>(null));
            Assert.Equal("Int32 (not an Error, so only its type is shown)", ResultInspection.DescribeError(4111));
            Assert.Equal("String (not an Error, so only its type is shown)", ResultInspection.DescribeError(Sentinels.CardHolder));
        }

        [Fact]
        public void DescribeSuccess_names_the_runtime_type_only()
        {
            Assert.Equal("it succeeded with a value of type Order.", ResultInspection.DescribeSuccess(new Order(1, Sentinels.CardHolder, 1)));
            Assert.Equal("it succeeded with a value of type List<Int32>.", ResultInspection.DescribeSuccess(new List<int>()));
            Assert.Equal("it succeeded with null.", ResultInspection.DescribeSuccess(null));
        }

        [Fact]
        public void AsError_unwraps_an_Error_and_nothing_else()
        {
            Assert.Same(Boom, ResultInspection.AsError(Boom));
            Assert.Null(ResultInspection.AsError("text"));
            Assert.Null(ResultInspection.AsError<Error?>(null));
#if NET11_0_OR_GREATER
            var timeout = new GatewayTimeout();
            Assert.Same(timeout, ResultInspection.AsError((CheckoutError)timeout));
#endif
        }

        [Fact]
        public void TryFindError_searches_like_TryFind_and_TryFindInErrors_in_order()
        {
            Assert.True(ResultInspection.TryFindError(Wrapped, out DbTimeout? timeout));
            Assert.IsType<DbTimeout>(timeout);
            Assert.False(ResultInspection.TryFindError(Wrapped, out OrderNotFound? none));
            Assert.Null(none);
            Assert.False(ResultInspection.TryFindError("text", out MessageError? _));

            var second = new OrderNotFound(2);
            IEnumerable<Error> errors = [Boom, Wrapped, second, new OrderNotFound(3)];
            Assert.True(ResultInspection.TryFindInErrors(errors, out OrderNotFound? first));
            Assert.Same(second, first);
            Assert.False(ResultInspection.TryFindInErrors(errors, out OutOfStock? _));
            Assert.Throws<ArgumentNullException>(() => ResultInspection.TryFindInErrors<Error, Error>(null!, out _));
        }

        [Fact]
        public void TypeName_writes_generic_arguments_as_CSharp_does()
        {
            Assert.Equal("Dictionary<String, List<Int32>>", ResultInspection.TypeName(typeof(Dictionary<string, List<int>>)));
            Assert.Equal("ValidationFailed<OrderError>", ResultInspection.TypeName(typeof(ValidationFailed<OrderError>)));
            Assert.Equal("Int32", ResultInspection.TypeName(typeof(int)));
            Assert.Throws<ArgumentNullException>(() => ResultInspection.TypeName(null!));
        }

        [Fact]
        public void ResultAssertionException_has_the_standard_constructors()
        {
            var inner = new InvalidOperationException("inner");
            Assert.NotNull(new ResultAssertionException().Message);
            Assert.Equal("m", new ResultAssertionException("m").Message);
            Assert.Same(inner, new ResultAssertionException("m", inner).InnerException);
            Assert.IsAssignableFrom<Exception>(new ResultAssertionException());
        }
    }
}
