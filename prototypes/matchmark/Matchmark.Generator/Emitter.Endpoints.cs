using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>The emitter's endpoints (decision Q10: the model and its outputs, split by output).</summary>
    internal sealed partial class Emitter
    {
        // ---------------------------------------------------------------- endpoints

        private static readonly string[] EndpointUsings =
        {
            "System", "System.Threading", "System.Threading.Tasks", "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Http",
            "Microsoft.AspNetCore.Http.HttpResults", "Microsoft.AspNetCore.Mvc", "Microsoft.AspNetCore.Routing", "System.Collections.Generic",
            "System.Text.Json", "System.Text.RegularExpressions",
        };

        /// <summary>
        /// Each tag's Map{Api}{Tag} is emitted on its own, from that tag's slice of the spec (question 4), so the generator
        /// can skip the tags an edit didn't touch; the endpoints file then only calls them.
        /// </summary>
        public bool SplitByTag { get; set; }

        /// <summary>One tag's endpoints, from a slice of the spec holding only its operations: the partial class's Map{Api}{Tag}.</summary>
        internal string EmitTagEndpoints()
        {
            CollectSchemas();
            var operations = CollectOperations();
            var w = new CodeWriter(_namespace);
            w.Usings(EndpointUsings);
            w.Open($"public static partial class {_apiName}Endpoints");
            foreach (var tag in operations.GroupBy(o => Naming.Pascal(string.IsNullOrEmpty(o.Tag) ? "Default" : o.Tag)))
            {
                EmitTag(w, tag.Key, tag);
            }

            w.Close();
            return w.ToString();
        }

        private void EmitTag(CodeWriter w, string tag, IEnumerable<Operation> operations)
        {
            w.Line($"/// <summary>Maps the operations tagged {tag} onto their handler interface, which must be registered in DI.</summary>");
            w.Open($"public static IEndpointRouteBuilder Map{_apiName}{tag}(this IEndpointRouteBuilder app)");
            foreach (var operation in operations)
            {
                EmitEndpoint(w, operation);
            }

            w.Line("return app;");
            w.Close();
            w.Line();
        }

        private string EmitEndpoints(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings(EndpointUsings);

            w.Open($"public static partial class {_apiName}Endpoints");
            w.Line("/// <summary>Configures JSON the way the spec reads: numbers are numbers, and a oneOf discriminator may come anywhere.</summary>");
            w.Open($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add{_apiName}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            w.Line($"// A request that can't be bound throws, so Use{_apiName}Problems can answer it as the spec's validation problem.");
            w.Line("global::Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.Configure<RouteHandlerOptions>(services, options => options.ThrowOnBadRequest = true);");
            if (_resolverEmitted || ReferencedResolver)
            {
                w.Line("// .NET's validation layer, with the spec's resolver first (validation.md): it answers a bad body before the handler runs.");
                w.Line($"global::{_namespace}.{_apiName}ValidationExtensions.Add{_apiName}Validation(services);");
            }

            w.Open("return global::Microsoft.Extensions.DependencyInjection.HttpJsonServiceExtensions.ConfigureHttpJsonOptions(services, options =>", braces: false);
            w.Line("{");
            w.Line("    options.SerializerOptions.NumberHandling = global::System.Text.Json.Serialization.JsonNumberHandling.Strict;");
            w.Line("    options.SerializerOptions.AllowOutOfOrderMetadataProperties = true;");
            w.Line($"    options.SerializerOptions.TypeInfoResolver = global::System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.WithAddedModifier(options.SerializerOptions.TypeInfoResolver ?? new global::System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), {ValidationClass}.RefuseNulls);");
            w.Line("});");
            w.Close();
            w.Line();
            w.Line("/// <summary>");
            w.Line("/// Maps every operation in the spec onto its handler interface, which must be registered in DI. On an application");
            w.Line($"/// (not a route group) it also adds Use{_apiName}Problems, so requests that can't be bound get the spec's validation problem.");
            w.Line("/// </summary>");
            w.Open($"public static IEndpointRouteBuilder Map{_apiName}(this IEndpointRouteBuilder app)");
            w.Line("if (app is IApplicationBuilder pipeline)");
            w.Line("{");
            w.Line($"    pipeline.Use{_apiName}Problems();");
            w.Line("}");
            w.Line();

            // Each tag maps its own operations (question 4): Map{Api} maps them all, and a host can map one tag alone.
            var tags = operations.GroupBy(o => Naming.Pascal(string.IsNullOrEmpty(o.Tag) ? "Default" : o.Tag)).ToList();
            foreach (var tag in tags)
            {
                w.Line($"Map{_apiName}{tag.Key}(app);");
            }

            w.Line("return app;");
            w.Close();
            w.Line();
            if (!SplitByTag)
            {
                foreach (var tag in tags)
                {
                    EmitTag(w, tag.Key, tag);
                }
            }

            w.Block(ProblemsMiddleware.Replace("{Api}", _apiName).Replace("{Validation}", ValidationClass));
            w.Line();
            w.Line("/// <summary>Writes a problem as application/problem+json with the status the spec gives it, whatever the handler set.</summary>");
            w.Open("private static ProblemHttpResult Problem(ProblemDetails problem, int status)");
            w.Line("problem.Status = status;");
            w.Line("return TypedResults.Problem(problem);");
            w.Close();
            w.Close();
            return w.ToString();
        }

        /// <summary>
        /// The answer to a request the endpoints can't bind. ASP.NET Core would write an empty 400 (or, for a body missing its
        /// oneOf discriminator, a 500); the spec documents a validation problem naming what was wrong. Found by the
        /// regression suite generated from the spec.
        /// </summary>
        private const string ProblemsMiddleware = @"/// <summary>
/// Answers a request that can't be bound (a body that isn't the schema's JSON, a parameter of the wrong type) with the
/// spec's validation problem, naming the value that was wrong, instead of an empty 400 or a 500.
/// </summary>
public static IApplicationBuilder Use{Api}Problems(this IApplicationBuilder app)
    => app.Use(async (context, next) =>
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (BadHttpRequestException e) when (!context.Response.HasStarted && e.StatusCode != StatusCodes.Status400BadRequest)
        {
            await TypedResults.Problem(statusCode: e.StatusCode).ExecuteAsync(context).ConfigureAwait(false);
        }
        catch (Exception e) when (!context.Response.HasStarted && BindingError(e) is { } error)
        {
            await TypedResults.ValidationProblem(new Dictionary<string, string[]> { [error.Key] = [error.Message] }).ExecuteAsync(context).ConfigureAwait(false);
        }
    });

/// <summary>The value a binding failure is about, and what was wrong with it; null for any other exception.</summary>
private static (string Key, string Message)? BindingError(Exception exception) => exception switch
{
    BadHttpRequestException { InnerException: JsonException json } => {Validation}.BindingError(json.Path ?? ""$"", json.Message),
    BadHttpRequestException bad => {Validation}.BindingError(
        Regex.Match(bad.Message, @""parameter \x22(?:[^\x22]* )?([^\x22 ]+)\x22"") is { Success: true } parameter ? parameter.Groups[1].Value : ""body"", bad.Message),
    NotSupportedException { Source: ""System.Text.Json"" } unsupported when unsupported.Message.Contains(""type discriminator"", StringComparison.Ordinal)
        => {Validation}.BindingError(Regex.Match(unsupported.Message, @""Path: (\$\S*)"").Groups[1].Value, unsupported.Message),
    _ => null,
};";

        private void EmitEndpoint(CodeWriter w, Operation operation)
        {
            var args = operation.Parameters.Where(p => !p.Deep)
                .Select(p => p.Enum ? $"[{p.Attribute}(Name = {Naming.Literal(p.Name)})] string? __text_{p.Identifier.TrimStart('@')}" : $"[{p.Attribute}(Name = {Naming.Literal(p.Name)})] {p.Type} {p.Identifier}")
                .ToList();
            if (operation.Body is { Form: true } or { Text: true } || operation.Parameters.Any(p => p.Deep))
            {
                args.Add("HttpRequest __http");
            }

            if (operation.Body is { Form: true } or { Text: true })
            {
            }
            else if (operation.Body is not null)
            {
                args.Add($"[FromBody] {operation.Body.Type} body");
            }

            args.Add($"[FromServices] {HandlerInterface(operation.Tag)} __handlers");
            if (operation.ResponseHeaders.Count > 0)
            {
                args.Add("HttpContext __context");
            }

            args.Add("CancellationToken __cancellationToken");

            var call = new List<string>(operation.Parameters.Select(p => p.Identifier));
            if (operation.Body is not null)
            {
                call.Add("body");
            }

            call.Add("__cancellationToken");

            var returnType = operation.EndpointReturnType;
            w.Line($"app.MapMethods({Naming.Literal(operation.Path)}, new[] {{ {Naming.Literal(operation.Method)} }}, static async Task<{returnType}> ({string.Join(", ", args)}) =>");
            w.Open(string.Empty);
            foreach (var deep in operation.Parameters.Where(p => p.Deep))
            {
                EmitDeepRead(w, deep, "__http.Query", "__cancellationToken", "throw new BadHttpRequestException(\"The query doesn't match the schema.\", StatusCodes.Status400BadRequest, {0});");
            }

            EmitEnumReads(w, operation, "return TypedResults.ValidationProblem({0});");

            if (operation.Body is { Text: true } text)
            {
                // Text, read as it is: [FromBody] string would want JSON.
                w.Open($"if (__http.ContentType?.StartsWith({Naming.Literal(text.MediaType)}, StringComparison.OrdinalIgnoreCase) != true)");
                w.Line($"throw new BadHttpRequestException(\"The body must be {text.MediaType}.\", StatusCodes.Status415UnsupportedMediaType);");
                w.Close();
                w.Line();
                w.Line("var body = await new global::System.IO.StreamReader(__http.Body).ReadToEndAsync(__cancellationToken).ConfigureAwait(false);");
                w.Line($"if ({ValidationClass}.CheckText(body, {(text.Schema.MinLength ?? (operation.RequiredBody ? 1 : 0)).Invariant()}, {(text.Schema.MaxLength?.Invariant() ?? "null")}, {(text.Schema.Pattern is { } textPattern ? Naming.Literal(textPattern) : "null")}) is {{ }} __textErrors)");
                w.Line("{");
                w.Line("    return TypedResults.ValidationProblem(__textErrors);");
                w.Line("}");
                w.Line();
            }

            if (operation.Body is { Form: true } form)
            {
                // Read here rather than bound with [FromForm], so that nested values in brackets work and a form that
                // doesn't fit the schema is the same validation problem a JSON body would be.
                w.Open("if (!__http.HasFormContentType)");
                w.Line($"throw new BadHttpRequestException(\"The body must be {FormMedia}.\", StatusCodes.Status415UnsupportedMediaType);");
                w.Close();
                w.Line();
                w.Line($"{form.Type} body;");
                w.Open("try");
                w.Line($"body = {ValidationClass}.ReadForm<{form.Type}>(global::System.Linq.Enumerable.SelectMany(await __http.ReadFormAsync(__cancellationToken).ConfigureAwait(false), f => global::System.Linq.Enumerable.Select(f.Value, v => new KeyValuePair<string, string>(f.Key, v ?? string.Empty))));");
                w.Close();
                w.Open("catch (JsonException e)");
                w.Line("throw new BadHttpRequestException(\"The form doesn't match the schema.\", StatusCodes.Status400BadRequest, e);");
                w.Close();
                w.Line();
            }

            var bounds = ParameterChecks(operation).ToList();
            if (bounds.Count > 0)
            {
                // Bound parameters, checked against the spec's constraints: the binding only checks their type.
                w.Line("var __invalid = new Dictionary<string, string[]>();");
                foreach (var check in bounds)
                {
                    w.Line(check);
                }

                w.Line("if (__invalid.Count > 0)");
                w.Line("{");
                w.Line("    return TypedResults.ValidationProblem(__invalid);");
                w.Line("}");
                w.Line();
            }

            if (operation.Dependencies.Count > 0)
            {
                var given = operation.Parameters.Select(p => $"[{Naming.Literal(p.Name)}] = {ValidationClass}.Text({p.Identifier})");
                w.Line($"if ({ValidationClass}.Broken{operation.Name}Dependencies(new Dictionary<string, string?> {{ {string.Join(", ", given)} }}) is {{ }} __broken)");
                w.Line("{");
                w.Line("    return TypedResults.ValidationProblem(__broken);");
                w.Line("}");
                w.Line();
            }

            if (operation.Body is { Validated: true })
            {
                w.Line($"if ({ValidationClass}.Validate(body) is {{ }} __errors)");
                w.Line("{");
                w.Line("    return TypedResults.ValidationProblem(__errors);");
                w.Line("}");
                w.Line();
            }

            var invoke = $"await __handlers.{operation.Name}({string.Join(", ", call)}).ConfigureAwait(false)";
            var hasValue = operation.SuccessIsUnion || operation.Successes[0].BodyType is not null;
            var headers = operation.ResponseHeaders.Count > 0;
            if (headers)
            {
                w.Line($"using var __headers = {_apiName}Headers.Begin();");
            }

            // The response headers the handler set, written onto the response before it's returned.
            var apply = headers ? "foreach (var __header in __headers.Values) __context.Response.Headers[__header.Key] = __header.Value;" : null;
            if (operation.Errors.Count == 0)
            {
                if (!hasValue)
                {
                    w.Line($"{invoke};");
                    if (apply is not null)
                    {
                        w.Line(apply);
                    }

                    w.Line($"return {operation.Successes[0].Factory("_")};");
                }
                else
                {
                    w.Line($"var __value = {invoke};");
                    if (apply is not null)
                    {
                        w.Line(apply);
                    }

                    w.Line($"return {Success(operation, "__value", returnType)};");
                }
            }
            else
            {
                w.Line($"var __result = {invoke};");
                if (apply is not null)
                {
                    w.Line(apply);
                }

                w.Line($"return __result.Match<{returnType}>(");
                w.Line($"    __success => {Success(operation, "__success", returnType)},");
                w.Line($"    __error => __error.Match<{returnType}>({string.Join(", ", operation.Errors.Select(e => $"__case => {e.Factory("__case.Body")}"))}));");
            }

            w.Close(braces: true, suffix: ")");
            w.Line($"    .WithName({Naming.Literal(operation.Id)})");
            foreach (var problem in operation.Errors.Where(e => e.IsProblem && !e.IsValidation))
            {
                // ProblemHttpResult carries no response metadata of its own; document each status from the spec.
                w.Line($"    .Produces<{problem.BodyType}>({problem.Status.Invariant()}, \"application/problem+json\")");
            }

            if (operation.Untyped && (operation.Body is not null || operation.Dependencies.Count > 0 || operation.ValidatesParameters))
            {
                w.Line("    .ProducesValidationProblem()");
            }

            foreach (var response in operation.Successes.Concat(operation.Errors).Where(r => r.NeedsProduces || operation.Untyped && !r.IsProblem && !r.IsValidation))
            {
                // Written by number, so documented here: TypedResults.Json and StatusCode declare nothing themselves.
                w.Line(response.BodyType is null || response.IsRedirect
                    ? $"    .Produces({response.Status.Invariant()})"
                    : $"    .Produces<{response.BodyType}>({response.Status.Invariant()}, {Naming.Literal(response.MediaType ?? "application/json")})");
            }

            w.Line($"    .WithTags({Naming.Literal(operation.Tag)});");
            w.Line();
        }

        private static string Success(Operation operation, string value, string returnType)
            => operation.SuccessIsUnion
                ? $"{value}.Match<{returnType}>({string.Join(", ", operation.Successes.Select(s => $"__case => {s.Factory("__case.Body")}"))})"
                : operation.Successes[0].Factory(value);

        private void Unsupported(string what, string why)
            => Diagnostics.Add(Diagnostic.Create(Generator.Diagnostics.Unsupported, _location, what, why));
    }
}
