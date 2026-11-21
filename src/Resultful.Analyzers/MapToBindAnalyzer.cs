using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Resultful.Analyzers;

/// <summary>
/// RF0009: <c>r.Map(x =&gt; Parse(x))</c> where Parse returns a result, giving <c>Result&lt;Result&lt;T&gt;&gt;</c>. The fix
/// renames Map to Bind (MapAsync to BindAsync) when the renamed call binds to a Bind.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MapToBindAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Set to the method's new name when the renamed call binds.</summary>
    public const string FixProperty = "Bind";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.MapToBind);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = new Symbols(start.Options);
            start.RegisterOperationAction(c => Analyze(c, symbols), OperationKind.Invocation);
        });
    }

    static void Analyze(OperationAnalysisContext context, Symbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name is not ("Map" or "MapAsync")) return;
        // The selector's return type (through MapAsync's Task or ValueTask), and the call's type (through the Task or
        // ValueTask an async receiver gives: `ValueTask<Result<T>>.Map`).
        var selector = method.Parameters.Select(p => p.Type).OfType<INamedTypeSymbol>().FirstOrDefault(t => t.TypeKind == TypeKind.Delegate);
        var returned = selector?.DelegateInvokeMethod?.ReturnType;
        var inner = method.Name == "Map" ? (symbols.IsResultType(returned) ? returned : null) : Result(returned, symbols);
        var outer = Result(invocation.Type, symbols);
        if (inner is null || outer is null) return;

        var bind = method.Name == "Map" ? "Bind" : "BindAsync";
        var properties = Binds(invocation, bind) ? ImmutableDictionary<string, string?>.Empty.Add(FixProperty, bind) : null;
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.MapToBind, Name(invocation.Syntax)?.GetLocation() ?? invocation.Syntax.GetLocation(), properties,
            Display(inner), Display(outer), bind));
    }

    /// <summary>The call with Map renamed binds to a method of that name (so the error types agree).</summary>
    static bool Binds(IInvocationOperation invocation, string bind)
    {
        if (invocation.Syntax is not InvocationExpressionSyntax syntax || Name(syntax) is not { } name) return false;
        var renamed = syntax.ReplaceToken(name.Identifier, SyntaxFactory.Identifier(bind).WithTriviaFrom(name.Identifier));
        var info = invocation.SemanticModel!.GetSpeculativeSymbolInfo(syntax.SpanStart, renamed, SpeculativeBindingOption.BindAsExpression);
        return info.Symbol is IMethodSymbol { Name: var bound } && bound == bind;
    }

    /// <summary>The `Map` in `r.Map(…)`, `r?.Map(…)` or `Map(…)`.</summary>
    public static SimpleNameSyntax? Name(SyntaxNode invocation) => (invocation as InvocationExpressionSyntax)?.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Name,
        MemberBindingExpressionSyntax binding => binding.Name,
        SimpleNameSyntax simple => simple,
        _ => null,
    };

    static ITypeSymbol? Result(ITypeSymbol? type, Symbols symbols) =>
        symbols.IsResultType(type) ? type : symbols.IsResultType(Symbols.AwaitedType(type)) ? Symbols.AwaitedType(type) : null;

    static string Display(ITypeSymbol? type) => type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "result";
}
