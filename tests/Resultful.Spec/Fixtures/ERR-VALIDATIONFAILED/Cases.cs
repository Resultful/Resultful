// Decision 14 (06): ValidationFailed carries many errors through a Result; Inner and TryFind see each of them.
using System.Linq;
using Resultful;
using Resultful.Spec.Support;

public sealed record Required(string Field) : Error($"{Field} is required");

public static class Cases
{
    [SpecCase]
    public static void Inner_lists_the_errors_and_TryFind_searches_them()
    {
        Error e = new ValidationFailed<Required>(new[] { new Required("name"), new Required("email") });
        Check.SequenceEqual(new[] { "name is required", "email is required" }, e.Inner().Select(i => i.Message), "Inner");
        Check.True(e.TryFind<Required>(out var first) && first.Field == "name", "TryFind finds the first");
        Check.Equal(3, e.Causes().Count(), "this, then each inner error");
        Check.Equal("Validation failed.", e.Message, "Message");
    }

    [SpecCase]
    public static void An_empty_list_is_rejected()
    {
        Check.Throws<System.ArgumentException>(() => _ = new ValidationFailed<Required>(System.Array.Empty<Required>()), "empty");
    }
}
