using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Resultful.Analyzers;

/// <summary>
/// RF0007, a [ClosedHierarchy] base another assembly can derive from through a constructor, and RF0010, a type that
/// does derive from one in another assembly (decision 15). Both read the attribute by full name; RF0010 also reads
/// resultful.closed_hierarchy_types for third-party bases.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClosedHierarchyAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Set on RF0007 diagnostics the fix can apply: <see cref="MakePrivateProtected"/> or <see cref="AddConstructor"/>.</summary>
    public const string FixProperty = "Fix";
    /// <summary>The declared constructors become private protected.</summary>
    public const string MakePrivateProtected = "Modifiers";
    /// <summary>A parameterless private protected constructor replaces the implicit one.</summary>
    public const string AddConstructor = "Add";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.PrivateProtected, Descriptors.PrivateProtectedPositional, Descriptors.ForeignDerivedType);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = new Symbols(start.Options);
            start.RegisterSymbolAction(c => Analyze(c, symbols), SymbolKind.NamedType);
        });
    }

    static void Analyze(SymbolAnalysisContext context, Symbols symbols)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } type || type.Locations.FirstOrDefault(l => l.IsInSource) is not { } location)
            return;

        var configured = symbols.ConfiguredClosed(location.SourceTree!);
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(b.ContainingAssembly, type.ContainingAssembly)) continue;
            if (!Symbols.IsClosedHierarchy(b) && !(configured.Count > 0 && configured.Contains(b.OriginalDefinition.GetDocumentationCommentId() ?? ""))) continue;
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.ForeignDerivedType, location, type.Name,
                b.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), b.ContainingAssembly.Name));
            break;
        }

        if (!Symbols.IsClosedHierarchy(type) || !IsVisibleOutside(type)) return;
        var open = type.InstanceConstructors.Where(c => !IsCopyConstructor(c, type) && IsVisibleOutside(c.DeclaredAccessibility)).ToList();
        if (open.Count == 0) return;

        string? fix = null;
        if (open.All(c => !c.IsImplicitlyDeclared && c.DeclaringSyntaxReferences.All(r => r.GetSyntax(context.CancellationToken) is ConstructorDeclarationSyntax)))
            fix = MakePrivateProtected;
        else if (open.Count == 1 && open[0].IsImplicitlyDeclared && type.BaseType?.InstanceConstructors.Any(c => c.Parameters.All(p => p.IsOptional) && c.DeclaredAccessibility != Accessibility.Private) == true)
            fix = AddConstructor;
        var properties = fix is null ? null : ImmutableDictionary<string, string?>.Empty.Add(FixProperty, fix);
        var positional = open.Any(c => c.DeclaringSyntaxReferences.Any(r => r.GetSyntax(context.CancellationToken) is TypeDeclarationSyntax));
        context.ReportDiagnostic(Diagnostic.Create(positional ? Descriptors.PrivateProtectedPositional : Descriptors.PrivateProtected, location, properties,
            type.Name, Display(open[0].DeclaredAccessibility)));
    }

    // CS8878: a non-sealed record's copy constructor must be public or protected.
    static bool IsCopyConstructor(IMethodSymbol constructor, INamedTypeSymbol type) =>
        type.IsRecord && constructor.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, type);

    static bool IsVisibleOutside(Accessibility accessibility) =>
        accessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;

    /// <summary>Another assembly can see the type: it and every type it is nested in are public or protected.</summary>
    static bool IsVisibleOutside(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.ContainingType)
            if (!IsVisibleOutside(t.DeclaredAccessibility)) return false;
        return true;
    }

    static string Display(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.ProtectedOrInternal => "protected internal",
        _ => "protected",
    };
}
