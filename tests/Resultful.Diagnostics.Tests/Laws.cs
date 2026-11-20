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

    static IEnumerable<Leaf> Leaves(Error error) => error.Causes().OfType<Leaf>();

    [Fact]
    public void A_logged_failure_has_every_message_and_note_and_no_leaf_data()
    {
        Gen.Select(Errors, Gen.Enum<LogLevel>().Where(l => l != LogLevel.None)).Sample((error, level) =>
        {
            var logger = new ListLogger();
            Result.Fail<int>(error).LogFailure(logger, level);
            var entry = logger.Entries.Single();
            return entry.Level == level
                && error.Causes().All(e => entry.Text.Contains(e.Message, StringComparison.Ordinal))
                && error.Context.All(n => entry.Text.Contains(n, StringComparison.Ordinal))
                && Leaves(error).All(l => !entry.Text.Contains(l.Secret, StringComparison.Ordinal))
                && !entry.State.Any(kv => kv.Value is string s && s.Contains('#', StringComparison.Ordinal)
                    || kv.Value is IEnumerable<string> list && list.Any(x => x.Contains('#', StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void Hooks_return_the_result_unchanged()
    {
        Gen.Select(Errors, Gen.Bool, Gen.Int).Sample((error, ok, value) =>
        {
            var result = ok ? Result.Ok(value) : Result.Fail<int>(error);
            using var activity = new Activity("law").Start();
            return result.LogFailure(new ListLogger(), LogLevel.Warning).Equals(result) && result.RecordFailure(activity).Equals(result);
        });
    }

    [Fact]
    public void An_activity_is_tagged_exactly_when_the_result_failed()
    {
        Gen.Select(Errors, Gen.Bool).Sample((error, ok) =>
        {
            using var activity = new Activity("law").Start();
            (ok ? Result.Ok(1) : Result.Fail<int>(error)).RecordFailure(activity);
            return ok
                ? activity.Status == ActivityStatusCode.Unset && !activity.TagObjects.Any()
                : activity.Status == ActivityStatusCode.Error && (string?)activity.GetTagItem(ResultfulDiagnostics.ErrorTypeTag) == "Leaf";
        });
    }
}
