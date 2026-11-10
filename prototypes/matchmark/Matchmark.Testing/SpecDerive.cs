using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Testing;

/// <summary>
/// Step 1 of the flip (docs/reference/flip.md): a starting spec from the document ASP.NET Core serves
/// (Microsoft.AspNetCore.OpenApi's <c>/openapi/v1.json</c>). It's the application's own description, so it records
/// accidents as well as decisions; this makes it usable as a spec and lists what to review before the flip.
/// </summary>
public static partial class SpecDerive
{
    /// <summary>The derived spec, and each thing a person should review in it.</summary>
    public static (JsonObject Spec, IReadOnlyList<string> Review) Derive(string documentJson)
    {
        var spec = JsonNode.Parse(documentJson) as JsonObject ?? throw new InvalidOperationException("The document isn't a JSON object.");
        var review = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var operations = new List<(string Method, string Path, JsonObject Operation)>();
        foreach (var (path, item) in spec["paths"] as JsonObject ?? [])
        {
            foreach (var (method, node) in item as JsonObject ?? [])
            {
                if (node is JsonObject operation && method is "get" or "put" or "post" or "delete" or "patch" or "head" or "options" or "trace" or "query")
                {
                    operations.Add((method, path, operation));
                    if (operation["operationId"]?.GetValue<string>() is { } id)
                    {
                        ids.Add(id);
                    }
                }
            }
        }

        foreach (var (method, path, operation) in operations)
        {
            var name = $"{method.ToUpperInvariant()} {path}";
            if (operation["operationId"] is null)
            {
                var id = Unique(OperationId(method, path), ids);
                operation["operationId"] = id;
                review.Add($"{name}: no operationId (an endpoint without .WithName), so it's {id}; name the endpoint to choose one");
            }

            var responses = operation["responses"] as JsonObject ?? [];
            if (operation["requestBody"] is not null && !responses.ContainsKey("400"))
            {
                review.Add($"{name}: takes a body but documents no 400, so how it refuses a bad one isn't in the spec (.ProducesValidationProblem)");
            }

            if (method == "get" && responses.Count == 1 && responses.ContainsKey("200") && path.Contains('{', StringComparison.Ordinal))
            {
                review.Add($"{name}: documents only 200, though it reads one resource: a 404 for one that doesn't exist is likely missing (.Produces(404))");
            }

            foreach (var (status, response) in responses)
            {
                if (status.StartsWith('2') && response?["content"] is JsonObject content && content.Select(c => c.Value).OfType<JsonObject>().All(c => c["example"] is null && c["examples"] is null))
                {
                    review.Add($"{name}: the {status} response has no example, so the suite and the twin sample one");
                }
            }
        }

        foreach (var server in (spec["servers"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (server["url"]?.GetValue<string>() is { } url && LocalHost().IsMatch(url))
            {
                review.Add($"servers: {url} is where the document was served from, not where the API lives");
            }
        }

        if (spec["info"]?["version"]?.GetValue<string>() is "1.0.0" or "v1" or null)
        {
            review.Add($"info.version is {spec["info"]?["version"]?.GetValue<string>() ?? "missing"}, ASP.NET Core's default: set the API's real version, which matchmark diff checks breaking changes against");
        }

        return (spec, review);
    }

    /// <summary>An operationId from a method and path: GET /pets/{petId}/photos is getPetsPhotosByPetId.</summary>
    public static string OperationId(string method, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var words = segments.Where(s => !s.StartsWith('{')).Select(Pascal);
        var parameters = segments.Where(s => s.StartsWith('{')).Select(s => Pascal(s.Trim('{', '}'))).ToList();
        return method.ToLowerInvariant() + string.Concat(words) + (parameters.Count > 0 ? "By" + string.Join("And", parameters) : string.Empty);
    }

    private static string Pascal(string word)
        => string.Concat(Regex.Split(word, "[^A-Za-z0-9]+").Where(p => p.Length > 0).Select(p => char.ToUpper(p[0], CultureInfo.InvariantCulture) + p[1..]));

    private static string Unique(string id, HashSet<string> taken)
    {
        var candidate = id;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            candidate = id + n.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    [GeneratedRegex(@"^https?://(localhost|127\.0\.0\.1|\[::1\])(:\d+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LocalHost();
}
