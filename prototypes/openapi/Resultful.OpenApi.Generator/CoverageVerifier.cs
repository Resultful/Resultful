using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Resultful.OpenApi.Generator
{
    /// <summary>An endpoint found in hand-written code: a controller action or a Minimal API Map call.</summary>
    internal sealed class Endpoint : IEquatable<Endpoint>
    {
        public Endpoint(string method, string route, string? name, string display, Location location, bool excluded, ImmutableArray<int>? statuses,
            ImmutableArray<Binding>? bindings)
        {
            Bindings = bindings;
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

        public Location Location { get; }

        /// <summary>Left out of the OpenAPI description on purpose, so not part of the spec.</summary>
        public bool Excluded { get; }

        /// <summary>The declared response statuses; null when the endpoint's responses can't be read (Minimal API).</summary>
        public ImmutableArray<int>? Statuses { get; }

        /// <summary>What the handler binds from the request; null when the handler can't be read.</summary>
        public ImmutableArray<Binding>? Bindings { get; }

        public bool Equals(Endpoint? other)
            => other is not null && Method == other.Method && Route == other.Route && Name == other.Name && Display == other.Display
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
        public Binding(string source, string name, string type, bool optional, string parameter)
        {
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

        public bool Equals(Binding? other)
            => other is not null && Source == other.Source && Name == other.Name && Type == other.Type && Optional == other.Optional && Parameter == other.Parameter;

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
            var statuses = method.GetAttributes().Concat(type.GetAttributes())
                .Where(a => IsMvc(a, "ProducesResponseTypeAttribute") || IsMvc(a, "ProducesResponseTypeAttribute`1"))
                .Select(a => a.ConstructorArguments.FirstOrDefault(c => c.Type?.SpecialType == SpecialType.System_Int32).Value)
                .OfType<int>()
                .Distinct()
                .OrderBy(s => s)
                .ToImmutableArray();
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
                yield return new Endpoint(verb.ToUpperInvariant(), normalized, name, $"{type.Name}.{method.Name}", location, excluded, statuses,
                    Bindings(method, normalized, complexFromBody: apiController));
            }
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

            var (prefix, groupExcluded) = Group(member.Expression, context.SemanticModel, ct);
            var chain = Chain(invocation).ToList();
            var excluded = groupExcluded || chain.Any(c => c.Name == "ExcludeFromDescription");
            var endpointName = chain.Where(c => c.Name == "WithName").Select(c => c.Argument).FirstOrDefault();
            var route = Normalize(Join(prefix, pattern));
            var handlerIndex = name == "MapMethods" ? 2 : 1;
            var handler = invocation.ArgumentList.Arguments.Count > handlerIndex
                ? Handler(invocation.ArgumentList.Arguments[handlerIndex].Expression, context.SemanticModel, ct)
                : null;
            var bindings = handler is null ? (ImmutableArray<Binding>?)null : Bindings(handler, route, complexFromBody: true);
            foreach (var verb in verbs)
            {
                yield return new Endpoint(verb.ToUpperInvariant(), route, endpointName,
                    $"{name}(\"{pattern}\")", invocation.GetLocation(), excluded, null, bindings);
            }
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

                bindings.Add(new Binding(source, name, display, optional, parameter.Name));
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
        private static IEnumerable<(string Name, string? Argument)> Chain(InvocationExpressionSyntax invocation)
        {
            SyntaxNode current = invocation;
            while (current.Parent is MemberAccessExpressionSyntax access && access.Parent is InvocationExpressionSyntax next)
            {
                yield return (access.Name.Identifier.ValueText, next.ArgumentList.Arguments.Count > 0 ? Literal(next.ArgumentList.Arguments[0].Expression) : null);
                current = next;
            }
        }

        /// <summary>The route prefix (and exclusion) of the group a Map call is made on, when it can be read from source.</summary>
        private static (string? Prefix, bool Excluded) Group(ExpressionSyntax receiver, SemanticModel model, System.Threading.CancellationToken ct)
        {
            if (receiver is IdentifierNameSyntax identifier
                && model.GetSymbolInfo(identifier, ct).Symbol is ILocalSymbol local
                && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct) is VariableDeclaratorSyntax { Initializer.Value: { } value })
            {
                receiver = value;
            }

            string? prefix = null;
            var excluded = false;
            foreach (var node in receiver.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (node.Expression is MemberAccessExpressionSyntax access)
                {
                    if (access.Name.Identifier.ValueText == "MapGroup" && node.ArgumentList.Arguments.Count > 0)
                    {
                        prefix = Literal(node.ArgumentList.Arguments[0].Expression) ?? prefix;
                    }

                    excluded |= access.Name.Identifier.ValueText == "ExcludeFromDescription";
                }
            }

            return (prefix, excluded);
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

        private static IEnumerable<Diagnostic> VerifyBindings(SpecOperation operation, Endpoint endpoint, ImmutableArray<Binding> bindings, string where, bool checkRoute)
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
                else if (body.Type != TypeName(expected))
                {
                    yield return Diagnostic.Create(Diagnostics.BindingTypeMismatch, endpoint.Location, where, "the request body", body.Type, TypeName(expected));
                }
            }
            else if (body is not null)
            {
                yield return Diagnostic.Create(Diagnostics.BodyMismatch, endpoint.Location, where,
                    $"'{body.Parameter}' binds a {body.Type} request body, which the spec doesn't document");
            }
        }

        public static IEnumerable<Diagnostic> Verify(IReadOnlyList<SpecOperation> operations, IReadOnlyList<Endpoint> endpoints, Location specLocation)
        {
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
                    foreach (var diagnostic in VerifyBindings(operation, endpoint, bindings, where, checkRoute: routeNamesMatch))
                    {
                        yield return diagnostic;
                    }
                }

                if (endpoint.Statuses is { } declared)
                {
                    foreach (var status in operation.Statuses.Except(declared))
                    {
                        yield return Diagnostic.Create(Diagnostics.ResponseNotDeclared, endpoint.Location, where, status);
                    }

                    foreach (var status in declared.Except(operation.Statuses))
                    {
                        yield return Diagnostic.Create(Diagnostics.ResponseNotDocumented, endpoint.Location, where, status);
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
