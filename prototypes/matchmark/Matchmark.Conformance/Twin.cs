using System.Text;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Matchmark.Conformance;

/// <summary>A twin (a stateful fake from the spec alone, <see cref="SpecTwin"/>) served by ASP.NET Core: a dev server.</summary>
public static class TwinExtensions
{
    /// <summary>Answers every request from a twin of the spec. Terminal: nothing after it runs.</summary>
    public static SpecTwin UseMatchmarkTwin(this IApplicationBuilder app, string specJson)
    {
        var twin = new SpecTwin(specJson);
        app.Run(async context =>
        {
            var request = context.Request;
            var body = request.ContentLength is > 0 || request.Headers.TransferEncoding.Count > 0
                ? await new StreamReader(request.Body, Encoding.UTF8).ReadToEndAsync(context.RequestAborted).ConfigureAwait(false)
                : null;
            var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            var (status, contentType, answer, answerHeaders) = twin.Answer(request.Method, request.Path.Value ?? "/", request.QueryString.Value ?? string.Empty,
                headers, body, request.ContentType?.Split(';')[0].Trim());
            context.Response.StatusCode = status;
            foreach (var (name, value) in answerHeaders)
            {
                context.Response.Headers[name] = value;
            }

            if (answer is not null)
            {
                context.Response.ContentType = contentType ?? "application/json";
                await context.Response.WriteAsync(answer, context.RequestAborted).ConfigureAwait(false);
            }
        });
        return twin;
    }
}
