using Microsoft.Extensions.Logging;

namespace Resultful.Diagnostics.Tests;

public sealed class LoggingTests
{
    const string Sentinel = "SENTINEL-4111111111111111";

    [Fact]
    public void A_failure_logs_one_entry_with_its_type_and_message()
    {
        var logger = new ListLogger();
        var failed = Result.Fail<int>(new CardDeclined(Sentinel));

        var returned = failed.LogFailure(logger, LogLevel.Warning);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("Result failed with CardDeclined: the card was declined", entry.Text);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(new EventId(1, "ResultFailure"), entry.EventId);
        Assert.Null(entry.Exception);
        Assert.Equal(failed, returned);
    }

    [Fact]
    public void A_leaf_sentinel_reaches_neither_the_text_nor_the_structured_state()
    {
        var logger = new ListLogger();
        var error = new CardDeclined(Sentinel) { Cause = new GatewayTimeout(Sentinel) { Cause = new Wrapper<string>(Sentinel) } }.WithContext("charging");

        Result.Fail<int>(error).LogFailure(logger, LogLevel.Error);
        Result.Fail<int, CardDeclined>(new CardDeclined(Sentinel)).LogFailure(logger, LogLevel.Error);
        Validation.Fail<int>([new CardDeclined(Sentinel), new Wrapper<string>(Sentinel)]).LogFailure(logger, LogLevel.Error);
        Result.Fail<int, PaymentError>(new PaymentError(new CardDeclined(Sentinel))).LogFailure(logger, LogLevel.Error);

        Assert.Equal(4, logger.Entries.Count);
        Assert.All(logger.AllText(), text => Assert.DoesNotContain("SENTINEL", text, StringComparison.Ordinal));
    }

