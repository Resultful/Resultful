using System.Diagnostics;
using CsCheck;
using Microsoft.Extensions.Logging;

namespace Resultful.Diagnostics.Tests;

public sealed class Laws
{
    // Messages and notes are letters and spaces; a leaf's own data is digits behind a marker, so it can't occur by chance.
    static readonly Gen<string> Text = Gen.String[Gen.Char["abcdefghij "], 0, 12];
    static readonly Gen<string> Secret = Gen.String[Gen.Char["0123456789"], 1, 12].Select(s => "#" + s);

    sealed record Leaf(string Secret, string Text) : Error(Text);

    static readonly Gen<Error> Errors = Gen.Recursive<Error>((depth, self) =>
        Gen.Select(Secret, Text, Text.Array[0, 3], depth < 3 ? self.Null() : Gen.Const((Error?)null))
            .Select(Error (secret, message, notes, cause) =>
            {
                Error error = new Leaf(secret, message) { Cause = cause };
                foreach (var note in notes) error = error.WithContext(note);
                return error;
            }));

    static readonly Gen<LogLevel> Levels = Gen.Enum<LogLevel>().Where(l => l != LogLevel.None);

    static IEnumerable<Leaf> Leaves(Error error) => error.Causes().OfType<Leaf>();

    [Fact]
    public void A_logged_failure_has_every_message_and_note_and_no_leaf_data()
    {
        Gen.Select(Errors, Levels).Sample((error, level) =>
        {
            var logger = new ListLogger();
            Result.Fail<int>(error).LogFailure(logger, level);
            var entry = logger.Entries.Single();
            return entry.Level == level
                && error.Causes().All(e => entry.Text.Contains(e.Message, StringComparison.Ordinal))
                && error.Context.All(n => entry.Text.Contains(n, StringComparison.Ordinal))
                && Leaves(error).All(l => !entry.Text.Contains(l.Secret, StringComparison.Ordinal))
                && logger.AllText().All(t => !t.Contains('#', StringComparison.Ordinal));
        });
    }

    [Fact]
    public void A_failed_validation_logs_every_error_as_a_cause_in_order()
    {
        Errors.List[1, 5].Sample(errors =>
        {
            var logger = new ListLogger();
            Validation.Fail<int>(errors).LogFailure(logger, LogLevel.Warning);
            var causes = (IReadOnlyList<string>)logger.Entries.Single().State.Single(kv => kv.Key == "ErrorCauses").Value!;
            var expected = errors.SelectMany(e => e.Causes()).Select(e => "Leaf: " + e.Message);
            return causes.SequenceEqual(expected) && logger.AllText().All(t => !t.Contains('#', StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Hooks_return_the_result_unchanged()
    {
        Gen.Select(Errors, Gen.Bool, Gen.Int).Sample((error, ok, value) =>
        {
            var result = ok ? Result.Ok(value) : Result.Fail<int>(error);
            var typed = ok ? Result.Ok<int, Error>(value) : Result.Fail<int, Error>(error);
            var union = ok ? Result.Ok<int, PaymentError>(value) : Result.Fail<int, PaymentError>(new PaymentError(error));
            var validation = ok ? Validation.Ok(value) : Validation.Fail<int>(error);
            using var activity = new Activity("law").Start();
            var logger = new ListLogger();
            return result.LogFailure(logger, LogLevel.Warning).Equals(result) && result.RecordFailure(activity).Equals(result)
                && typed.LogFailure(logger, LogLevel.Warning).Equals(typed) && typed.RecordFailure(activity).Equals(typed)
                && union.LogFailure(logger, LogLevel.Warning).Equals(union) && union.RecordFailure(activity).Equals(union)
                && validation.LogFailure(logger, LogLevel.Warning).Equals(validation) && validation.RecordFailure(activity).Equals(validation);
        });
    }

    [Fact]
    public void A_hook_logs_and_tags_exactly_when_the_result_failed_and_agrees_with_Match()
    {
        Gen.Select(Errors, Gen.Bool, Levels).Sample((error, ok, level) =>
        {
            var result = ok ? Result.Ok(1) : Result.Fail<int>(error);
            using var activity = new Activity("law").Start();
            var logger = new ListLogger();
            result.LogFailure(logger, level).RecordFailure(activity);
            var expectedTag = result.Match(_ => (string?)null, e => e.GetType().Name);
            return logger.Entries.Count == (result.IsFailure ? 1 : 0)
                && (string?)activity.GetTagItem(ResultfulDiagnostics.ErrorTypeTag) == expectedTag
                && activity.Status == (result.IsFailure ? ActivityStatusCode.Error : ActivityStatusCode.Unset)
                && activity.TagObjects.Count() == (result.IsFailure ? 1 : 0);
        });
    }

    [Fact]
    public void An_error_union_logs_and_tags_as_its_leaf_does()
    {
        Gen.Select(Errors, Levels).Sample((error, level) =>
        {
            var (direct, viaUnion) = (new ListLogger(), new ListLogger());
            Result.Fail<int>(error).LogFailure(direct, level);
            Result.Fail<int, PaymentError>(new PaymentError(error)).LogFailure(viaUnion, level);
            using var a = new Activity("a").Start();
            Result.Fail<int>(error).RecordFailure(a);
            using var b = new Activity("b").Start();
            Result.Fail<int, PaymentError>(new PaymentError(error)).RecordFailure(b);
            return direct.Entries.Single().Text == viaUnion.Entries.Single().Text
                && Equals(a.GetTagItem(ResultfulDiagnostics.ErrorTypeTag), b.GetTagItem(ResultfulDiagnostics.ErrorTypeTag));
        });
    }
}
