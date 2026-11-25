using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Resultful.Analyzers;

/// <summary>
/// What the rules know about a symbol, read from attributes by full name (decision 15): a library's own
/// <c>internal</c> copy of an attribute counts the same as the core's. No rule names a Resultful type, except that an
/// error union is a union whose cases are <c>Resultful.Error</c>s (decision 17).
/// </summary>
internal sealed class Symbols
{
    public const string MustObserveAttribute = "Resultful.CodeAnalysis.MustObserveAttribute";
    public const string ResultShapeAttribute = "Resultful.CodeAnalysis.ResultShapeAttribute";
    public const string ClosedHierarchyAttribute = "Resultful.CodeAnalysis.ClosedHierarchyAttribute";
    const string ErrorType = "Resultful.Error";
    const string ErrorUnionInterface = "Resultful.IErrorUnion";
    const string UnionInterface = "System.Runtime.CompilerServices.IUnion";

    readonly ConcurrentDictionary<ISymbol, bool> _mustObserve = new(SymbolEqualityComparer.Default);
    readonly ConcurrentDictionary<ITypeSymbol, bool> _resultType = new(SymbolEqualityComparer.Default);
    readonly ConcurrentDictionary<SyntaxTree, ImmutableHashSet<string>> _configured = new();
    readonly ConcurrentDictionary<SyntaxTree, ImmutableHashSet<string>> _configuredClosed = new();
    readonly AnalyzerConfigOptionsProvider _options;

    public Symbols(AnalyzerOptions options) => _options = options.AnalyzerConfigOptionsProvider;

    // ---- [MustObserve] ----

    /// <summary>Documentation IDs listed under <c>resultful.must_observe_types</c> for the tree's files.</summary>
    public ImmutableHashSet<string> Configured(SyntaxTree tree) => _configured.GetOrAdd(tree, t =>
        _options.GetOptions(t).TryGetValue(Descriptors.MustObserveTypesKey, out var value) ? ParseIds(value) : ImmutableHashSet<string>.Empty);

    /// <summary>Comma-separated documentation IDs; an entry without a `X:` prefix is a type (`T:`).</summary>
    static readonly char[] Separators = [',', ';'];

