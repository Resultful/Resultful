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
        public Endpoint(string method, string route, string? name, string display, Location location, bool excluded, ImmutableArray<int>? statuses)
        {
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

        public bool Equals(Endpoint? other)
            => other is not null && Method == other.Method && Route == other.Route && Name == other.Name && Display == other.Display
               && Excluded == other.Excluded && Location.Equals(other.Location)
               && (Statuses?.SequenceEqual(other.Statuses ?? ImmutableArray<int>.Empty) ?? other.Statuses is null);

        public override bool Equals(object? obj) => Equals(obj as Endpoint);

        public override int GetHashCode() => (Method, Route, Display).GetHashCode();
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
                yield return new Endpoint(verb.ToUpperInvariant(), Normalize(route), name, $"{type.Name}.{method.Name}", location, excluded, statuses);
            }
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
            foreach (var verb in verbs)
            {
                yield return new Endpoint(verb.ToUpperInvariant(), Normalize(Join(prefix, pattern)), endpointName,
                    $"{name}(\"{pattern}\")", invocation.GetLocation(), excluded, null);
            }
        }

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
