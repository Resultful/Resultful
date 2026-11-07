using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>An endpoint found in hand-written code: a controller action or a Minimal API Map call.</summary>
    internal sealed class Endpoint : IEquatable<Endpoint>
    {
        public Endpoint(string method, string route, string? name, string display, Location location, bool excluded, ImmutableArray<int>? statuses,
            ImmutableArray<Binding>? bindings, ImmutableArray<(int Status, string Type)>? responseTypes = null, ImmutableArray<TypeShape>? shapes = null)
        {
            Bindings = bindings;
            ResponseTypes = responseTypes ?? ImmutableArray<(int, string)>.Empty;
            Shapes = shapes ?? ImmutableArray<TypeShape>.Empty;
            Method = method;
            Route = route;
            Name = name;
            Display = display;
            Location = location;
            Excluded = excluded;
            Statuses = statuses;
        }

        public string Method { get; }

        /// <summary>The full route template, with parameter names but without constraints or defaults.</summary>
        public string Route { get; }

        public string? Name { get; }

        public string Display { get; }

        /// <summary>A Minimal API Map call, whose responses come from its return type and .Produces calls.</summary>
        public bool IsMap => Display.StartsWith("Map", StringComparison.Ordinal);

        public Location Location { get; }

        /// <summary>Left out of the OpenAPI description on purpose, so not part of the spec.</summary>
        public bool Excluded { get; }

        /// <summary>The declared response statuses; null when the endpoint's responses can't be read (Minimal API).</summary>
        public ImmutableArray<int>? Statuses { get; }

        /// <summary>What the handler binds from the request; null when the handler can't be read.</summary>
        public ImmutableArray<Binding>? Bindings { get; }

        /// <summary>The body type each declared response has, by its full name.</summary>
        public ImmutableArray<(int Status, string Type)> ResponseTypes { get; }

        /// <summary>The JSON shape of the request body's and responses' types, and of every type they reach.</summary>
        public ImmutableArray<TypeShape> Shapes { get; }

        public bool Equals(Endpoint? other)
            => other is not null && ResponseTypes.SequenceEqual(other.ResponseTypes) && Shapes.SequenceEqual(other.Shapes) && Method == other.Method && Route == other.Route && Name == other.Name && Display == other.Display
               && Excluded == other.Excluded && Location.Equals(other.Location)
               && (Statuses?.SequenceEqual(other.Statuses ?? ImmutableArray<int>.Empty) ?? other.Statuses is null)
               && (Bindings?.SequenceEqual(other.Bindings ?? ImmutableArray<Binding>.Empty) ?? other.Bindings is null);

        public override bool Equals(object? obj) => Equals(obj as Endpoint);

        public override int GetHashCode() => (Method, Route, Display).GetHashCode();
    }

    /// <summary>
    /// One value a handler binds from the request. Source is Route, Query, Header or Body, or Model for a complex parameter
    /// that an MVC action without [ApiController] binds from the form and query rather than the body.
    /// </summary>
    internal sealed class Binding : IEquatable<Binding>
    {
        public Binding(string source, string name, string type, bool optional, string parameter, string fullType,
            ImmutableArray<(string Keyword, string Value)> constraints)
        {
            Constraints = constraints;
            FullType = fullType;
            Source = source;
            Name = name;
            Type = type;
            Optional = optional;
            Parameter = parameter;
        }

        public string Source { get; }

        public string Name { get; }

        /// <summary>The C# type, without nullability.</summary>
        public string Type { get; }

        /// <summary>Nullable, or has a default: a request may leave it out.</summary>
        public bool Optional { get; }

        /// <summary>The C# parameter it binds to.</summary>
        public string Parameter { get; }

        /// <summary>The type's full name, so a body is compared by identity rather than by its simple name.</summary>
        public string FullType { get; }

        /// <summary>The parameter's DataAnnotations, as schema keywords.</summary>
        public ImmutableArray<(string Keyword, string Value)> Constraints { get; }

        public bool Equals(Binding? other)
            => other is not null && FullType == other.FullType && Constraints.SequenceEqual(other.Constraints) && Source == other.Source && Name == other.Name && Type == other.Type && Optional == other.Optional && Parameter == other.Parameter;

        public override bool Equals(object? obj) => Equals(obj as Binding);

        public override int GetHashCode() => (Source, Name, Type, Optional, Parameter).GetHashCode();
    }

    /// <summary>Finds the endpoints in hand-written code, and checks them against the spec's operations.</summary>
    internal static class CoverageVerifier
    {
        private static readonly string[] Verbs = { "Get", "Post", "Put", "Delete", "Patch" };
        private static readonly char[] Slash = { '/' };
        private static readonly char[] ParameterEnd = { ':', '=', '?' };

        // ---------------------------------------------------------------- controllers

        public static bool IsCandidateAction(SyntaxNode node)
            => node is MethodDeclarationSyntax method && method.AttributeLists.Count > 0;

        public static IEnumerable<Endpoint> ReadAction(GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
        {
            if (context.SemanticModel.GetDeclaredSymbol(context.Node, ct) is not IMethodSymbol method)
            {
                yield break;
            }

            var type = method.ContainingType;
            var excluded = IsExcluded(method.GetAttributes()) || IsExcluded(type.GetAttributes());
            var classRoute = type.GetAttributes().FirstOrDefault(a => IsMvc(a, "RouteAttribute"))?.ConstructorArguments.FirstOrDefault().Value as string;
            var produces = method.GetAttributes().Concat(type.GetAttributes())
                .Where(a => IsMvc(a, "ProducesResponseTypeAttribute") || IsMvc(a, "ProducesResponseTypeAttribute`1"))
                .Select(a => (Status: a.ConstructorArguments.FirstOrDefault(c => c.Type?.SpecialType == SpecialType.System_Int32).Value as int?,
                              Type: a.AttributeClass!.TypeArguments.FirstOrDefault()
                                    ?? a.ConstructorArguments.FirstOrDefault(c => c.Kind == TypedConstantKind.Type).Value as ITypeSymbol))
                .Where(p => p.Status is not null)
                .Select(p => (Status: p.Status!.Value, p.Type))
                .ToList();
            var statuses = produces.Select(p => p.Status).Distinct().OrderBy(s => s).ToImmutableArray();

            // A success declared without a type is described with the action's own return type, as ApiExplorer does.
            var returned = ReturnedType(method.ReturnType);
            var responseTypes = produces
                .Select(p => (p.Status, Type: p.Type ?? (p.Status is >= 200 and < 300 ? returned : null)))
                .Where(p => p.Type is not null)
                .ToList();
            var controller = type.Name.EndsWith("Controller", StringComparison.Ordinal) ? type.Name.Substring(0, type.Name.Length - "Controller".Length) : type.Name;

            foreach (var attribute in method.GetAttributes())
            {
                var verb = Verbs.FirstOrDefault(v => IsMvc(attribute, $"Http{v}Attribute"));
                if (verb is null)
                {
                    continue;
                }

                var template = attribute.ConstructorArguments.FirstOrDefault().Value as string ?? string.Empty;
                var name = attribute.NamedArguments.FirstOrDefault(n => n.Key == "Name").Value.Value as string;
                var route = template.StartsWith("/", StringComparison.Ordinal) || template.StartsWith("~/", StringComparison.Ordinal)
                    ? template.TrimStart('~')
                    : Join(classRoute, template);
                route = route.Replace("[controller]", controller).Replace("[action]", method.Name);
                var location = (attribute.ApplicationSyntaxReference?.GetSyntax(ct) ?? context.Node).GetLocation();
                var normalized = Normalize(route);
                var apiController = IsApiController(type);
                var bindings = Bindings(method, normalized, complexFromBody: apiController);
                yield return new Endpoint(verb.ToUpperInvariant(), normalized, name, $"{type.Name}.{method.Name}", location, excluded, statuses,
                    bindings, responseTypes.Select(r => (r.Status, TypeShape.NameOf(r.Type!))).ToImmutableArray(),
                    Shapes(method, bindings, responseTypes.Select(r => r.Type!)));
            }
        }

        /// <summary>T, from T, ActionResult&lt;T&gt;, Task&lt;T&gt; or ValueTask&lt;ActionResult&lt;T&gt;&gt;; null for IActionResult and void.</summary>
        private static ITypeSymbol? ReturnedType(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { IsGenericType: true } named && named.Name is "Task" or "ValueTask")
            {
                type = named.TypeArguments[0];
            }
            else if (type.Name is "Task" or "ValueTask" || type.SpecialType == SpecialType.System_Void)
            {
                return null;
            }

            if (type is INamedTypeSymbol { Name: "ActionResult", IsGenericType: true } result)
            {
                return result.TypeArguments[0];
            }

            // ApiExplorer describes an object return type as unknown, like IActionResult.
            return type.SpecialType == SpecialType.System_Object
                   || type.AllInterfaces.Any(i => i.Name is "IActionResult" or "IResult") || type.Name is "IActionResult" or "IResult"
                ? null
                : type;
        }

        /// <summary>The shapes of the body's type and the response types, and of everything they reach.</summary>
        private static ImmutableArray<TypeShape> Shapes(IMethodSymbol method, ImmutableArray<Binding> bindings, IEnumerable<ITypeSymbol> responses)
        {
            var shapes = new Dictionary<string, TypeShape>(StringComparer.Ordinal);
            var body = bindings.FirstOrDefault(b => b.Source is "Body" or "Model");
            if (body is not null && method.Parameters.FirstOrDefault(p => p.Name == body.Parameter) is { } parameter)
            {
                TypeShape.Collect(parameter.Type, shapes);
            }

            foreach (var response in responses)
            {
                TypeShape.Collect(response, shapes);
            }

            return shapes.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToImmutableArray();
        }

        private static bool IsApiController(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (current.GetAttributes().Any(a => IsMvc(a, "ApiControllerAttribute")))
                {
                    return true;
                }
            }

            return type.ContainingAssembly.GetAttributes().Any(a => IsMvc(a, "ApiControllerAttribute"));
        }

        private static bool IsExcluded(ImmutableArray<AttributeData> attributes)
            => attributes.Any(a => IsMvc(a, "NonActionAttribute")
                || (IsMvc(a, "ApiExplorerSettingsAttribute") && a.NamedArguments.Any(n => n.Key == "IgnoreApi" && n.Value.Value is true)));

        private static bool IsMvc(AttributeData attribute, string metadataName)
            => attribute.AttributeClass is { } c && c.MetadataName == metadataName && c.ContainingNamespace.ToDisplayString() == "Microsoft.AspNetCore.Mvc";

        // ---------------------------------------------------------------- Minimal APIs

        public static bool IsCandidateMap(SyntaxNode node)
            => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }
               && member.Name.Identifier.ValueText is "MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch" or "MapMethods";

        public static IEnumerable<Endpoint> ReadMap(GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            var member = (MemberAccessExpressionSyntax)invocation.Expression;
            if (context.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol { } symbol
                || symbol.ContainingNamespace.ToDisplayString() != "Microsoft.AspNetCore.Builder"
                || invocation.ArgumentList.Arguments.Count == 0
                || Literal(invocation.ArgumentList.Arguments[0].Expression) is not { } pattern)
            {
                yield break;
            }

            var name = member.Name.Identifier.ValueText;
            var verbs = name == "MapMethods"
                ? (invocation.ArgumentList.Arguments.Count > 1 ? Strings(invocation.ArgumentList.Arguments[1].Expression) : Enumerable.Empty<string>())
                : new[] { name.Substring(3).ToUpperInvariant() };

            var (prefix, groupExcluded, groupCalls) = Group(member.Expression, context.SemanticModel, ct);
            var chain = Chain(invocation).ToList();
            var excluded = groupExcluded || chain.Any(c => c.Name == "ExcludeFromDescription");
            var endpointName = chain.Where(c => c.Name == "WithName").Select(c => c.Argument).FirstOrDefault();
            var route = Normalize(Join(prefix, pattern));
            var handlerIndex = name == "MapMethods" ? 2 : 1;
            var handler = invocation.ArgumentList.Arguments.Count > handlerIndex
                ? Handler(invocation.ArgumentList.Arguments[handlerIndex].Expression, context.SemanticModel, ct)
                : null;
            var bindings = handler is null ? (ImmutableArray<Binding>?)null : Bindings(handler, route, complexFromBody: true);

            // Responses, as ApiExplorer describes them: the metadata of the handler's typed results, then .Produces calls on
            // the endpoint and its group. With neither, a handler returning T is a 200 of T, and any other a plain 200.
            ImmutableArray<int>? statuses = null;
            var responseTypes = new List<(int Status, ITypeSymbol? Type)>();
            if (handler is not null)
            {
                responseTypes.AddRange(ResultResponses(handler.ReturnType, out var returned));
                responseTypes.AddRange(groupCalls.Concat(chain.Select(c => c.Call))
                    .Select(c => Produces(c, context.SemanticModel, ct))
                    .Where(p => p is not null)
                    .Select(p => p!.Value));
                if (responseTypes.Count == 0)
                {
                    responseTypes.Add((200, returned));
                }

                statuses = responseTypes.Select(r => r.Status).Distinct().OrderBy(s => s).ToImmutableArray();
            }

            var typed = responseTypes.Where(r => r.Type is not null).ToList();
            var shapes = handler is null ? ImmutableArray<TypeShape>.Empty : Shapes(handler, bindings!.Value, typed.Select(r => r.Type!));
            foreach (var verb in verbs)
            {
                yield return new Endpoint(verb.ToUpperInvariant(), route, endpointName,
                    $"{name}(\"{pattern}\")", invocation.GetLocation(), excluded, statuses, bindings,
                    typed.Select(r => (r.Status, TypeShape.NameOf(r.Type!))).Distinct().ToImmutableArray(), shapes);
            }
        }

        /// <summary>The status each typed result in Microsoft.AspNetCore.Http.HttpResults declares, as its metadata does.</summary>
        private static readonly Dictionary<string, int> TypedResultStatuses = new(StringComparer.Ordinal)
        {
            ["Ok"] = 200, ["Created"] = 201, ["CreatedAtRoute"] = 201, ["Accepted"] = 202, ["AcceptedAtRoute"] = 202, ["NoContent"] = 204,
            ["BadRequest"] = 400, ["UnauthorizedHttpResult"] = 401, ["NotFound"] = 404, ["Conflict"] = 409,
            ["UnprocessableEntity"] = 422, ["InternalServerError"] = 500, ["ValidationProblem"] = 400,
        };

        /// <summary>
        /// The responses a handler's return type declares: each case of Results&lt;...&gt;, or a single typed result.
        /// <paramref name="returned"/> is the type a handler returning a plain value (not an IResult) writes as its 200.
        /// </summary>
        private static IEnumerable<(int Status, ITypeSymbol? Type)> ResultResponses(ITypeSymbol type, out ITypeSymbol? returned)
        {
            returned = null;
            if (type is INamedTypeSymbol { IsGenericType: true, Name: "Task" or "ValueTask" } awaited)
            {
                type = awaited.TypeArguments[0];
            }
            else if (type.Name is "Task" or "ValueTask" || type.SpecialType == SpecialType.System_Void)
            {
                return Enumerable.Empty<(int, ITypeSymbol?)>();
            }

            if (type is INamedTypeSymbol { Name: "Results" } results && IsHttpResults(results))
            {
                return results.TypeArguments.SelectMany(t => TypedResult(t)).ToList();
            }

            if (type is INamedTypeSymbol named && IsHttpResults(named))
            {
                return TypedResult(named).ToList();
            }

            if (type.SpecialType != SpecialType.System_Object && type.Name is not ("IResult" or "IActionResult") && !type.AllInterfaces.Any(i => i.Name == "IResult"))
            {
                returned = type;
            }

            return Enumerable.Empty<(int, ITypeSymbol?)>();
        }

        private static IEnumerable<(int Status, ITypeSymbol? Type)> TypedResult(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named || !TypedResultStatuses.TryGetValue(named.Name, out var status))
            {
                yield break; // ProblemHttpResult, JsonHttpResult and the like declare no response of their own.
            }

            if (named.Name == "ValidationProblem")
            {
                yield return (status, ValidationProblemType(named));
                yield break;
            }

            yield return (status, named.TypeArguments.Length == 1 ? named.TypeArguments[0] : null);
        }

        private static ITypeSymbol? ValidationProblemType(INamedTypeSymbol validationProblem)
            => validationProblem.GetMembers("ProblemDetails").OfType<IPropertySymbol>().FirstOrDefault()?.Type;

        private static bool IsHttpResults(INamedTypeSymbol type) => type.ContainingNamespace.ToDisplayString() == "Microsoft.AspNetCore.Http.HttpResults";

        /// <summary>The response a .Produces, .ProducesProblem or .ProducesValidationProblem call declares.</summary>
        private static (int Status, ITypeSymbol? Type)? Produces(InvocationExpressionSyntax call, SemanticModel model, System.Threading.CancellationToken ct)
        {
            if (model.GetSymbolInfo(call, ct).Symbol is not IMethodSymbol method
                || method.Name is not ("Produces" or "ProducesProblem" or "ProducesValidationProblem"))
            {
                return null;
            }

            int? status = null;
            ITypeSymbol? type = method.TypeArguments.FirstOrDefault();
            foreach (var argument in call.ArgumentList.Arguments)
            {
                var parameter = argument.NameColon is { } colon
                    ? method.Parameters.FirstOrDefault(p => p.Name == colon.Name.Identifier.ValueText)
                    : method.Parameters.ElementAtOrDefault(call.ArgumentList.Arguments.IndexOf(argument) + (method.IsExtensionMethod && method.ReducedFrom is null ? 1 : 0));
                if (parameter?.Name == "statusCode" && model.GetConstantValue(argument.Expression, ct).Value is int value)
                {
                    status = value;
                }
                else if (parameter?.Name == "responseType" && argument.Expression is TypeOfExpressionSyntax typeOf)
                {
                    type = model.GetTypeInfo(typeOf.Type, ct).Type;
                }
            }

            var parameters = method.ReducedFrom?.Parameters ?? method.Parameters;
            status ??= parameters.FirstOrDefault(p => p.Name == "statusCode") is { HasExplicitDefaultValue: true, ExplicitDefaultValue: int fallback } ? fallback : null;
            if (status is null)
            {
                return null;
            }

            return method.Name switch
            {
                "ProducesProblem" => (status.Value, model.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ProblemDetails")),
                "ProducesValidationProblem" => (status.Value, model.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.HttpValidationProblemDetails")),
                _ => (status.Value, type),
            };
        }

        /// <summary>The method a Map call's handler is: a lambda, or a method group.</summary>
        private static IMethodSymbol? Handler(ExpressionSyntax expression, SemanticModel model, System.Threading.CancellationToken ct)
        {
            var info = model.GetSymbolInfo(expression, ct);
            return info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        }

        // ---------------------------------------------------------------- parameter binding, as ASP.NET Core infers it

        private static readonly string[] SpecialTypes =
        {
            "System.Threading.CancellationToken", "Microsoft.AspNetCore.Http.HttpContext", "Microsoft.AspNetCore.Http.HttpRequest",
            "Microsoft.AspNetCore.Http.HttpResponse", "System.Security.Claims.ClaimsPrincipal", "System.IO.Stream",
            "System.IO.Pipelines.PipeReader", "Microsoft.AspNetCore.Http.IFormFile", "Microsoft.AspNetCore.Http.IFormFileCollection",
            "Microsoft.AspNetCore.Http.IFormCollection",
        };

        /// <summary>
        /// What each parameter binds from. An explicit [From*] attribute wins; otherwise ASP.NET Core's inference: special
        /// types are skipped, a name in the route template binds from the route, a simple type from the query, an interface
        /// from services, and a complex type from the body (or, for MVC without [ApiController], from the form and query).
        /// </summary>
        private static ImmutableArray<Binding> Bindings(IMethodSymbol method, string route, bool complexFromBody)
        {
            var routeNames = new HashSet<string>(ParameterNames(route), StringComparer.OrdinalIgnoreCase);
            var bindings = ImmutableArray.CreateBuilder<Binding>();
            foreach (var parameter in method.Parameters)
            {
                var type = parameter.Type;
                var underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                    ? nullable.TypeArguments[0]
                    : type;
                var optional = !ReferenceEquals(underlying, type) || type.NullableAnnotation == NullableAnnotation.Annotated || parameter.HasExplicitDefaultValue;
                var display = TypeName(underlying.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                var attribute = parameter.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name is
                    "FromRouteAttribute" or "FromQueryAttribute" or "FromHeaderAttribute" or "FromBodyAttribute" or "FromServicesAttribute"
                    or "FromKeyedServicesAttribute" or "FromFormAttribute" or "AsParametersAttribute");
                string source;
                var name = parameter.Name;
                if (attribute is not null)
                {
                    source = attribute.AttributeClass!.Name.Substring(4, attribute.AttributeClass.Name.Length - "FromAttribute".Length);
                    name = attribute.NamedArguments.FirstOrDefault(n => n.Key == "Name").Value.Value as string ?? name;
                }
                else if (SpecialTypes.Contains(underlying.ToDisplayString()))
                {
                    continue;
                }
                else if (routeNames.Contains(parameter.Name))
                {
                    source = "Route";
                }
                else if (IsSimple(underlying))
                {
                    source = "Query";
                }
                else if (underlying.TypeKind == TypeKind.Interface || underlying.IsAbstract && underlying.TypeKind == TypeKind.Class && !IsCollection(underlying))
                {
                    source = "Services";
                }
                else
                {
                    source = complexFromBody ? "Body" : "Model";
                }

                if (source is "Services" or "KeyedServices" or "Form" or "Parameters")
                {
                    continue;
                }

                bindings.Add(new Binding(source, name, display, optional, parameter.Name, TypeShape.NameOf(underlying),
                    Constraints.Of(parameter.GetAttributes(), IsCollection(underlying) && underlying.SpecialType != SpecialType.System_String)));
            }

            return bindings.ToImmutable();
        }

        private static bool IsSimple(ITypeSymbol type)
            => type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_String or SpecialType.System_DateTime
               || type.TypeKind == TypeKind.Enum
               || type.ToDisplayString() is "System.Guid" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan" or "System.Uri";

        private static bool IsCollection(ITypeSymbol type) => type.AllInterfaces.Any(i => i.Name == "IEnumerable");

        /// <summary>"global::System.Collections.Generic.IReadOnlyList&lt;PetStore.Api.Pet&gt;" → "IReadOnlyList&lt;Pet&gt;".</summary>
        internal static string TypeName(string type)
            => System.Text.RegularExpressions.Regex.Replace(type.Replace("global::", string.Empty), @"\b(?:[A-Za-z_]\w*\.)+", string.Empty);

        /// <summary>The calls chained after a Map call: <c>.WithName("x").ExcludeFromDescription()</c>.</summary>
        private static IEnumerable<(string Name, string? Argument, InvocationExpressionSyntax Call)> Chain(InvocationExpressionSyntax invocation)
        {
            SyntaxNode current = invocation;
            while (current.Parent is MemberAccessExpressionSyntax access && access.Parent is InvocationExpressionSyntax next)
            {
                yield return (access.Name.Identifier.ValueText, next.ArgumentList.Arguments.Count > 0 ? Literal(next.ArgumentList.Arguments[0].Expression) : null, next);
                current = next;
            }
        }

        /// <summary>The route prefix, exclusion and calls of the group a Map call is made on, when it can be read from source.</summary>
        private static (string? Prefix, bool Excluded, List<InvocationExpressionSyntax> Calls) Group(ExpressionSyntax receiver, SemanticModel model, System.Threading.CancellationToken ct)
        {
            if (receiver is IdentifierNameSyntax identifier
                && model.GetSymbolInfo(identifier, ct).Symbol is ILocalSymbol local
                && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct) is VariableDeclaratorSyntax { Initializer.Value: { } value })
            {
                receiver = value;
            }

            string? prefix = null;
            var excluded = false;
            var calls = new List<InvocationExpressionSyntax>();
            foreach (var node in receiver.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (node.Expression is MemberAccessExpressionSyntax access)
                {
                    calls.Add(node);
                    if (access.Name.Identifier.ValueText == "MapGroup" && node.ArgumentList.Arguments.Count > 0)
                    {
                        prefix = Literal(node.ArgumentList.Arguments[0].Expression) ?? prefix;
                    }

                    excluded |= access.Name.Identifier.ValueText == "ExcludeFromDescription";
                }
            }

            return (prefix, excluded, calls);
        }

        private static string? Literal(ExpressionSyntax expression)
            => expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) ? literal.Token.ValueText : null;

        private static IEnumerable<string> Strings(ExpressionSyntax expression)
            => expression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>()
                .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression))
                .Select(l => l.Token.ValueText);

        // ---------------------------------------------------------------- routes

        private static string Join(string? prefix, string template)
            => string.IsNullOrEmpty(prefix) ? template : prefix!.TrimEnd('/') + "/" + template.TrimStart('/');

        /// <summary>"/pets/{petId:long}/" → "/pets/{petId}"; "api/Pets" → "/api/pets".</summary>
        internal static string Normalize(string route)
        {
            var segments = route.Split(Slash, StringSplitOptions.RemoveEmptyEntries).Select(segment =>
            {
                if (!segment.StartsWith("{", StringComparison.Ordinal))
                {
                    return segment.ToLowerInvariant();
                }

                var name = segment.Trim('{', '}').TrimStart('*');
                var end = name.IndexOfAny(ParameterEnd);
                return "{" + (end >= 0 ? name.Substring(0, end) : name) + "}";
            });
            return "/" + string.Join("/", segments);
        }

        /// <summary>"/pets/{petId}" → "/pets/{}": the route's shape, for matching regardless of parameter names.</summary>
        private static string Shape(string route)
            => string.Join("/", route.Split('/').Select(s => s.StartsWith("{", StringComparison.Ordinal) ? "{}" : s));

        private static IEnumerable<string> ParameterNames(string route)
            => route.Split('/').Where(s => s.StartsWith("{", StringComparison.Ordinal)).Select(s => s.Trim('{', '}'));

        // ---------------------------------------------------------------- the check

        private static IEnumerable<Diagnostic> VerifyBindings(Emitter emitter, SpecOperation operation, Endpoint endpoint, ImmutableArray<Binding> bindings, string where, bool checkRoute)
        {
            var bound = new HashSet<Binding>();
            foreach (var parameter in operation.Parameters.Where(p => checkRoute || p.Source != "Route"))
            {
                var binding = bindings.FirstOrDefault(b => b.Source == parameter.Source && string.Equals(b.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                if (binding is null)
                {
                    yield return Diagnostic.Create(Diagnostics.ParameterNotBound, endpoint.Location, where, parameter.Source.ToLowerInvariant(), parameter.Name,
                        $"[From{parameter.Source}(Name = \"{parameter.Name}\")] {TypeName(parameter.Type)}{(parameter.Required ? string.Empty : "?")}");
                    continue;
                }

                bound.Add(binding);
                if (binding.Type != TypeName(parameter.Type))
                {
                    yield return Diagnostic.Create(Diagnostics.BindingTypeMismatch, endpoint.Location, where, $"'{parameter.Name}'", binding.Type, TypeName(parameter.Type));
                }

                foreach (var difference in Constraints.Differences($"'{parameter.Name}'", parameter.Constraints, binding.Constraints))
                {
                    yield return Diagnostic.Create(Diagnostics.ParameterConstraintDiffers, endpoint.Location, where, difference);
                }

                if (!parameter.Required && !binding.Optional)
                {
                    yield return Diagnostic.Create(Diagnostics.OptionalParameterRequired, endpoint.Location, where, parameter.Name, binding.Type);
                }
            }

            foreach (var extra in bindings.Where(b => b.Source is "Query" or "Header" || (checkRoute && b.Source == "Route")).Where(b => !bound.Contains(b)))
            {
                yield return Diagnostic.Create(Diagnostics.ParameterNotDocumented, endpoint.Location, where, extra.Source.ToLowerInvariant(), extra.Name, extra.Parameter);
            }

            var body = bindings.FirstOrDefault(b => b.Source == "Body");
            var model = bindings.FirstOrDefault(b => b.Source == "Model");
            if (operation.BodyType is { } expected)
            {
                if (body is null)
                {
                    yield return Diagnostic.Create(Diagnostics.BodyMismatch, endpoint.Location, where, model is null
                        ? $"the spec has a {TypeName(expected)} request body, but the endpoint binds none"
                        : $"the spec has a {TypeName(expected)} request body, but '{model.Parameter}' binds from the form and query: mark it [FromBody], or add [ApiController]");
                }
                else if (operation.BodySchema is { } schema)
                {
                    foreach (var diagnostic in VerifyModel(emitter, schema, body.FullType, endpoint, where, "the request body"))
                    {
                        yield return diagnostic;
                    }
                }
            }
            else if (body is not null)
            {
                yield return Diagnostic.Create(Diagnostics.BodyMismatch, endpoint.Location, where,
                    $"'{body.Parameter}' binds a {body.Type} request body, which the spec doesn't document");
            }
        }

        /// <summary>
        /// A model is consistent by construction when it is the type generated from its schema. Any other type is compared
        /// with the schema as System.Text.Json would read and write it: consistent today (MM021), or each difference (MM022).
        /// </summary>
        private static IEnumerable<Diagnostic> VerifyModel(Emitter emitter, IOpenApiSchema schema, string actual, Endpoint endpoint, string where, string what)
        {
            var shapes = endpoint.Shapes.ToDictionary(s => s.Name, StringComparer.Ordinal);
            if (emitter.ByConstruction(schema, actual, shapes))
            {
                yield break;
            }

            var differences = emitter.Differences(schema, actual, shapes);
            if (differences.Count == 0)
            {
                yield return Diagnostic.Create(Diagnostics.ModelConsistent, endpoint.Location, where, what, TypeName(actual), Emitter.SchemaName(schema));
                yield break;
            }

            foreach (var difference in differences)
            {
                yield return Diagnostic.Create(Diagnostics.ModelDiffers, endpoint.Location, where,
                    $"{what} is {TypeName(actual)}, which differs from the schema {Emitter.SchemaName(schema)}: {difference}");
            }
        }

        public static IEnumerable<Diagnostic> Verify(Emitter emitter, IReadOnlyList<Endpoint> endpoints, Location specLocation)
        {
            var operations = emitter.Operations();
            var documented = endpoints.Where(e => !e.Excluded).ToList();
            var matched = new HashSet<Endpoint>();
            foreach (var operation in operations)
            {
                var route = Normalize(operation.Path);
                var endpoint = documented.FirstOrDefault(e => e.Method == operation.Method && Shape(e.Route) == Shape(route));
                if (endpoint is null)
                {
                    yield return Diagnostic.Create(Diagnostics.OperationNotImplemented, specLocation, operation.Id, operation.Method, operation.Path);
                    continue;
                }

                matched.Add(endpoint);
                var where = $"{endpoint.Display} ({operation.Method} {operation.Path})";
                if (endpoint.Name != operation.Id)
                {
                    yield return Diagnostic.Create(Diagnostics.OperationNameMismatch, endpoint.Location, where, endpoint.Name ?? "none", operation.Id);
                }

                foreach (var (actual, expected) in ParameterNames(endpoint.Route).Zip(ParameterNames(route), (a, e) => (a, e)))
                {
                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    {
                        yield return Diagnostic.Create(Diagnostics.RouteParameterMismatch, endpoint.Location, where, actual, expected);
                    }
                }

                var routeNamesMatch = ParameterNames(endpoint.Route).SequenceEqual(ParameterNames(route), StringComparer.Ordinal);
                if (endpoint.Bindings is { } bindings)
                {
                    foreach (var diagnostic in VerifyBindings(emitter, operation, endpoint, bindings, where, checkRoute: routeNamesMatch))
                    {
                        yield return diagnostic;
                    }
                }

                if (endpoint.Statuses is { } declared)
                {
                    foreach (var status in operation.Statuses.Except(declared))
                    {
                        yield return Diagnostic.Create(Diagnostics.ResponseNotDeclared, endpoint.Location, where, status,
                            endpoint.IsMap ? "a typed result or .Produces call" : "[ProducesResponseType]");
                    }

                    foreach (var status in declared.Except(operation.Statuses))
                    {
                        yield return Diagnostic.Create(Diagnostics.ResponseNotDocumented, endpoint.Location, where, status);
                    }
                }

                foreach (var (status, type) in endpoint.ResponseTypes)
                {
                    if (!operation.ResponseSchemas.TryGetValue(status, out var schema))
                    {
                        continue; // MM013 already reports it.
                    }

                    if (schema is null)
                    {
                        yield return Diagnostic.Create(Diagnostics.ModelDiffers, endpoint.Location, where,
                            $"the {status} response is {TypeName(type)}, but the spec's {status} response has no body");
                        continue;
                    }

                    foreach (var diagnostic in VerifyModel(emitter, schema, type, endpoint, where, $"the {status} response"))
                    {
                        yield return diagnostic;
                    }
                }
            }

            foreach (var endpoint in documented.Where(e => !matched.Contains(e)))
            {
                yield return Diagnostic.Create(Diagnostics.EndpointNotInSpec, endpoint.Location, endpoint.Display, endpoint.Method, endpoint.Route);
            }
        }
    }
}
