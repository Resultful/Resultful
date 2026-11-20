using System.Collections;
using System.Text;

namespace Resultful.Diagnostics;

/// <summary>
/// The structured state of one failure log entry. Each value is also a named property for structured sinks:
/// <c>ErrorType</c>, <c>ErrorMessage</c>, <c>ErrorContext</c> (the notes, innermost first) and <c>ErrorCauses</c>.
/// </summary>
/// <remarks>
/// <see cref="Format"/>, the <c>{OriginalFormat}</c> a structured sink sees, always names all four properties. The
/// rendered line (<see cref="ToString"/>) leaves out the <c>Context</c> and <c>Causes</c> parts when they are empty,
/// so a plain failure reads as one short sentence. The difference is deliberate; the property values are the same.
/// </remarks>
internal sealed class FailureLogState : IReadOnlyList<KeyValuePair<string, object?>>
{
    internal const string Format = "Result failed with {ErrorType}: {ErrorMessage} Context: {ErrorContext} Causes: {ErrorCauses}";

    private readonly string _type;
    private readonly string _message;
    private readonly IReadOnlyList<string> _context;
    private readonly string[] _causes;

    public FailureLogState(Error error)
    {
        _type = ErrorDescription.TypeName(error.GetType());
        _message = error.Message;
        _context = error.Context;
        _causes = ErrorDescription.Causes(error);
    }

    public int Count => 5;

    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new("ErrorType", _type),
        1 => new("ErrorMessage", _message),
        2 => new("ErrorContext", _context),
        3 => new("ErrorCauses", _causes),
        4 => new("{OriginalFormat}", Format),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// The rendered line: <c>Result failed with CardDeclined: the card was declined</c>, then
    /// <c> Context: charging / loading order 7</c> and <c> Causes: Timeout: the gateway timed out; …</c> when there are any.
    /// </summary>
    public override string ToString()
    {
        var text = new StringBuilder("Result failed with ").Append(_type).Append(": ").Append(_message);
        if (_context.Count > 0) text.Append(" Context: ").AppendJoin(" / ", _context);
        if (_causes.Length > 0) text.Append(" Causes: ").AppendJoin("; ", _causes);
        return text.ToString();
    }

    public static string Render(FailureLogState state, Exception? exception) => state.ToString();
}
