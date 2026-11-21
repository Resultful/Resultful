using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Resultful.Analyzers;

/// <summary>
/// RF0008: <c>error.Match(n =&gt; 404, c =&gt; 409)</c> on a closed hierarchy. Its handlers are positional, ordered by
/// the generator, so a renamed or new case can move them; an untyped lambda then handles the wrong case without a
/// compile error. With <c>(NotFound n) =&gt; 404</c> the compiler rejects the moved call.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UntypedHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.UntypedHandler);

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
        if (!IsMultiCase(method)) return;

        foreach (var argument in invocation.Arguments)
        {
            if (Lambda(argument.Value) is not { } lambda || IsTyped(lambda.Syntax)) continue;
            var parameter = lambda.Symbol.Parameters.FirstOrDefault()?.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "the case";
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.UntypedHandler, lambda.Syntax.GetLocation(),
                $"{method.ContainingType.Name}.{method.Name}", parameter));
        }
    }

    /// <summary>
    /// Match, Switch or TryMap on a [ClosedHierarchy] type with two or more case handlers, or on a [ResultShape] with two or
    /// more besides onSuccess. Core's Match(onSuccess, onError) is on neither, and a shape's two-handler Match is fixed.
    /// </summary>
    public static bool IsMultiCase(IMethodSymbol method)
    {
        if (method.Name is not ("Match" or "Switch" or "TryMap")) return false;
        var type = method.ContainingType;
        var handlers = method.Parameters.Count(p => p.Type.TypeKind == TypeKind.Delegate);
        if (Symbols.IsClosedHierarchy(type)) return handlers >= 2;
        return Symbols.IsResultShape(type) && handlers - 1 >= 2;
    }

    static IAnonymousFunctionOperation? Lambda(IOperation value) => value switch
    {
        IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } => lambda,
        IConversionOperation conversion => Lambda(conversion.Operand),
        _ => null,
    };

    /// <summary>Every parameter declares its type: `(NotFound n) =&gt; …`, `delegate (NotFound n) { … }`.</summary>
    public static bool IsTyped(SyntaxNode lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax => false,
        ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters.All(x => x.Type is not null),
        AnonymousMethodExpressionSyntax a => a.ParameterList is not null,
        _ => true,
    };
}
