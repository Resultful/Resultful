using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Resultful.Analyzers;

/// <summary>
/// RF0006 (decision 10 layer 3): a result field, or get-only auto-property, in a class that a constructor can leave
/// unassigned, so it holds the zero value. Constructors are read as a symbol's operation blocks; their writes are
/// compared with the candidates when the class ends.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnassignedFieldAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.UnassignedField);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = new Symbols(start.Options);
            start.RegisterSymbolStartAction(s => Start(s, symbols), SymbolKind.NamedType);
        });
    }

    static void Start(SymbolStartAnalysisContext context, Symbols symbols)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } type) return;
        var candidates = Candidates(type, symbols).ToList();
        if (candidates.Count == 0) return;

        var written = new ConcurrentDictionary<IMethodSymbol, HashSet<ISymbol>>(SymbolEqualityComparer.Default);
        context.RegisterOperationBlockAction(block =>
        {
            if (block.OwningSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } constructor) return;
            var set = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var operation in block.OperationBlocks.SelectMany(b => b.DescendantsAndSelf()))
            {
                var member = operation switch
                {
                    IFieldReferenceOperation field when IsWritten(field) => field.Field.OriginalDefinition,
                    IPropertyReferenceOperation property when IsWritten(property) => (ISymbol)property.Property.OriginalDefinition,
                    _ => null,
                };
                if (member is not null) set.Add(member);
            }

            written[constructor] = set;
        });

        context.RegisterSymbolEndAction(end =>
        {
            foreach (var (member, memberType, location) in candidates)
            {
                var constructors = member.IsStatic ? type.StaticConstructors : type.InstanceConstructors;
                var assigned = !constructors.IsEmpty && constructors.All(c => Assigns(c, member, type, written));
                if (!assigned)
                    end.ReportDiagnostic(Diagnostic.Create(Descriptors.UnassignedField, location, member.Name,
                        memberType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        });
    }

    /// <summary>Fields and get-only auto-properties of a result type, without an initializer, not `required`.</summary>
    static IEnumerable<(ISymbol Member, ITypeSymbol Type, Location Location)> Candidates(INamedTypeSymbol type, Symbols symbols)
    {
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.IsConst || !symbols.IsResultType(field.Type)) continue;
            ISymbol member = field;
            if (field.IsImplicitlyDeclared)
            {
                // An auto-property's backing field; a settable or init property may be set by an object initializer.
                if (field.AssociatedSymbol is not IPropertySymbol { SetMethod: null } property) continue;
                member = property;
            }

            if (member is IFieldSymbol { IsRequired: true } or IPropertySymbol { IsRequired: true }) continue;
            var syntax = member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var hasInitializer = syntax switch
            {
                VariableDeclaratorSyntax declarator => declarator.Initializer is not null,
                PropertyDeclarationSyntax property => property.Initializer is not null || property.ExpressionBody is not null,
                _ => true, // a positional record's property is assigned from its parameter
            };
            if (hasInitializer) continue;
            var location = syntax switch
            {
                VariableDeclaratorSyntax declarator => declarator.Identifier.GetLocation(),
                PropertyDeclarationSyntax property => property.Identifier.GetLocation(),
                _ => member.Locations[0],
            };
            yield return (member, field.Type, location);
        }
    }

    static bool Assigns(IMethodSymbol constructor, ISymbol member, INamedTypeSymbol type, ConcurrentDictionary<IMethodSymbol, HashSet<ISymbol>> written)
    {
        if (constructor.IsImplicitlyDeclared)
            // A record's copy constructor copies every field; the implicit parameterless one assigns none.
            return constructor.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, type);
        var syntax = constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is ConstructorDeclarationSyntax { Initializer: { } initializer } && initializer.ThisOrBaseKeyword.Text == "this") return true;
        return written.TryGetValue(constructor, out var set) && set.Contains(member);
    }

    /// <summary>The reference is assigned: `x = …`, `x += …`, `x ??= …`, `out x`, `ref x`, or a deconstruction's target.</summary>
    static bool IsWritten(IOperation reference)
    {
        var node = reference;
        while (node.Parent is ITupleOperation or IConversionOperation) node = node.Parent;
        return node.Parent switch
        {
            IAssignmentOperation assignment => assignment.Target == node,
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Out or RefKind.Ref,
            _ => false,
        };
    }
}
