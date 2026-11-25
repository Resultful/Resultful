using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Resultful.Analyzers;

/// <summary>
/// RF0005's fix, "rewrite as a switch expression", with fix-all: one arm per handler, in the handlers' order. A lambda
/// keeps its parameter name and body (<c>NotFound n =&gt; 404</c>); a method group <c>M</c> becomes <c>NotFound notFound =&gt; M(notFound)</c>.
/// A name already in scope gets a number. Not offered until RFSP0001 ships (stack 17), since the switch it writes gets CS8509
/// without the suppressor; the tests construct it with <c>offered: true</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MatchToSwitchCodeFixProvider)), Shared]
public sealed class MatchToSwitchCodeFixProvider : CodeFixProvider
{
    /// <summary>The code action's title.</summary>
    public const string Title = "Rewrite as a switch expression";

    readonly bool _offered;

    /// <summary>The fix as shipped: not offered yet (see the remarks on RF0005).</summary>
    public MatchToSwitchCodeFixProvider() : this(offered: false) { }

    internal MatchToSwitchCodeFixProvider(bool offered) => _offered = offered;

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Descriptors.MatchToSwitch.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || !_offered) return;
        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } invocation) continue;
            context.RegisterCodeFix(CodeAction.Create(Title, ct => Rewrite(context.Document, invocation, ct), nameof(MatchToSwitchCodeFixProvider)), diagnostic);
        }
    }

    static async Task<Document> Rewrite(Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model?.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation
            || invocation.Expression is not MemberAccessExpressionSyntax access) return document;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var line = text.Lines.GetLineFromPosition(invocation.SpanStart).ToString();
        var indent = line.Substring(0, line.Length - line.TrimStart().Length);
        var newline = text.Lines.Count > 1 && text.ToString(text.Lines[0].SpanIncludingLineBreak).EndsWith("\r\n", System.StringComparison.Ordinal) ? "\r\n" : "\n";
        var receiver = access.Expression.WithoutTrivia();
        var source = new StringBuilder()
            .Append(receiver is IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax
                or ThisExpressionSyntax or ParenthesizedExpressionSyntax ? receiver.ToString() : $"({receiver})")
            .Append(" switch").Append(newline).Append(indent).Append('{').Append(newline);
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        // A pattern variable lives in the enclosing statement's scope, so it may not reuse a local or parameter there (CS0136).
        string Unique(string name)
        {
            var candidate = name;
            for (var i = 2; !chosen.Add(candidate) || model!.LookupSymbols(invocation.SpanStart, name: candidate.TrimStart('@'))
                     .Any(x => x is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol); i++)
                candidate = name.TrimStart('@') + i;
            return candidate;
        }

        foreach (var argument in operation.Arguments)
        {
            var leaf = ((INamedTypeSymbol)argument.Parameter!.Type).DelegateInvokeMethod!.Parameters[0].Type;
            var type = leaf.ToMinimalDisplayString(model, invocation.SpanStart);
            var value = argument.Value is IConversionOperation conversion ? conversion.Operand : argument.Value;
            string arm;
            if (value is IDelegateCreationOperation { Target: IAnonymousFunctionOperation { Syntax: LambdaExpressionSyntax lambda } function })
            {
                var parameter = function.Symbol.Parameters[0];
                var body = lambda.ExpressionBody!;
                var name = parameter.Name;
                if (name != "_" && Unique(name) is var unique && unique != name)
                {
                    body = body.ReplaceNodes(
                        body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
                            .Where(id => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(id, cancellationToken).Symbol, parameter)),
                        (old, _) => SyntaxFactory.IdentifierName(unique).WithTriviaFrom(old));
                    name = unique;
                }

                arm = (name == "_" ? type : $"{type} {name}") + " => " + body.WithoutLeadingTrivia().ToFullString().TrimEnd();
            }
            else
            {
                var name = char.ToLowerInvariant(leaf.Name[0]) + leaf.Name.Substring(1);
                if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None) name = "@" + name;
                name = Unique(name);
                arm = $"{type} {name} => {argument.Value.Syntax.WithoutTrivia()}({name})";
            }

            source.Append(indent).Append("    ").Append(arm).Append(',').Append(newline);
        }

        source.Append(indent).Append('}');
        var rewritten = SyntaxFactory.ParseExpression(source.ToString()).WithTriviaFrom(invocation);
        return document.WithSyntaxRoot(root.ReplaceNode(invocation, rewritten));
    }
}
