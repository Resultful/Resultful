using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Resultful.Analyzers;

/// <summary>
/// RF0008's fix, "add the parameter types", with fix-all: <c>n =&gt; 404</c> becomes <c>(NotFound n) =&gt; 404</c>, and
/// <c>(a, b) =&gt; …</c> gets a type on each parameter. Modifiers (<c>static</c>, <c>async</c>) and trivia are kept.
/// Not offered for <c>delegate { … }</c>, which has no parameters to type.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UntypedHandlerCodeFixProvider)), Shared]
public sealed class UntypedHandlerCodeFixProvider : CodeFixProvider
{
    /// <summary>The code action's title.</summary>
    public const string Title = "Add the parameter types";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Descriptors.UntypedHandler.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;
        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                    .FirstAncestorOrSelf<LambdaExpressionSyntax>() is not { } lambda || lambda.Span != diagnostic.Location.SourceSpan) continue;
            context.RegisterCodeFix(CodeAction.Create(Title, ct => AddTypes(context.Document, lambda, ct), nameof(UntypedHandlerCodeFixProvider)), diagnostic);
        }
    }

    static async Task<Document> AddTypes(Document document, LambdaExpressionSyntax lambda, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model?.GetSymbolInfo(lambda, cancellationToken).Symbol is not IMethodSymbol symbol) return document;

        TypeSyntax Type(int i) =>
            SyntaxFactory.ParseTypeName(symbol.Parameters[i].Type.ToMinimalDisplayString(model, lambda.SpanStart)).WithTrailingTrivia(SyntaxFactory.Space);

        LambdaExpressionSyntax typed;
        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
                var parameter = simple.Parameter.WithoutTrivia().WithType(Type(0));
                var list = SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(parameter)).WithTriviaFrom(simple.Parameter);
                typed = SyntaxFactory.ParenthesizedLambdaExpression(simple.Modifiers, list, simple.ArrowToken, simple.Block, simple.ExpressionBody)
                    .WithTriviaFrom(simple);
                break;
            case ParenthesizedLambdaExpressionSyntax parenthesized:
                var parameters = parenthesized.ParameterList.Parameters;
                var withTypes = parameters.Select((p, i) => p.Type is null ? p.WithoutLeadingTrivia().WithType(Type(i)).WithLeadingTrivia(p.GetLeadingTrivia()) : p);
                typed = parenthesized.WithParameterList(parenthesized.ParameterList.WithParameters(
                    SyntaxFactory.SeparatedList(withTypes, parameters.GetSeparators())));
                break;
            default:
                return document;
        }

        return document.WithSyntaxRoot(root.ReplaceNode(lambda, typed));
    }
}
