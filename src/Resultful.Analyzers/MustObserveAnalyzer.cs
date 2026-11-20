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
        ImmutableArray.Create(Descriptors.MustObserve, Descriptors.MustObserveAsyncVoid, Descriptors.MustObserveUpcast);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = new Symbols(start.Options);
            start.RegisterOperationAction(c => AnalyzeStatement(c, symbols), OperationKind.ExpressionStatement);
            start.RegisterOperationAction(c => AnalyzeHandlers(c, symbols), OperationKind.Invocation);
            start.RegisterOperationAction(c => AnalyzeTaskUpcast(c, symbols), OperationKind.Conversion);
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
                if (IsMustObserveValue(invocation.Type, symbols, configured) || symbols.IsMustObserveMethod(invocation.TargetMethod, configured))
                    return $"The {Display(invocation.Type)} returned by {Name(invocation)}";
                // Fire-and-forget: a task (or any awaitable) of a [MustObserve] value, neither awaited nor discarded.
                var awaited = Symbols.AwaitedType(invocation.Type);
                return IsMustObserveValue(awaited, symbols, configured)
                    ? $"The {Display(awaited)} in the {Display(invocation.Type)} returned by {Name(invocation)}"
                    : null;

            case IObjectCreationOperation creation when symbols.IsMustObserveType(creation.Type, configured):
                return $"The {Display(creation.Type)} created here";

            case IAwaitOperation await when await.Type is { SpecialType: not SpecialType.System_Void }:
                var source = await.Operation as IInvocationOperation;
                return IsMustObserveValue(await.Type, symbols, configured) || source is not null && symbols.IsMustObserveMethod(Unwrap(source).TargetMethod, configured)
                    ? $"The {Display(await.Type)} awaited from {(source is null ? "this await" : Name(source))}"
                    : null;

            default:
                return null;
        }
    }

    /// <summary>A [MustObserve] value, or an array of them (`await Task.WhenAll(…)` of results).</summary>
    static bool IsMustObserveValue(ITypeSymbol? type, Symbols symbols, ImmutableHashSet<string> configured) =>
        type is IArrayTypeSymbol array ? symbols.IsMustObserveType(array.ElementType, configured) : symbols.IsMustObserveType(type, configured);

    static void AnalyzeHandlers(OperationAnalysisContext context, Symbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var configured = symbols.Configured(invocation.Syntax.SyntaxTree);
        // The receiver is a [MustObserve] value, or a task of one (`Switch<T>(this Task<Result<T>>, …)`, `Tee` on a ValueTask).
        var receiver = method.IsExtensionMethod
            ? method.Parameters.Length > 0 ? method.Parameters[0].Type : null
            : method.IsStatic ? null : method.ContainingType;
        if (receiver is null || !symbols.IsMustObserveType(receiver, configured) && !symbols.IsMustObserveType(Symbols.AwaitedType(receiver), configured)) return;

        foreach (var argument in invocation.Arguments)
        {
            if (AsyncVoidHandler(argument.Value) is { } handler)
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.MustObserveAsyncVoid, handler.GetLocation(), method.Name));
        }
    }

    /// <summary>An async lambda or anonymous method, or a method group naming an async void method, given as a void delegate.</summary>
    static SyntaxNode? AsyncVoidHandler(IOperation value) => value switch
    {
        IDelegateCreationOperation { Target: IAnonymousFunctionOperation { Symbol: { IsAsync: true, ReturnsVoid: true } } lambda } => lambda.Syntax,
        IDelegateCreationOperation { Target: IMethodReferenceOperation { Method: { IsAsync: true, ReturnsVoid: true } } group } => group.Syntax,
        IConversionOperation conversion => AsyncVoidHandler(conversion.Operand),
        _ => null,
    };

    /// <summary>
    /// A fresh task of a [MustObserve] value converted to the non-generic Task (or ValueTask): `Func&lt;Task&gt; f = () =&gt;
    /// MakeAsync(1);`. Whoever awaits it can no longer see the result. Only a call or a `new` is checked, not a variable,
    /// which may still be awaited as itself.
    /// </summary>
    static void AnalyzeTaskUpcast(OperationAnalysisContext context, Symbols symbols)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (!conversion.IsImplicit || conversion.Operand is not (IInvocationOperation or IObjectCreationOperation)) return;
        if (conversion.Type is not INamedTypeSymbol { IsGenericType: false } target
            || target.ToDisplayString() is not ("System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")) return;
        var configured = symbols.Configured(conversion.Syntax.SyntaxTree);
        var awaited = Symbols.AwaitedType(conversion.Operand.Type);
        if (!IsMustObserveValue(awaited, symbols, configured)) return;
        var source = conversion.Operand is IInvocationOperation invocation ? $"returned by {Name(invocation)}" : "created here";
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.MustObserveUpcast, conversion.Operand.Syntax.GetLocation(),
            $"The {Display(conversion.Operand.Type)} {source}", target.Name, Display(awaited)));
    }

    // `CancelAsync(1).ConfigureAwait(false)` is named after CancelAsync.
    static IInvocationOperation Unwrap(IInvocationOperation invocation) =>
        invocation.TargetMethod.Name == "ConfigureAwait" && invocation.Instance is IInvocationOperation inner ? Unwrap(inner) : invocation;

    static string Name(IInvocationOperation invocation) => Unwrap(invocation).TargetMethod.Name;

    static string Display(ITypeSymbol? type) => type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "value";
}
