using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace Resultful.Analyzers;

/// <summary>
/// RF0004's fix, "wrap in Result.Ok(…) or Result.Fail&lt;T&gt;(…)": two actions, one per meaning, for a
/// Resultful.Result&lt;string&gt;. No fix-all, since each string is a choice.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BareStringCodeFixProvider)), Shared]
public sealed class BareStringCodeFixProvider : CodeFixProvider
{
    /// <summary>The success action's title.</summary>
    public const string OkTitle = "Wrap in Result.Ok(…)";
    /// <summary>The failure action's title.</summary>
    public const string FailTitle = "Wrap in Result.Fail<string>(…)";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Descriptors.BareString.Id);

    /// <inheritdoc />
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.ContainsKey(BareStringAnalyzer.FixProperty)) continue;
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not ExpressionSyntax expression) continue;
            while (expression.Parent is ExpressionSyntax parent && parent.Span == diagnostic.Location.SourceSpan) expression = parent;
            var target = expression;
            context.RegisterCodeFix(CodeAction.Create(OkTitle, ct => Wrap(context.Document, target, "Ok", ct), "RF0004.Ok"), diagnostic);
            context.RegisterCodeFix(CodeAction.Create(FailTitle, ct => Wrap(context.Document, target, "Fail<string>", ct), "RF0004.Fail"), diagnostic);
        }
    }

    static async Task<Document> Wrap(Document document, ExpressionSyntax expression, string method, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;
        var call = SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression("global::Resultful.Result." + method).WithAdditionalAnnotations(Simplifier.Annotation),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(expression.WithoutTrivia()))))
            .WithTriviaFrom(expression);
        return document.WithSyntaxRoot(root.ReplaceNode(expression, call));
    }
}