    public static ImmutableHashSet<string> ParseIds(string value) => value
        .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim())
        .Where(s => s.Length > 0)
        .Select(s => s.Length > 1 && s[1] == ':' ? s : "T:" + s)
        .ToImmutableHashSet(StringComparer.Ordinal);

    /// <summary>The type carries [MustObserve] or is listed in .editorconfig. A nullable struct counts as its underlying type.</summary>
    public bool IsMustObserveType(ITypeSymbol? type, ImmutableHashSet<string> configured)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];
        if (type is not INamedTypeSymbol named) return false;
        var definition = named.OriginalDefinition;
        return HasMustObserve(definition) || configured.Count > 0 && configured.Contains(definition.GetDocumentationCommentId() ?? "");
    }

    /// <summary>The method carries [MustObserve] or is listed in .editorconfig (as `M:…`).</summary>
    public bool IsMustObserveMethod(IMethodSymbol method, ImmutableHashSet<string> configured)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        return HasMustObserve(definition) || configured.Count > 0 && configured.Contains(definition.GetDocumentationCommentId() ?? "");
    }

    bool HasMustObserve(ISymbol symbol) => _mustObserve.GetOrAdd(symbol, s => HasAttribute(s, MustObserveAttribute));

    /// <summary>The type a value of this type gives when awaited (`GetAwaiter().GetResult()`), or null if it isn't awaitable.</summary>
    public static ITypeSymbol? AwaitedType(ITypeSymbol? type)
    {
        if (type is null) return null;
        var getAwaiter = Members<IMethodSymbol>(type, "GetAwaiter").FirstOrDefault(m => !m.IsStatic && m.Parameters.Length == 0 && m.TypeParameters.Length == 0);
        if (getAwaiter is null) return null;
        var getResult = Members<IMethodSymbol>(getAwaiter.ReturnType, "GetResult").FirstOrDefault(m => !m.IsStatic && m.Parameters.Length == 0);
        return getResult is null || getResult.ReturnsVoid ? null : getResult.ReturnType;
    }

    static IEnumerable<T> Members<T>(ITypeSymbol type, string name) where T : ISymbol
    {
        for (var t = type; t is not null; t = t.BaseType)
            foreach (var member in t.GetMembers(name).OfType<T>())
                yield return member;
    }

    // ---- results and error unions (RF0002) ----

    /// <summary>
    /// A struct whose zero value isn't a valid value: it forbids <c>new T()</c> with an <c>[Obsolete(…, error: true)]</c>
    /// parameterless constructor (decision 10, layer 2), or carries [ResultShape], or is an error union (decision 17).
    /// </summary>
    public bool IsResultType(ITypeSymbol? type) => type is INamedTypeSymbol { TypeKind: TypeKind.Struct } named
        && _resultType.GetOrAdd(named.OriginalDefinition, t => ComputeIsResultType((INamedTypeSymbol)t));

    static bool ComputeIsResultType(INamedTypeSymbol type) =>
        type.InstanceConstructors.Any(c => c.Parameters.Length == 0 && !c.IsImplicitlyDeclared && IsErrorObsolete(c))
        || HasAttribute(type, ResultShapeAttribute)
        || IsErrorUnion(type);

    static bool IsErrorObsolete(IMethodSymbol constructor) => constructor.GetAttributes().Any(a =>
        a.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute"
        && a.ConstructorArguments.Length == 2
        && a.ConstructorArguments[1].Value is true);

    public static bool IsUnion(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Struct && type.AllInterfaces.Any(i => i.ToDisplayString() == UnionInterface);

    /// <summary>A union whose cases are all errors, or nested error unions (decision 17 keeps a nested union as one case).</summary>
    public static bool IsErrorUnion(INamedTypeSymbol type) => IsErrorUnion(type, depth: 0);

    static bool IsErrorUnion(INamedTypeSymbol type, int depth)
    {
        if (!IsUnion(type)) return false;
        if (type.AllInterfaces.Any(i => i.ToDisplayString() == ErrorUnionInterface)) return true;
        var cases = UnionCaseTypes(type).ToList();
        return cases.Count > 0 && cases.All(c => c is INamedTypeSymbol n && (DerivesFrom(n, ErrorType) || depth < 8 && IsErrorUnion(n, depth + 1)));
    }

    /// <summary>A C# 15 union has one public single-parameter constructor per case; read through symbols, so the 4.14 build sees it.</summary>
    static IEnumerable<ITypeSymbol> UnionCaseTypes(INamedTypeSymbol union) => union.InstanceConstructors
        .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 1)
        .Select(c => c.Parameters[0].Type);

    /// <summary>The type is <paramref name="fullName"/> or derives from it.</summary>
    static bool DerivesFrom(INamedTypeSymbol type, string fullName)
    {
        for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
            if (t.ToDisplayString() == fullName) return true;
        return false;
    }

    // ---- [ClosedHierarchy] and [ResultShape] (RF0005, RF0007, RF0008, RF0010) ----

    /// <summary>Documentation IDs listed under <c>resultful.closed_hierarchy_types</c> for the tree's files.</summary>
    public ImmutableHashSet<string> ConfiguredClosed(SyntaxTree tree) => _configuredClosed.GetOrAdd(tree, t =>
        _options.GetOptions(t).TryGetValue(Descriptors.ClosedHierarchyTypesKey, out var value) ? ParseIds(value) : ImmutableHashSet<string>.Empty);

    public static bool IsClosedHierarchy(INamedTypeSymbol type) => HasAttribute(type.OriginalDefinition, ClosedHierarchyAttribute);

    public static bool IsResultShape(INamedTypeSymbol type) => HasAttribute(type.OriginalDefinition, ResultShapeAttribute);

    static bool HasAttribute(ISymbol symbol, string fullName) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == fullName);
}
