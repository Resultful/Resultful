// Quality (06): Error.ToString is sealed in the base: the leaf's type name, Message, Context and the cause chain,
// never the leaf's own properties (they may hold personal data).
using Resultful;
using Resultful.Spec.Support;

public sealed record CardDeclined(string CardNumber) : Error("the card was declined");

public static class Cases
{
    [SpecCase]
    public static void ToString_shows_what_happened_and_hides_the_data()
    {
        var e = new CardDeclined("4111111111111111") { Cause = Error.From("issuer said no") }.WithContext("charging order 9");
        var text = e.ToString();
        Check.True(text.Contains("CardDeclined", System.StringComparison.Ordinal), "type name: " + text);
        Check.True(text.Contains("the card was declined", System.StringComparison.Ordinal), "message: " + text);
        Check.True(text.Contains("charging order 9", System.StringComparison.Ordinal), "context: " + text);
        Check.True(text.Contains("MessageError", System.StringComparison.Ordinal) && text.Contains("issuer said no", System.StringComparison.Ordinal), "cause: " + text);
        Check.False(text.Contains("4111", System.StringComparison.Ordinal), "the leaf's property stays out: " + text);
    }

    [SpecCase]
    public static void ToString_is_sealed_in_the_base()
    {
        var method = typeof(Error).GetMethod(nameof(ToString), System.Type.EmptyTypes)!;
        Check.True(method.IsFinal && method.DeclaringType == typeof(Error), "sealed override on Error");
    }
}