    [Fact]
    public void Context_and_the_cause_chain_are_logged_by_type_and_message()
    {
        var logger = new ListLogger();
        var error = new CardDeclined("4111") { Cause = new GatewayTimeout("acme-secret-gw") }.WithContext("charging").WithContext("placing order 7");

        Result.Fail<int>(error).LogFailure(logger, LogLevel.Error);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("Result failed with CardDeclined: the card was declined Context: charging / placing order 7 Causes: GatewayTimeout: the gateway timed out", entry.Text);
        Assert.DoesNotContain("acme-secret-gw", entry.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_state_carries_named_properties_for_structured_sinks()
    {
        var logger = new ListLogger();
        var error = new CardDeclined("4111") { Cause = new GatewayTimeout("gw") }.WithContext("charging");

        Result.Fail<int>(error).LogFailure(logger, LogLevel.Information);

        var state = Assert.Single(logger.Entries).State.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(["ErrorType", "ErrorMessage", "ErrorContext", "ErrorCauses", "{OriginalFormat}"], state.Keys);
        Assert.Equal("CardDeclined", state["ErrorType"]);
        Assert.Equal("the card was declined", state["ErrorMessage"]);
        Assert.Equal(["charging"], Assert.IsAssignableFrom<IReadOnlyList<string>>(state["ErrorContext"]));
        Assert.Equal(["GatewayTimeout: the gateway timed out"], Assert.IsAssignableFrom<IReadOnlyList<string>>(state["ErrorCauses"]));
        Assert.Equal("Result failed with {ErrorType}: {ErrorMessage} Context: {ErrorContext} Causes: {ErrorCauses}", state["{OriginalFormat}"]);
    }

    [Fact]
    public void The_template_names_only_the_parts_the_line_has()
    {
        static (string Text, object? Format, int Count) Log(Error error)
        {
            var logger = new ListLogger();
            Result.Fail<int>(error).LogFailure(logger, LogLevel.Warning);
            var entry = Assert.Single(logger.Entries);
            return (entry.Text, entry.State.Single(kv => kv.Key == "{OriginalFormat}").Value, entry.State.Count);
        }

        var plain = Log(new CardDeclined("x"));
        var noted = Log(new CardDeclined("x").WithContext("charging"));
        var caused = Log(new CardDeclined("x") { Cause = new GatewayTimeout("gw") });

        Assert.Equal(("Result failed with CardDeclined: the card was declined", (object?)"Result failed with {ErrorType}: {ErrorMessage}", 5), plain);
        Assert.Equal(("Result failed with CardDeclined: the card was declined Context: charging", (object?)"Result failed with {ErrorType}: {ErrorMessage} Context: {ErrorContext}", 5), noted);
        Assert.Equal(("Result failed with CardDeclined: the card was declined Causes: GatewayTimeout: the gateway timed out", (object?)"Result failed with {ErrorType}: {ErrorMessage} Causes: {ErrorCauses}", 5), caused);
    }

    [Fact]
    public void The_state_is_a_read_only_list_with_a_bounded_indexer()
    {
        IReadOnlyList<KeyValuePair<string, object?>>? captured = null;
        var capturing = new CapturingLogger(state => captured = state);

        Result.Fail<int>(new CardDeclined("x")).LogFailure(capturing, LogLevel.Warning);

        Assert.NotNull(captured);
        Assert.Equal(5, captured.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => captured[5]);
        Assert.Throws<ArgumentOutOfRangeException>(() => captured[-1]);
        Assert.Equal(captured.ToList(), ((System.Collections.IEnumerable)captured).Cast<KeyValuePair<string, object?>>().ToList());
    }

    [Fact]
    public void A_generic_leaf_is_named_with_its_type_arguments()
    {
        var logger = new ListLogger();

        Result.Fail<int>(new Wrapper<Dictionary<string, List<int>>>([])).LogFailure(logger, LogLevel.Warning);

        Assert.Equal("Result failed with Wrapper<Dictionary<String, List<Int32>>>: wrapped", Assert.Single(logger.Entries).Text);
    }

    [Fact]
    public void A_success_and_the_zero_value_log_nothing_and_ask_nothing()
    {
        var logger = new ListLogger();

        Result.Ok(1).LogFailure(logger, LogLevel.Warning);
        default(Result<int>).LogFailure(logger, LogLevel.Warning);
        Result.Ok<int, CardDeclined>(1).LogFailure(logger, LogLevel.Warning);
        default(Result<int, CardDeclined>).LogFailure(logger, LogLevel.Warning);
        Validation.Ok(1).LogFailure(logger, LogLevel.Warning);
        default(Validation<int>).LogFailure(logger, LogLevel.Warning);
        Result.Ok<int, PaymentError>(1).LogFailure(logger, LogLevel.Warning);
        default(Result<int, PaymentError>).LogFailure(logger, LogLevel.Warning);

        Assert.Empty(logger.Entries);
        Assert.Equal(0, logger.EnabledChecks);
    }

    [Fact]
    public void A_disabled_level_logs_nothing()
    {
        var logger = new ListLogger(LogLevel.Error);

        Result.Fail<int>(new CardDeclined("4111")).LogFailure(logger, LogLevel.Warning);
        Result.Fail<int, CardDeclined>(new CardDeclined("4111")).LogFailure(logger, LogLevel.Warning);
        Validation.Fail<int>(new CardDeclined("4111")).LogFailure(logger, LogLevel.Warning);
        Result.Fail<int, PaymentError>(new PaymentError(new CardDeclined("4111"))).LogFailure(logger, LogLevel.Warning);

        Assert.Empty(logger.Entries);
        Assert.Equal(4, logger.EnabledChecks);
    }

    [Fact]
    public void LogLevel_None_logs_nothing_even_when_the_logger_says_yes()
    {
        var logger = new EverythingLogger();

        Result.Fail<int>(new CardDeclined("4111")).LogFailure(logger, LogLevel.None);
        Result.Fail<int, CardDeclined>(new CardDeclined("4111")).LogFailure(logger, LogLevel.None);
        Validation.Fail<int>(new CardDeclined("4111")).LogFailure(logger, LogLevel.None);
        Result.Fail<int, PaymentError>(new PaymentError(new CardDeclined("4111"))).LogFailure(logger, LogLevel.None);

        Assert.Equal(0, logger.Count);
    }

    [Fact]
    public void The_typed_overload_logs_the_typed_error()
    {
        var logger = new ListLogger();
        var failed = Result.Fail<int, CardDeclined>(new CardDeclined("4111"));

        var returned = failed.LogFailure(logger, LogLevel.Critical);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(("Result failed with CardDeclined: the card was declined", LogLevel.Critical), (entry.Text, entry.Level));
        Assert.Equal(failed, returned);
    }

    [Fact]
    public void An_error_union_logs_its_leaf()
    {
        var logger = new ListLogger();
        var failed = Result.Fail<int, PaymentError>(new PaymentError(new GatewayTimeout("gw").WithContext("charging")));

        var returned = failed.LogFailure(logger, LogLevel.Warning);

        Assert.Equal("Result failed with GatewayTimeout: the gateway timed out Context: charging", Assert.Single(logger.Entries).Text);
        Assert.Equal(failed, returned);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void A_csharp_union_logs_its_leaf()
    {
        var logger = new ListLogger();
        Result<int, ChargeError> failed = (ChargeError)new CardDeclined(Sentinel);

        failed.LogFailure(logger, LogLevel.Warning);

        Assert.Equal("Result failed with CardDeclined: the card was declined", Assert.Single(logger.Entries).Text);
        Assert.All(logger.AllText(), text => Assert.DoesNotContain("SENTINEL", text, StringComparison.Ordinal));
    }
#endif

    [Fact]
    public void A_failed_validation_logs_one_entry_whose_causes_are_its_errors_in_order()
    {
        var logger = new ListLogger();
        var failed = Validation.Fail<int>([new CardDeclined("4111"), new GatewayTimeout("gw")]);

        var returned = failed.LogFailure(logger, LogLevel.Warning);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("Result failed with ValidationFailed<Error>: Validation failed. Causes: CardDeclined: the card was declined; GatewayTimeout: the gateway timed out", entry.Text);
        Assert.Equal(new EventId(1, "ResultFailure"), entry.EventId);
        Assert.Equal(failed, returned);
    }

    [Fact]
    public void A_null_logger_throws()
    {
        Assert.Throws<ArgumentNullException>("logger", () => Result.Ok(1).LogFailure(null!, LogLevel.Warning));
        Assert.Throws<ArgumentNullException>("logger", () => Result.Ok<int, CardDeclined>(1).LogFailure(null!, LogLevel.Warning));
        Assert.Throws<ArgumentNullException>("logger", () => Validation.Ok(1).LogFailure(null!, LogLevel.Warning));
        Assert.Throws<ArgumentNullException>("logger", () => Result.Ok<int, PaymentError>(1).LogFailure(null!, LogLevel.Warning));
    }

    sealed class CapturingLogger(Action<IReadOnlyList<KeyValuePair<string, object?>>> capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            capture((IReadOnlyList<KeyValuePair<string, object?>>)state!);
    }
}
