using System.Collections;
using System.Text;

namespace Resultful.Diagnostics;

/// <summary>
/// The structured state of one failure log entry. Each value is also a named property for structured sinks:
/// <c>ErrorType</c>, <c>ErrorMessage</c>, <c>ErrorContext</c> (the notes, innermost first) and <c>ErrorCauses</c>.
/// </summary>
/// <remarks>
/// The rendered line (<see cref="ToString"/>) and the <c>{OriginalFormat}</c> both leave out the <c>Context</c> and
/// <c>Causes</c> parts when they are empty, so a plain failure reads as one short sentence and groups under one template.
/// </remarks>
internal sealed class FailureLogState : IReadOnlyList<KeyValuePair<string, object?>>
{
    // The {OriginalFormat} a structured sink sees names only the parts the rendered line has: an empty context or
    // cause list is left out of both. The named properties are always there.
    internal const string Format = "Result failed with {ErrorType}: {ErrorMessage}";
    internal const string ContextPart = " Context: {ErrorContext}";
    internal const string CausesPart = " Causes: {ErrorCauses}";
    private static readonly string[] Formats = [Format, Format + ContextPart, Format + CausesPart, Format + ContextPart + CausesPart];

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
        4 => new("{OriginalFormat}", Formats[(_context.Count > 0 ? 1 : 0) + (_causes.Length > 0 ? 2 : 0)]),
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
