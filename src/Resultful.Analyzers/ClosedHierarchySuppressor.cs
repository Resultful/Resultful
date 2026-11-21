using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Resultful.Analyzers;

/// <summary>
/// RFSP0001 (decision 15): the compiler's CS8509 on a switch expression over a closed base, suppressed when the switch
/// handles every value that can reach it. The base, or a base of it, carries [ClosedHierarchy] (by full name) or is listed
/// under resultful.closed_hierarchy_types.
/// </summary>
/// <remarks>
/// <para>
/// Suppressed only when all of these hold:
/// the input's type is a non-generic class declared in the closed base's assembly; every non-abstract type in that
/// assembly that derives from it (or is it), internal ones included, is matched by an arm without a <c>when</c> guard
/// whose pattern is a type (<c>NotFound</c>), a declaration (<c>NotFound n</c>), an empty property pattern
/// (<c>NotFound { }</c>), or an <c>or</c> of them; no type outside that assembly derives from it, in this compilation or
/// in a referenced assembly that references the base's (RF0010 reports such a type where it is compiled); and, the
/// null-arm rule, when the compiler's nullable analysis says the input may be null at the switch, an unguarded arm
/// matches <c>null</c> (<c>null =&gt; …</c>, or <c>null or …</c>).
/// </para>
/// <para>
/// The null-arm rule follows the compiler's own: where it would report CS8655 for an otherwise exhaustive switch, the
/// suppressor keeps CS8509. So <c>OrderError? e</c> needs a <c>null</c> arm, the same variable after
/// <c>if (e is null) return;</c> doesn't, and code with nullable annotations disabled doesn't either (the compiler
/// asks for no null arm there).
/// </para>
/// <para>
/// Arms and leaves are compared as symbols, and an arm counts only if its type derives from the input, so a type of the
/// same name from another assembly (an extern alias) or from source matches nothing. A generic leaf is matched only by
/// an arm on a non-generic base of it.
/// </para>
/// <para>
/// The suppression is a compile-time snapshot of the base's assembly and of the referenced assemblies that reference
/// it. A later version of the base's assembly that adds a leaf, or an assembly this one doesn't reference that derives
/// from the base (an <c>InternalsVisibleTo</c> friend, or code compiled without the analyzers, which another assembly
/// can do through a closed record's copy constructor, CS8878), makes the switch throw <c>SwitchExpressionException</c>
/// until the switch is recompiled against it.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClosedHierarchySuppressor : DiagnosticSuppressor
{
    /// <inheritdoc />
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } = ImmutableArray.Create(Descriptors.ClosedHierarchySwitch);

    /// <inheritdoc />
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        Symbols? symbols = null;
        Hierarchies? hierarchies = null;
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id != Descriptors.ClosedHierarchySwitch.SuppressedDiagnosticId || diagnostic.Location.SourceTree is not { } tree) continue;
            var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node.FirstAncestorOrSelf<SwitchExpressionSyntax>() is not { } syntax) continue;
            var model = context.GetSemanticModel(tree);
            if (model.GetOperation(syntax, context.CancellationToken) is not ISwitchExpressionOperation operation) continue;

            symbols ??= new Symbols(context.Options);
            hierarchies ??= new Hierarchies(context.Compilation, context.CancellationToken);
            if (IsComplete(operation, model, symbols.ConfiguredClosed(tree), hierarchies, context.CancellationToken))
                context.ReportSuppression(Suppression.Create(Descriptors.ClosedHierarchySwitch, diagnostic));
        }
    }

    static bool IsComplete(ISwitchExpressionOperation operation, SemanticModel model, ImmutableHashSet<string> configured, Hierarchies hierarchies,
        CancellationToken cancellationToken)
    {
        if (operation.Value.Type is not INamedTypeSymbol { TypeKind: TypeKind.Class } input || IsGeneric(input)) return false;
        input = input.OriginalDefinition;
        INamedTypeSymbol? root = null;
        for (var t = input; t is not null && root is null; t = t.BaseType)
            if (Symbols.IsClosedHierarchy(t) || configured.Count > 0 && configured.Contains(t.OriginalDefinition.GetDocumentationCommentId() ?? ""))
                root = t;
        if (root is null || !SymbolEqualityComparer.Default.Equals(root.ContainingAssembly, input.ContainingAssembly)) return false;

        if (hierarchies.Leaves(input) is not { } leaves) return false;

        // Matched by symbol, so a type with the same name from another assembly (an extern alias) or from source covers nothing.
        var matched = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var matchesNull = false;
        foreach (var arm in operation.Arms)
            if (arm.Guard is null)
                Collect(arm.Pattern, input, matched, ref matchesNull);

        if (!matchesNull && model.GetTypeInfo(operation.Value.Syntax, cancellationToken).Nullability.FlowState == NullableFlowState.MaybeNull) return false;
        return leaves.All(leaf => Bases(leaf).Any(matched.Contains));
    }

    /// <summary>The types an unguarded pattern matches whole that derive from the input, and whether it matches null.</summary>
    static void Collect(IPatternOperation pattern, INamedTypeSymbol input, HashSet<INamedTypeSymbol> matched, ref bool matchesNull)
    {
        switch (pattern)
        {
            case ITypePatternOperation type:
                Add(type.MatchedType, input, matched);
                break;
            case IDeclarationPatternOperation { MatchesNull: false } declaration:
                Add(declaration.MatchedType, input, matched);
                break;
            case IRecursivePatternOperation { DeconstructSymbol: null, DeconstructionSubpatterns.IsEmpty: true, PropertySubpatterns.IsEmpty: true } recursive:
                Add(recursive.MatchedType, input, matched);
                break;
            case IBinaryPatternOperation { OperatorKind: BinaryOperatorKind.Or } or:
                Collect(or.LeftPattern, input, matched, ref matchesNull);
                Collect(or.RightPattern, input, matched, ref matchesNull);
                break;
            case IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: null } }:
                matchesNull = true;
                break;
        }
    }

    static void Add(ITypeSymbol? type, INamedTypeSymbol input, HashSet<INamedTypeSymbol> matched)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Class } named && !IsGeneric(named) && Bases(named).Contains(input, SymbolEqualityComparer.Default))
            matched.Add(named.OriginalDefinition);
    }

    /// <summary>The type and its bases that aren't generic, which are the ones an arm can name whole.</summary>
    static IEnumerable<INamedTypeSymbol> Bases(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (!IsGeneric(t))
                yield return t.OriginalDefinition;
    }

    static bool IsGeneric(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.ContainingType)
            if (t.IsGenericType) return true;
        return false;
    }

    /// <summary>
    /// The types that derive from a closed base, read from one map per assembly (each type under its base's definition),
    /// built once: per compilation for its own source, and for a referenced assembly once for as long as its symbol lives,
    /// so compilations that share it share the map. A referenced assembly's types include its internal and private nested
    /// ones, which no arm in another assembly can name.
    /// </summary>
    sealed class Hierarchies(Compilation compilation, CancellationToken cancellationToken)
    {
        static readonly ConditionalWeakTable<IAssemblySymbol, Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>> Referenced = new();

        readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>?> _leaves = new(SymbolEqualityComparer.Default);
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>? _source;
        List<IAssemblySymbol>? _referencing;
        IAssemblySymbol? _referencingHome;

        /// <summary>The non-abstract types that derive from the input (or are it), or null when one is outside its assembly.</summary>
        public List<INamedTypeSymbol>? Leaves(INamedTypeSymbol input)
        {
            if (!_leaves.TryGetValue(input, out var leaves))
                _leaves[input] = leaves = Compute(input);
            return leaves;
        }

        List<INamedTypeSymbol>? Compute(INamedTypeSymbol input)
        {
            var home = input.ContainingAssembly;
            var map = Map(home);
            var closure = new List<INamedTypeSymbol> { input };
            var seen = new HashSet<INamedTypeSymbol>(closure, SymbolEqualityComparer.Default);
            for (var i = 0; i < closure.Count; i++)
                if (map.TryGetValue(closure[i], out var derived))
                    foreach (var d in derived)
                        if (seen.Add(d)) closure.Add(d);

            // Every other assembly that can hold a derived type: this one, and the references that reference the base's.
            if (!SymbolEqualityComparer.Default.Equals(home, compilation.Assembly))
                foreach (var assembly in Referencing(home).Prepend(compilation.Assembly))
                {
                    var other = Map(assembly);
                    if (closure.Any(other.ContainsKey)) return null;
                }

            return [.. closure.Where(t => !t.IsAbstract)];
        }

        List<IAssemblySymbol> Referencing(IAssemblySymbol home)
        {
            if (_referencing is null || !SymbolEqualityComparer.Default.Equals(_referencingHome, home))
            {
                _referencingHome = home;
                _referencing = [.. compilation.SourceModule.ReferencedAssemblySymbols.Where(a => !SymbolEqualityComparer.Default.Equals(a, home)
                    && a.Modules.Any(m => m.ReferencedAssemblySymbols.Contains(home, SymbolEqualityComparer.Default)))];
            }

            return _referencing;
        }

        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> Map(IAssemblySymbol assembly)
        {
            if (SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly))
                return _source ??= Build(assembly, cancellationToken);
            var token = cancellationToken;
            return Referenced.GetValue(assembly, a => Build(a, token));
        }

        static Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> Build(IAssemblySymbol assembly, CancellationToken cancellationToken)
        {
            var map = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
            foreach (var type in Types(assembly.GlobalNamespace, cancellationToken))
            {
                if (type.TypeKind != TypeKind.Class || type.BaseType is not { } baseType) continue;
                var key = baseType.OriginalDefinition;
                if (!map.TryGetValue(key, out var list)) map[key] = list = [];
                list.Add(type.OriginalDefinition);
            }

            return map;
        }

        static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol container, CancellationToken cancellationToken)
        {
            foreach (var member in container.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member is INamespaceSymbol ns)
                {
                    foreach (var t in Types(ns, cancellationToken)) yield return t;
                }
                else if (member is INamedTypeSymbol type)
                {
                    yield return type;
                    foreach (var t in Types(type, cancellationToken)) yield return t;
                }
            }
        }
    }
}
