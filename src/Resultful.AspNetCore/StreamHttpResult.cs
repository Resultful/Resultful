using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Resultful.AspNetCore;

/// <summary>
/// Turns a result that holds a stream into a <see cref="StreamHttpResult{T, TError}"/>: its checks have run before the
/// first item, so a failure answers a ProblemDetails, and a success streams a JSON array.
/// </summary>
/// <remarks>
/// More specific than <see cref="HttpResultExtensions"/>' overloads, so the compiler picks these for a
/// <c>Result&lt;IAsyncEnumerable&lt;T&gt;&gt;</c>. A zero (<c>default</c>) result has no case: converting it throws.
/// </remarks>
public static class StreamHttpResultExtensions
{
    /// <summary>A success streams its items as a JSON array with 200; a failure answers the mapped problem for its error.</summary>
    /// <typeparam name="T">The items' type.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>The HTTP result.</returns>
    public static StreamHttpResult<T, Error> ToHttpResult<T>(this Result<IAsyncEnumerable<T>> result) =>
        result.TryGetValue(out var values) ? new(values) : new(result.Error);

    /// <summary>A success streams its items as a JSON array with 200; a failure answers the mapped problem for its error.</summary>
    /// <typeparam name="T">The items' type.</typeparam>
    /// <typeparam name="TError">The error type.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>The HTTP result.</returns>
    public static StreamHttpResult<T, TError> ToHttpResult<T, TError>(this Result<IAsyncEnumerable<T>, TError> result) where TError : Error =>
        result.TryGetValue(out var values) ? new(values) : new(result.Error);
}

/// <summary>
/// A result that holds a stream, as an <see cref="IResult"/>. A failure answers the mapped ProblemDetails, exactly as
/// <see cref="ResultHttpResult{T, TError}"/> does, before any byte is written. A success answers 200 and streams the
/// items as a JSON array, as ASP.NET Core streams an <see cref="IAsyncEnumerable{T}"/>, with the app's JSON options.
/// </summary>
/// <typeparam name="T">The items' type.</typeparam>
/// <typeparam name="TError">The result's error type, kept for the endpoint's metadata.</typeparam>
/// <remarks>
/// <para>
/// The first item is read before the status and headers are sent, so an exception the stream throws before its first
/// item reaches the app's exception handling (a 500 from <c>UseExceptionHandler</c>, say) with the response not
/// started. So does one thrown before the serializer has sent anything. The other side of this: the status and headers
/// wait for the first item, so a slow producer can meet a proxy's or an <c>HttpClient</c>'s timeout before any header
/// arrives.
/// </para>
/// <para>
/// Once the response has started (the serializer sends what it holds whenever the stream waits for its next item),
/// the 200 and part of the array are on the wire: a failure can only end the response. An exception from the stream
/// is then noted at Warning (category <c>Resultful.AspNetCore.StreamHttpResult</c>, event
/// <c>ResultStreamFailed</c>, with how many items were handed to the serializer; the host logs the exception itself)
/// and rethrown, so the server aborts the response:
/// over HTTP/1.1 the chunked body ends without its last chunk, over HTTP/2 and HTTP/3 the stream is reset. The client
/// sees a truncated array and a read error, never a ProblemDetails.
/// </para>
/// <para>
/// The stream is read with <see cref="HttpContext.RequestAborted"/>, so a client that goes away cancels the producer.
/// Its enumerator is always disposed. Under native AOT, the app's JSON options need metadata for
/// <c>IAsyncEnumerable&lt;T&gt;</c>, as for any streamed endpoint.
/// </para>
/// </remarks>
public sealed class StreamHttpResult<T, TError> : IResult, IEndpointMetadataProvider where TError : Error
{
    /// <summary>The log category of a stream that failed after the response started.</summary>
    internal const string LogCategory = "Resultful.AspNetCore.StreamHttpResult";

    private static readonly Action<ILogger, int, Exception?> StreamFailed = LoggerMessage.Define<int>(
        LogLevel.Warning, new EventId(2, "ResultStreamFailed"), "A streamed result failed after {Count} items were handed to the serializer; the response was aborted.");

    private static readonly JsonSerializerOptions FallbackJson = new JsonOptions().SerializerOptions;

    private readonly IAsyncEnumerable<T>? _values;
    private readonly TError? _error;

