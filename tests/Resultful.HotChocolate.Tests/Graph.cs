using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Resultful.HotChocolate.Tests;

/// <summary>Builds a real <see cref="IRequestExecutor"/> in process and runs operations against it.</summary>
public static class Graph
{
    public const string Development = "Development";
    public const string Production = "Production";

    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>A server with <c>AddResultfulErrors</c>, in the given environment.</summary>
    public static async Task<IRequestExecutor> BuildAsync(
        Action<IRequestExecutorBuilder> configure,
        Action<ResultfulGraphQLOptions>? options = null,
        string environment = Production)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
        var builder = services.AddGraphQL();
        configure(builder);
        builder.AddResultfulErrors(options);
        return await builder.BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>The schema's SDL with "\n" line endings, to compare with a pinned raw string.</summary>
    public static string Sdl(IRequestExecutor executor) => executor.Schema.ToString().ReplaceLineEndings("\n").Trim();

    /// <summary>Runs <paramref name="operation"/>, with <paramref name="result"/> as the global state "result" when given.</summary>
    public static async Task<JsonNode> RunAsync(
        IRequestExecutor executor,
        string operation,
        object? result = null,
        IReadOnlyDictionary<string, object?>? variables = null)
    {
        var request = OperationRequestBuilder.New().SetDocument(operation);
        if (result is not null) request.SetGlobalState("result", result);
        if (variables is not null) request.SetVariableValues(variables);
        var response = await executor.ExecuteAsync(request.Build(), TestContext.Current.CancellationToken);
        return JsonNode.Parse(response.ToJson())!;
    }

    /// <summary>The response's JSON, compact, for whole-response comparisons.</summary>
    public static async Task<string> RunJsonAsync(
        IRequestExecutor executor,
        string operation,
        object? result = null,
        IReadOnlyDictionary<string, object?>? variables = null) =>
        (await RunAsync(executor, operation, result, variables)).ToJsonString(Relaxed);
}
