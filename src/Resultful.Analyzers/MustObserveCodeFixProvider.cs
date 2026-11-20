using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Resultful.Analyzers;

/// <summary>
/// RF0001's fix (code-fixes.tsv: "discard with `_ =`", fix-all): <c>Cancel(1);</c> becomes <c>_ = Cancel(1);</c>, and
/// <c>x =&gt; Cancel(x)</c> as a void lambda becomes <c>x =&gt; _ = Cancel(x)</c>. Not offered for an async void handler,
/// which a discard doesn't fix.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MustObserveCodeFixProvider)), Shared]
public sealed class MustObserveCodeFixProvider : CodeFixProvider
{
    public const string Title = "Discard with `_ =`";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Descriptors.MustObserve.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(MustObserveAnalyzer.FixProperty, out var fix) || fix != MustObserveAnalyzer.DiscardFix) continue;
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not ExpressionSyntax expression) continue;
            expression = Outermost(expression, diagnostic.Location.SourceSpan);
            context.RegisterCodeFix(
                CodeAction.Create(Title, ct => Discard(context.Document, expression, ct), equivalenceKey: nameof(MustObserveCodeFixProvider)),
                diagnostic);
        }
    }

    // FindNode returns the innermost node for the span; the diagnostic covers the whole expression.
    static ExpressionSyntax Outermost(ExpressionSyntax expression, Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        while (expression.Parent is ExpressionSyntax parent && parent.Span == span) expression = parent;
        return expression;
    }

    static async Task<Document> Discard(Document document, ExpressionSyntax expression, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;
        var discard = SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                // A discard needs the contextual kind the parser gives `_`; IdentifierName("_") binds as a name (CS0103).
                SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(default, SyntaxKind.UnderscoreToken, "_", "_", default)),
                expression.WithoutTrivia())
            .WithTriviaFrom(expression);
        return document.WithSyntaxRoot(root.ReplaceNode(expression, discard));
    }
}
