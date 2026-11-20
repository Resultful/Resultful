using Microsoft.Extensions.Logging;

namespace Resultful.Diagnostics.Tests;

public sealed class LoggingTests
{
    [Fact]
    public void A_failure_logs_one_entry_with_its_type_and_message_and_not_its_data()
    {
        var logger = new ListLogger();
        var failed = Result.Fail<int>(new CardDeclined("4111111111111111"));

        var returned = failed.LogFailure(logger, LogLevel.Warning);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("Result failed with CardDeclined: the card was declined", entry.Text);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(new EventId(1, "ResultFailure"), entry.EventId);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain("4111", entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(entry.State, kv => kv.Value?.ToString()?.Contains("4111", StringComparison.Ordinal) == true);
        Assert.Equal(failed, returned);
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
    public void A_generic_leaf_is_named_with_its_type_arguments()
    {
        var logger = new ListLogger();

        Result.Fail<int>(new Wrapper<List<string>>([])).LogFailure(logger, LogLevel.Warning);

        Assert.StartsWith("Result failed with Wrapper<List<String>>: wrapped", Assert.Single(logger.Entries).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_success_and_the_zero_value_log_nothing()
    {
        var logger = new ListLogger();

        Result.Ok(1).LogFailure(logger, LogLevel.Warning);
        default(Result<int>).LogFailure(logger, LogLevel.Warning);
        Result.Ok<int, CardDeclined>(1).LogFailure(logger, LogLevel.Warning);
        default(Result<int, CardDeclined>).LogFailure(logger, LogLevel.Warning);
        Validation.Ok(1).LogFailure(logger, LogLevel.Warning);
        default(Validation<int>).LogFailure(logger, LogLevel.Warning);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void A_disabled_level_logs_nothing()
    {
        var logger = new ListLogger(LogLevel.Error);

        Result.Fail<int>(new CardDeclined("4111")).LogFailure(logger, LogLevel.Warning);

        Assert.Empty(logger.Entries);
        Assert.Equal(1, logger.EnabledChecks);
    }

    [Fact]
    public void LogLevel_None_logs_nothing_even_when_the_logger_says_yes()
    {
        var logger = new EverythingLogger();

        Result.Fail<int>(new CardDeclined("4111")).LogFailure(logger, LogLevel.None);
        Result.Fail<int, CardDeclined>(new CardDeclined("4111")).LogFailure(logger, LogLevel.None);
        Validation.Fail<int>(new CardDeclined("4111")).LogFailure(logger, LogLevel.None);

        Assert.Equal(0, logger.Count);
    }

    [Fact]
    public void The_typed_overload_logs_the_typed_error()
    {
        var logger = new ListLogger();
        var failed = Result.Fail<int, CardDeclined>(new CardDeclined("4111"));

        var returned = failed.LogFailure(logger, LogLevel.Warning);

        Assert.Equal("Result failed with CardDeclined: the card was declined", Assert.Single(logger.Entries).Text);
        Assert.Equal(failed, returned);
    }

    [Fact]
    public void A_failed_validation_logs_one_entry_whose_causes_are_its_errors_in_order()
    {
        var logger = new ListLogger();
        var failed = Validation.Fail<int>([new CardDeclined("4111"), new GatewayTimeout("gw")]);

        var returned = failed.LogFailure(logger, LogLevel.Warning);

        var entry = Assert.Single(logger.Entries);
        var expected = new ValidationFailed<Error>(failed.Errors).Message;
        Assert.Equal($"Result failed with ValidationFailed<Error>: {expected} Causes: CardDeclined: the card was declined; GatewayTimeout: the gateway timed out", entry.Text);
        Assert.Equal(failed, returned);
    }

    [Fact]
    public void A_null_logger_throws()
    {
        Assert.Throws<ArgumentNullException>("logger", () => Result.Ok(1).LogFailure(null!, LogLevel.Warning));
        Assert.Throws<ArgumentNullException>("logger", () => Result.Ok<int, CardDeclined>(1).LogFailure(null!, LogLevel.Warning));
        Assert.Throws<ArgumentNullException>("logger", () => Validation.Ok(1).LogFailure(null!, LogLevel.Warning));
    }
}
