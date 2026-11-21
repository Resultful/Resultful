using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Resultful.Analyzers;

/// <summary>
/// RF0011: <c>if (r.Failed(out var value, out var error)) { … value … }</c> for a value-type <c>value</c>, which is
/// <c>default</c> there. A reference type is left to the compiler's nullable analysis (<c>[MaybeNullWhen(true)]</c>).
/// The code that runs only when the call returned true is read from the syntax around the call, as RF0003 reads its
/// guards: an <c>if</c>, <c>?:</c>, <c>while</c>, <c>&amp;&amp;</c> or <c>||</c>, through <c>!</c> and parentheses, and the
/// statements after <c>if (!r.Failed(…)) return …;</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FailedValueAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.FailedValue);

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
        if (method.Name != "Failed" || method.IsStatic || method.ReturnType.SpecialType != SpecialType.System_Boolean
            || method.Parameters.Length != 2 || method.Parameters.Any(p => p.RefKind != RefKind.Out)) return;
        var type = method.ContainingType;
        if (!symbols.IsMustObserveType(type, symbols.Configured(invocation.Syntax.SyntaxTree)) && !Symbols.IsResultShape(type)) return;

        var valueType = method.Parameters[0].Type;
        if (!valueType.IsValueType || valueType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) return;
        var argument = invocation.Arguments.FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.Parameter, method.Parameters[0]));
        var local = argument?.Value switch
        {
            IDeclarationExpressionOperation { Expression: ILocalReferenceOperation declared } => declared.Local,
            ILocalReferenceOperation reference => reference.Local,
            _ => null,
        };
        if (local is null) return;

        var model = invocation.SemanticModel!;
        var receiver = invocation.Instance?.Syntax.ToString() ?? "";
        var typeName = valueType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var regions = WhereFailed(invocation.Syntax, model).ToList();
        if (regions.Count == 0) return;
        foreach (var read in ReadsBeforeAssignment(invocation.Syntax, local, model))
        {
            // A local function runs when it is called, not where it is declared: its reads count only when the
            // declaration itself is in the failed code.
            var at = read.Ancestors().OfType<LocalFunctionStatementSyntax>().LastOrDefault(f => f.SpanStart > invocation.Syntax.Span.End) ?? (SyntaxNode)read;
            if (regions.Any(r => r.Span.Contains(at.Span)))
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.FailedValue, read.GetLocation(), local.Name, receiver, typeName));
        }
    }

    /// <summary>
    /// The syntax that runs only when the call returned true. <c>whenTrue</c>/<c>whenFalse</c> say which values of the
    /// current expression imply that, walking out from the call through <c>!</c>, parentheses, <c>&amp;&amp;</c> and <c>||</c>.
    /// </summary>
    static IEnumerable<SyntaxNode> WhereFailed(SyntaxNode call, SemanticModel model)
    {
        bool whenTrue = true, whenFalse = false;
        for (SyntaxNode node = call, parent = call.Parent!; parent is not null; node = parent, parent = parent.Parent!)
        {
            switch (parent)
            {
                case ParenthesizedExpressionSyntax:
                    continue;
                case PrefixUnaryExpressionSyntax negation when negation.IsKind(SyntaxKind.LogicalNotExpression):
                    (whenTrue, whenFalse) = (whenFalse, whenTrue);
                    continue;
                case BinaryExpressionSyntax both when both.IsKind(SyntaxKind.LogicalAndExpression):
                    // The right runs only when the left is true; the whole is true only when both are.
                    if (node == both.Left && whenTrue) yield return both.Right;
                    whenFalse = false;
                    continue;
                case BinaryExpressionSyntax either when either.IsKind(SyntaxKind.LogicalOrExpression):
                    // The right runs only when the left is false; the whole is false only when both are.
                    if (node == either.Left && whenFalse) yield return either.Right;
                    whenTrue = false;
                    continue;
                case ConditionalExpressionSyntax conditional when node == conditional.Condition:
                    if (whenTrue) yield return conditional.WhenTrue;
                    if (whenFalse) yield return conditional.WhenFalse;
                    yield break;
                case WhileStatementSyntax loop when node == loop.Condition:
                    if (whenTrue) yield return loop.Statement;
                    yield break;
                case IfStatementSyntax @if when node == @if.Condition:
                    if (whenTrue) yield return @if.Statement;
                    if (whenFalse && @if.Else is { } @else) yield return @else.Statement;
                    // `if (!r.Failed(…)) return …;`: what follows in the block runs only when the call returned true.
                    if (whenFalse && @if.Else is null && @if.Parent is BlockSyntax block
                        && model.AnalyzeControlFlow(@if.Statement) is { Succeeded: true, EndPointIsReachable: false })
                    {
                        // A label can be reached by `goto` from a success, so the region ends at the first one.
                        foreach (var after in block.Statements.Where(s => s.SpanStart > @if.Span.End).TakeWhile(s => s is not LabeledStatementSyntax))
                        {
                            if (after is not LocalFunctionStatementSyntax) yield return after;
                        }
                    }

                    yield break;
                default:
                    yield break;
            }
        }
    }

    /// <summary>
    /// The references to the local after the call in its member, in order, up to the first assignment to it: after that
    /// it may hold a real value, wherever the assignment is.
    /// </summary>
    static IEnumerable<IdentifierNameSyntax> ReadsBeforeAssignment(SyntaxNode call, ILocalSymbol local, SemanticModel model)
    {
        var scope = call.Ancestors().FirstOrDefault(a => a is MemberDeclarationSyntax and not GlobalStatementSyntax) ?? call.SyntaxTree.GetRoot();
        var references = scope.DescendantNodes(n => n.Span.End > call.Span.End)
            .OfType<IdentifierNameSyntax>()
            .Where(id => id.SpanStart > call.Span.End && id.Identifier.ValueText == local.Name
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(id).Symbol, local));
        foreach (var reference in references)
        {
            if (IsWritten(model.GetOperation(reference))) yield break;
            yield return reference;
        }
    }

    /// <summary>
    /// The reference writes the local, or can: an assignment (simple, compound, <c>??=</c>, a deconstruction), <c>++</c>
    /// or <c>--</c>, an <c>out</c> or <c>ref</c> argument, a <c>ref</c> alias (<c>ref var r = ref value;</c>,
    /// <c>r = ref value</c>) or its address. After it the local may hold a real value, so the scan ends.
    /// </summary>
    static bool IsWritten(IOperation? reference)
    {
        if (reference is not ILocalReferenceOperation) return false;
        IOperation child = reference;
        var parent = reference.Parent;
        // `(value, _) = …`: the reference is inside the target tuple, at any depth.
        while (parent is ITupleOperation)
        {
            child = parent;
            parent = parent.Parent;
        }

        return parent switch
        {
            IDeconstructionAssignmentOperation deconstruction => deconstruction.Target == child,
            ISimpleAssignmentOperation { IsRef: true } refAssignment => refAssignment.Value == child || refAssignment.Target == child,
            IAssignmentOperation assignment => assignment.Target == child,
            IIncrementOrDecrementOperation => true,
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Out or RefKind.Ref,
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: RefKind.Ref } } => true,
            IAddressOfOperation => true,
            _ => false,
        };
    }
}
