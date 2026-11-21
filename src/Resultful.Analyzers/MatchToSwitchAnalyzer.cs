using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Resultful.Analyzers;

/// <summary>
/// RF0005: <c>error.Match((NotFound n) =&gt; 404, (Conflict c) =&gt; 409)</c> on a closed hierarchy, where every handler
/// is an expression, reads better as <c>error switch { NotFound n =&gt; 404, Conflict c =&gt; 409 }</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MatchToSwitchAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.MatchToSwitch);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    static void Analyze(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name != "Match" || method.IsStatic || invocation.Instance is null || !Symbols.IsClosedHierarchy(method.ContainingType)) return;
        // `e?.Match(…)`, `e.Match<int>(…)` and a dropped Match don't map onto a switch expression one to one.
        if (invocation.Syntax is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name: IdentifierNameSyntax } access }
            || invocation.Parent is IExpressionStatementOperation) return;
        if (invocation.Arguments.Length == 0 || !invocation.Arguments.All(a => IsExpressionHandler(a.Value))) return;
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.MatchToSwitch, access.Name.GetLocation(), method.ContainingType.Name));
    }

    /// <summary>A method group, or a non-async lambda with one typed parameter and an expression body (RF0008 asks for the type first).</summary>
    static bool IsExpressionHandler(IOperation value) => value switch
    {
        IConversionOperation conversion => IsExpressionHandler(conversion.Operand),
        IDelegateCreationOperation { Target: IMethodReferenceOperation } => true,
        IDelegateCreationOperation { Target: IAnonymousFunctionOperation { Symbol.IsAsync: false, Symbol.Parameters.Length: 1, Syntax: LambdaExpressionSyntax { ExpressionBody: not null } lambda } } => UntypedHandlerAnalyzer.IsTyped(lambda),
        _ => false,
    };
}
