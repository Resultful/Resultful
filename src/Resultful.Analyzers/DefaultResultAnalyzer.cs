using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Resultful.Analyzers;

/// <summary>
/// RF0002 (decision 10 layer 3, decision 17 point 5): <c>default(Result&lt;T&gt;)</c>, a <c>default</c> literal where a
/// result is expected, and <c>new U()</c> or <c>default</c> of an error union. A result is a struct that forbids
/// <c>new T()</c> with an <c>[Obsolete(…, error: true)]</c> parameterless constructor, or carries [ResultShape]; so a
/// user's own result type is checked the same way, and <c>Option&lt;T&gt;</c>, whose zero value is <c>None</c>, is not.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefaultResultAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.DefaultResult);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = new Symbols(start.Options);
            start.RegisterOperationAction(c => AnalyzeDefault(c, symbols), OperationKind.DefaultValue);
            start.RegisterOperationAction(c => AnalyzeCreation(c, symbols), OperationKind.ObjectCreation);
        });
    }

    static void AnalyzeDefault(OperationAnalysisContext context, Symbols symbols)
    {
        var operation = (IDefaultValueOperation)context.Operation;
        if (symbols.IsResultType(operation.Type) && !IsComparison(operation)) Report(context, operation);
    }

    /// <summary>
    /// `r == default`, `r != default`, `r.Equals(default(Result&lt;int&gt;))` and `default` inside a pattern compare with the zero
    /// value rather than create one to use, so they are left alone (a pattern also has the compiler's own rules, CS9135).
    /// </summary>
    static bool IsComparison(IOperation operation)
    {
        var node = operation;
        while (node.Parent is IConversionOperation or IParenthesizedOperation) node = node.Parent;
        switch (node.Parent)
        {
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals }:
                return true;
            case IArgumentOperation { Parent: IInvocationOperation { TargetMethod.Name: nameof(Equals) } }:
                return true;
        }

        for (var ancestor = operation.Parent; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor is IPatternOperation) return true;
        return false;
    }

    // `new OrderError()` on a union (or any result without a declared parameterless constructor) is its zero value.
    static void AnalyzeCreation(OperationAnalysisContext context, Symbols symbols)
    {
        var operation = (IObjectCreationOperation)context.Operation;
        if (operation.Constructor is { Parameters.Length: 0, IsImplicitlyDeclared: true } && symbols.IsResultType(operation.Type))
            Report(context, operation);
    }

    static void Report(OperationAnalysisContext context, IOperation operation) =>
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.DefaultResult, operation.Syntax.GetLocation(),
            operation.Type!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
}