    internal StreamHttpResult(IAsyncEnumerable<T> values) => _values = values ?? throw new ArgumentNullException(nameof(values));

    internal StreamHttpResult(TError error) => _error = error;

    /// <summary>Writes the response: the mapped problem on failure; on success, 200 and the items as they arrive.</summary>
    /// <param name="httpContext">The request's context; its services supply the options, the ProblemDetails service, the JSON options and logging.</param>
    /// <returns>A task that completes when the response is written, or faults with the stream's exception.</returns>
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return _error is not null ? ErrorProblems.WriteAsync(httpContext, _error) : StreamAsync(httpContext, _values!);
    }

    private static async Task StreamAsync(HttpContext httpContext, IAsyncEnumerable<T> values)
    {
        var services = httpContext.RequestServices;
        var json = services?.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions ?? FallbackJson;
#if NET11_0_OR_GREATER
        var typeInfo = json.GetTypeInfo<IAsyncEnumerable<T>>();
#else
        var typeInfo = (JsonTypeInfo<IAsyncEnumerable<T>>)json.GetTypeInfo(typeof(IAsyncEnumerable<T>));
#endif
        var aborted = httpContext.RequestAborted;
        var enumerator = values.GetAsyncEnumerator(aborted);
        await using (enumerator.ConfigureAwait(false))
        {
            // Before any byte: an exception here leaves the response unstarted.
            var any = await enumerator.MoveNextAsync().ConfigureAwait(false);
            var written = new Counter();
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            try
            {
                await httpContext.Response.WriteAsJsonAsync(Continue(enumerator, any, written), typeInfo, contentType: null, aborted).ConfigureAwait(false);
            }
            catch (Exception) when (httpContext.Response.HasStarted && !aborted.IsCancellationRequested)
            {
                var logger = services?.GetService<ILoggerFactory>()?.CreateLogger(LogCategory);
                if (logger is not null) StreamFailed(logger, written.Count, null);
                throw;
            }
        }
    }

    // The items from the one already read on; the enumerator is disposed by StreamAsync, not here.
    private static async IAsyncEnumerable<T> Continue(IAsyncEnumerator<T> enumerator, bool any, Counter written)
    {
        if (!any) yield break;
        do
        {
            yield return enumerator.Current;
            written.Count++;
        }
        while (await enumerator.MoveNextAsync().ConfigureAwait(false));
    }

    private sealed class Counter
    {
        public int Count;
    }

    /// <summary>
    /// Adds the statuses this result can answer to an endpoint's metadata, as <see cref="ResultHttpResult{T, TError}"/>
    /// does: 200 with a JSON array of <typeparamref name="T"/>, 400 with a validation problem when
    /// <typeparamref name="TError"/> can be a <see cref="ValidationFailed{TError}"/>, and 500 with a problem.
    /// </summary>
    /// <param name="method">The endpoint's handler.</param>
    /// <param name="builder">The endpoint's builder.</param>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder) =>
        ResultHttpResult<IAsyncEnumerable<T>, TError>.PopulateMetadata(method, builder);
}

/// <summary>
/// Turns a result that holds a stream, whose error is an error union (an <see cref="IErrorUnion"/>), into a
/// <see cref="StreamHttpResult{T, TError}"/>. The union's leaf, <see cref="IErrorUnion.Error"/>, is what the map sees.
/// </summary>
/// <remarks>
/// A separate class from <see cref="StreamHttpResultExtensions"/>: the two <c>ToHttpResult&lt;T, TError&gt;</c>
/// overloads differ only by constraint, as with <see cref="ErrorUnionHttpResultExtensions"/>.
/// </remarks>
public static class ErrorUnionStreamHttpResultExtensions
{
    /// <summary>A success streams its items as a JSON array with 200; a failure answers the mapped problem for the union's leaf.</summary>
    /// <typeparam name="T">The items' type.</typeparam>
    /// <typeparam name="TError">The error union.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>The HTTP result, typed with <see cref="Error"/>: the leaf may be any of the union's cases.</returns>
    /// <remarks>A zero (<c>default</c>) result has no case: converting it throws, as reading it does.</remarks>
    public static StreamHttpResult<T, Error> ToHttpResult<T, TError>(this Result<IAsyncEnumerable<T>, TError> result) where TError : struct, IErrorUnion =>
        result.TryGetValue(out var values) ? new(values) : new(result.Error.Error);
}
