using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Resultful.Analyzers;

/// <summary>
/// RF0004: <c>Result&lt;string&gt; F() =&gt; "not found";</c>. A string literal (or interpolated string, or a
/// concatenation with one) converted implicitly to a result's success. Before 0.3 the same line was a failure.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BareStringAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Set when the target is Resultful.Result&lt;string&gt;, which the fix knows how to wrap.</summary>
    public const string FixProperty = "Fix";

    static readonly ImmutableDictionary<string, string?> Wrappable = ImmutableDictionary<string, string?>.Empty.Add(FixProperty, "Wrap");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.BareString);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = new Symbols(start.Options);
            start.RegisterOperationAction(c => Analyze(c, symbols), OperationKind.Conversion);
        });
    }

    static void Analyze(OperationAnalysisContext context, Symbols symbols)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (!conversion.IsImplicit || conversion.OperatorMethod is not { } op || op.Parameters.Length != 1) return;
        if (conversion.Operand.Type?.SpecialType != SpecialType.System_String || !IsBare(conversion.Operand.Syntax)) return;
        if (!IsSuccessConversion(op, symbols, symbols.Configured(conversion.Syntax.SyntaxTree))) return;

        var target = (INamedTypeSymbol)op.ContainingType;
        var fixable = target.OriginalDefinition.ToDisplayString() == "Resultful.Result<T>";
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.BareString, conversion.Operand.Syntax.GetLocation(),
            fixable ? Wrappable : null, target.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    /// <summary>
    /// A generic result's conversion from its first type parameter (the value; `Result&lt;int, string&gt;`'s conversion from
    /// TError is a failure), or a [ResultShape]'s conversion from string (a shape's errors are Error records).
    /// </summary>
    static bool IsSuccessConversion(IMethodSymbol op, Symbols symbols, ImmutableHashSet<string> configured)
    {
        var type = op.ContainingType;
        if (type.IsGenericType)
        {
            var definition = op.OriginalDefinition;
            return symbols.IsMustObserveType(type, configured)
                && SymbolEqualityComparer.Default.Equals(definition.Parameters[0].Type, definition.ContainingType.TypeParameters[0]);
        }

        return Symbols.IsResultShape(type);
    }

    static bool IsBare(SyntaxNode syntax) => syntax switch
    {
        ParenthesizedExpressionSyntax p => IsBare(p.Expression),
        LiteralExpressionSyntax literal => literal.Token.Value is string,
        InterpolatedStringExpressionSyntax => true,
        BinaryExpressionSyntax add when add.IsKind(SyntaxKind.AddExpression) => IsBare(add.Left) || IsBare(add.Right),
        _ => false,
    };
}
