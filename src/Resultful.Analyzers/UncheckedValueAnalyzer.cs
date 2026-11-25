using System.Collections.Concurrent;
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
/// RF0003: <c>r.Value</c> on a [MustObserve] type with IsSuccess, where no check of the same local, parameter or field
/// guards the read. The guards are read from the syntax around the read: an <c>if</c>, a conditional, <c>&amp;&amp;</c>
/// or <c>||</c>, a <c>while</c>, an <c>is</c> pattern, or an earlier <c>if (…) return;</c> in an enclosing block, switch
/// section or the top-level statements. The earliest such exit is found once per statement list and variable.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UncheckedValueAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.UncheckedValue);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = new Symbols(start.Options);
            var exits = new Exits();
            start.RegisterOperationAction(c => Analyze(c, symbols, exits), OperationKind.PropertyReference);
        });
    }

    static void Analyze(OperationAnalysisContext context, Symbols symbols, Exits exits)
    {
        var operation = (IPropertyReferenceOperation)context.Operation;
        if (operation.Property.Name != "Value" || operation.Instance is not { } instance) return;
        var type = operation.Property.ContainingType;
        if (!symbols.IsMustObserveType(type, symbols.Configured(operation.Syntax.SyntaxTree)) || type.GetMembers("IsSuccess").IsEmpty) return;
        // `r.Value = …` can't happen on a get-only Value; a read inside the type itself is its own business.
        if (SymbolEqualityComparer.Default.Equals(context.ContainingSymbol.ContainingType?.OriginalDefinition, type.OriginalDefinition)) return;

        var variable = Variable(instance);
        if (variable is not null && IsGuarded(operation.Syntax, variable, operation.SemanticModel!, exits)) return;
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.UncheckedValue, operation.Syntax.GetLocation(), instance.Syntax.ToString()));
    }

    /// <summary>The local, parameter or field (of this, or static) the value is read from; null for anything else.</summary>
    static ISymbol? Variable(IOperation instance) => instance switch
    {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        IFieldReferenceOperation { Instance: null or IInstanceReferenceOperation } field => field.Field,
        _ => null,
    };

    static bool IsGuarded(SyntaxNode read, ISymbol variable, SemanticModel model, Exits exits)
    {
        for (SyntaxNode node = read, parent = read.Parent!; parent is not null; node = parent, parent = parent.Parent!)
        {
            switch (parent)
            {
                case IfStatementSyntax @if when node == @if.Statement && Implies(@if.Condition, true, variable, model):
                case IfStatementSyntax { Else: { } @else } elseIf when node == @else && Implies(elseIf.Condition, false, variable, model):
                case WhileStatementSyntax @while when node == @while.Statement && Implies(@while.Condition, true, variable, model):
                case ConditionalExpressionSyntax c when node == c.WhenTrue && Implies(c.Condition, true, variable, model):
                case ConditionalExpressionSyntax c2 when node == c2.WhenFalse && Implies(c2.Condition, false, variable, model):
                case BinaryExpressionSyntax both when both.IsKind(SyntaxKind.LogicalAndExpression) && node == both.Right && Implies(both.Left, true, variable, model):
                case BinaryExpressionSyntax either when either.IsKind(SyntaxKind.LogicalOrExpression) && node == either.Right && Implies(either.Left, false, variable, model):
                    return true;
                case BlockSyntax block when node is StatementSyntax && exits.Before(block, block.Statements, node, variable, model):
                case SwitchSectionSyntax section when node is StatementSyntax && exits.Before(section, section.Statements, node, variable, model):
                    return true;
                case GlobalStatementSyntax { Parent: CompilationUnitSyntax unit } global:
                    return exits.Before(unit, unit.Members.OfType<GlobalStatementSyntax>().Select(g => g.Statement), global.Statement, variable, model);
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// For each statement list (a block, a switch section, the top-level statements) and variable, where the first
    /// `if (failure) exit;` ends: a statement after it only runs on a success. Computed once, so many reads after many
    /// checks stay linear.
    /// </summary>
    sealed class Exits
    {
        readonly ConcurrentDictionary<(SyntaxNode List, ISymbol Variable), int> _firstExitEnd = new(new KeyComparer());

        public bool Before(SyntaxNode list, IEnumerable<StatementSyntax> statements, SyntaxNode statement, ISymbol variable, SemanticModel model) =>
            _firstExitEnd.GetOrAdd((list, variable), _ => FirstExitEnd(statements, variable, model)) <= statement.SpanStart;

        static int FirstExitEnd(IEnumerable<StatementSyntax> statements, ISymbol variable, SemanticModel model)
        {
            foreach (var statement in statements)
            {
                if (statement is IfStatementSyntax { Else: null } @if && Mentions(@if.Condition, variable.Name) && Implies(@if.Condition, false, variable, model)
                    && model.AnalyzeControlFlow(@if.Statement) is { Succeeded: true, EndPointIsReachable: false })
                    return @if.Span.End;
            }

            return int.MaxValue;
        }

        static bool Mentions(SyntaxNode node, string name) =>
            node.DescendantTokens().Any(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == name);

        sealed class KeyComparer : IEqualityComparer<(SyntaxNode List, ISymbol Variable)>
        {
            public bool Equals((SyntaxNode List, ISymbol Variable) x, (SyntaxNode List, ISymbol Variable) y) =>
                x.List == y.List && SymbolEqualityComparer.Default.Equals(x.Variable, y.Variable);

            public int GetHashCode((SyntaxNode List, ISymbol Variable) key) =>
                key.List.GetHashCode() ^ SymbolEqualityComparer.Default.GetHashCode(key.Variable);
        }
    }

    /// <summary>The condition having the value <paramref name="whenTrue"/> means the variable is a success.</summary>
    static bool Implies(ExpressionSyntax condition, bool whenTrue, ISymbol variable, SemanticModel model)
    {
        switch (condition)
        {
            case ParenthesizedExpressionSyntax p:
                return Implies(p.Expression, whenTrue, variable, model);
            case PrefixUnaryExpressionSyntax n when n.IsKind(SyntaxKind.LogicalNotExpression):
                return Implies(n.Operand, !whenTrue, variable, model);
            case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression):
                return whenTrue
                    ? Implies(b.Left, true, variable, model) || Implies(b.Right, true, variable, model)
                    : Implies(b.Left, false, variable, model) && Implies(b.Right, false, variable, model);
            case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalOrExpression):
                return whenTrue
                    ? Implies(b.Left, true, variable, model) && Implies(b.Right, true, variable, model)
                    : Implies(b.Left, false, variable, model) || Implies(b.Right, false, variable, model);
            case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.EqualsExpression) || b.IsKind(SyntaxKind.NotEqualsExpression):
                var literal = b.Right as LiteralExpressionSyntax ?? b.Left as LiteralExpressionSyntax;
                var other = literal == b.Right ? b.Left : b.Right;
                if (literal is null || !(literal.IsKind(SyntaxKind.TrueLiteralExpression) || literal.IsKind(SyntaxKind.FalseLiteralExpression))) return false;
                // `x == true` is x; `x == false` and `x != true` are !x.
                var same = literal.IsKind(SyntaxKind.TrueLiteralExpression) == b.IsKind(SyntaxKind.EqualsExpression);
                return Implies(other, same ? whenTrue : !whenTrue, variable, model);
            case IsPatternExpressionSyntax pattern:
                return Matches(pattern.Expression, pattern.Pattern, whenTrue, variable, model);
            case MemberAccessExpressionSyntax { Name.Identifier.Text: "IsSuccess" } success:
                return whenTrue && IsVariable(success.Expression, variable, model);
            case MemberAccessExpressionSyntax { Name.Identifier.Text: "IsFailure" } failure:
                return !whenTrue && IsVariable(failure.Expression, variable, model);
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }:
                return access.Name.Identifier.Text switch
                {
                    "TryGetValue" => whenTrue && IsVariable(access.Expression, variable, model),
                    "TryGetError" or "TryGetErrors" or "Failed" => !whenTrue && IsVariable(access.Expression, variable, model),
                    _ => false,
                };
            default:
                return false;
        }
    }

    /// <summary>
    /// `x is true`/`is false`/`is not …` on a check, and `r is { IsSuccess: true }` or `{ IsFailure: false }` (which only says
    /// something when the pattern matches).
    /// </summary>
    static bool Matches(ExpressionSyntax subject, PatternSyntax pattern, bool whenTrue, ISymbol variable, SemanticModel model)
    {
        switch (pattern)
        {
            case ParenthesizedPatternSyntax p:
                return Matches(subject, p.Pattern, whenTrue, variable, model);
            case UnaryPatternSyntax { Pattern: var inner } negated when negated.OperatorToken.IsKind(SyntaxKind.NotKeyword):
                return Matches(subject, inner, !whenTrue, variable, model);
            case ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal } when literal.IsKind(SyntaxKind.TrueLiteralExpression):
                return Implies(subject, whenTrue, variable, model);
            case ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal } when literal.IsKind(SyntaxKind.FalseLiteralExpression):
                return Implies(subject, !whenTrue, variable, model);
            case RecursivePatternSyntax { PropertyPatternClause: { } clause } when whenTrue && IsVariable(subject, variable, model):
                return clause.Subpatterns.Any(sub => sub.NameColon?.Name.Identifier.Text switch
                {
                    "IsSuccess" => IsBool(sub.Pattern, true),
                    "IsFailure" => IsBool(sub.Pattern, false),
                    _ => false,
                });
            default:
                return false;
        }
    }

    static bool IsBool(PatternSyntax pattern, bool value) =>
        pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal }
        && literal.IsKind(value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);

    static bool IsVariable(ExpressionSyntax expression, ISymbol variable, SemanticModel model) =>
        SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(expression).Symbol, variable);
}
