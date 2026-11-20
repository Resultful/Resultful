using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Resultful.Analyzers;

/// <summary>
/// RF0001: a [MustObserve] value thrown away. A value is thrown away when it is the whole of an expression statement
/// (including the implicit one in a void lambda or an expression-bodied void member): a call, a <c>new</c>, an
/// <c>await</c> whose result is dropped, or a call that returns an unawaited task of one. An async lambda passed as a
/// void delegate to a [MustObserve] type's method (<c>Switch</c>, <c>Tee</c>, …) is async void, and is reported too.
/// Only an explicit <c>_ =</c> passes: it is an assignment, not a value left on its own.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustObserveAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Set on diagnostics the code fix can discard with <c>_ =</c>.</summary>
    public const string FixProperty = "Fix";
    public const string DiscardFix = "Discard";

    static readonly ImmutableDictionary<string, string?> Discardable = ImmutableDictionary<string, string?>.Empty.Add(FixProperty, DiscardFix);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.MustObserve, Descriptors.MustObserveAsyncVoid);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = new Symbols(start.Options);
            start.RegisterOperationAction(c => AnalyzeStatement(c, symbols), OperationKind.ExpressionStatement);
            start.RegisterOperationAction(c => AnalyzeHandlers(c, symbols), OperationKind.Invocation);
        });
    }

    static void AnalyzeStatement(OperationAnalysisContext context, Symbols symbols)
    {
        var expression = ((IExpressionStatementOperation)context.Operation).Operation;
        var phrase = Unobserved(expression, symbols, symbols.Configured(expression.Syntax.SyntaxTree));
        if (phrase is null) return;
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.MustObserve, expression.Syntax.GetLocation(), Discardable, phrase));
    }

    /// <summary>A phrase naming the value thrown away ("The Result&lt;int&gt; returned by Cancel"), or null if none is.</summary>
    static string? Unobserved(IOperation operation, Symbols symbols, ImmutableHashSet<string> configured)
    {
        switch (operation)
        {
            case IConditionalAccessOperation conditional:
                return Unobserved(conditional.WhenNotNull, symbols, configured);

            case IInvocationOperation invocation when !invocation.TargetMethod.ReturnsVoid:
                if (symbols.IsMustObserveType(invocation.Type, configured) || symbols.IsMustObserveMethod(invocation.TargetMethod, configured))
                    return $"The {Display(invocation.Type)} returned by {Name(invocation)}";
                // Fire-and-forget: a task (or any awaitable) of a [MustObserve] value, neither awaited nor discarded.
                var awaited = Symbols.AwaitedType(invocation.Type);
                return awaited is not null && symbols.IsMustObserveType(awaited, configured)
                    ? $"The {Display(awaited)} in the {Display(invocation.Type)} returned by {Name(invocation)}"
                    : null;

            case IObjectCreationOperation creation when symbols.IsMustObserveType(creation.Type, configured):
                return $"The {Display(creation.Type)} created here";

            case IAwaitOperation await when await.Type is { SpecialType: not SpecialType.System_Void }:
                var source = await.Operation as IInvocationOperation;
                return symbols.IsMustObserveType(await.Type, configured) || source is not null && symbols.IsMustObserveMethod(Unwrap(source).TargetMethod, configured)
                    ? $"The {Display(await.Type)} awaited from {(source is null ? "this await" : Name(source))}"
                    : null;

            default:
                return null;
        }
    }

    static void AnalyzeHandlers(OperationAnalysisContext context, Symbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var configured = symbols.Configured(invocation.Syntax.SyntaxTree);
        var onMustObserve = method.IsExtensionMethod
            ? method.Parameters.Length > 0 && symbols.IsMustObserveType(method.Parameters[0].Type, configured)
            : !method.IsStatic && symbols.IsMustObserveType(method.ContainingType, configured);
        if (!onMustObserve) return;

        foreach (var argument in invocation.Arguments)
        {
            if (Handler(argument.Value) is { Symbol: { IsAsync: true, ReturnsVoid: true } } handler)
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.MustObserveAsyncVoid, handler.Syntax.GetLocation(), method.Name));
        }
    }

    static IAnonymousFunctionOperation? Handler(IOperation value) => value switch
    {
        IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } => lambda,
        IConversionOperation conversion => Handler(conversion.Operand),
        _ => null,
    };

    // `CancelAsync(1).ConfigureAwait(false)` is named after CancelAsync.
    static IInvocationOperation Unwrap(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "ConfigureAwait" && invocation.Instance is IInvocationOperation inner ? Unwrap(inner) : invocation;

    static string Name(IInvocationOperation invocation) => Unwrap(invocation).TargetMethod.Name;

    static string Display(ITypeSymbol? type) => type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "value";
}
