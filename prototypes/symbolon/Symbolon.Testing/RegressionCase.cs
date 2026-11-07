using System.Net.Http.Headers;
using System.Text;

namespace Symbolon.Testing;

/// <summary>What a case sends, and so what it expects.</summary>
public enum CaseKind
{
    /// <summary>The spec's own examples: must be accepted.</summary>
    Example,

    /// <summary>A request built to pass every schema: must be accepted.</summary>
    Valid,

    /// <summary>A valid request with one value at a bound (maxLength, minimum...): must be accepted.</summary>
    Boundary,

    /// <summary>A request that breaks exactly one constraint: must be rejected with a 4xx.</summary>
    Invalid,
}

/// <summary>An HTTP request, independent of any client: it can be sent, printed or turned into a curl command.</summary>
public sealed record RegressionRequest(
    string Method,
    string Path,
    IReadOnlyList<KeyValuePair<string, string>> Query,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    string? Body,
    string? ContentType)
{
    /// <summary>The value of each path parameter, for links that refer to them ($request.path.petId).</summary>
    public IReadOnlyDictionary<string, string> PathParameters { get; init; } = new Dictionary<string, string>();

    /// <summary>The path and query, relative to the API's base address.</summary>
    public string Target
        => Path.TrimStart('/') + (Query.Count == 0 ? string.Empty
            : "?" + string.Join("&", Query.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}")));

    public HttpRequestMessage ToMessage()
    {
        var message = new HttpRequestMessage(new HttpMethod(Method), new Uri(Target, UriKind.Relative));
        foreach (var (name, value) in Headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        if (Body is not null || ContentType is not null)
        {
            message.Content = new StringContent(Body ?? string.Empty, Encoding.UTF8);
            message.Content.Headers.ContentType = ContentType is null ? null : MediaTypeHeaderValue.Parse(ContentType);
        }

        return message;
    }

    /// <summary>The same request as a curl command, to reproduce a failure by hand.</summary>
    public string Curl(Uri? baseAddress)
    {
        var url = new Uri(baseAddress ?? new Uri("http://localhost/"), Target);
        var command = new StringBuilder($"curl -i -X {Method} '{url}'");
        foreach (var (name, value) in Headers)
        {
            command.Append(System.Globalization.CultureInfo.InvariantCulture, $" -H '{name}: {value}'");
        }

        if (ContentType is not null)
        {
            command.Append(System.Globalization.CultureInfo.InvariantCulture, $" -H 'Content-Type: {ContentType}'");
        }

        if (Body is not null)
        {
            command.Append(System.Globalization.CultureInfo.InvariantCulture, $" --data-raw '{Body.Replace("'", "'\\''", StringComparison.Ordinal)}'");
        }

        return command.ToString();
    }

    public override string ToString() => $"{Method} /{Target}";
}

/// <summary>
/// One generated test: a request against one operation, what kind of request it is, and (for an invalid request) where
/// the value it breaks is: "body", "body.name", "body.details.livesLeft", "query.limit", "path.petId", "header.X-Id".
/// A validation problem in reply should name it. <see cref="Name"/> is unique and stable, so it can name a test.
/// </summary>
public sealed record RegressionCase(string Name, SpecOperation Operation, CaseKind Kind, RegressionRequest Request, string? Location = null)
{
    public override string ToString() => Name;
}

/// <summary>
/// A stateful test from the spec's links: call an operation, then follow each link from its response, checking that the
/// resource can be read back as it was written, and that it is gone once deleted.
/// </summary>
public sealed record RegressionWorkflow(string Name, RegressionCase Start, string Status, IReadOnlyList<SpecLink> Links)
{
    public override string ToString() => Name;
}
