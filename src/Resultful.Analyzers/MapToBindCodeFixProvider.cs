using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Resultful.Analyzers;

/// <summary>RF0009's fix, "replace Map with Bind", with fix-all: only the method name changes.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MapToBindCodeFixProvider)), Shared]
public sealed class MapToBindCodeFixProvider : CodeFixProvider
{
    /// <summary>The code action's title.</summary>
    public const string Title = "Replace Map with Bind";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Descriptors.MapToBind.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(MapToBindAnalyzer.FixProperty, out var bind) || bind is null) continue;
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<SimpleNameSyntax>() is not { } name) continue;
            var token = name.Identifier;
            context.RegisterCodeFix(CodeAction.Create(Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceToken(token, SyntaxFactory.Identifier(bind).WithTriviaFrom(token)))),
                    nameof(MapToBindCodeFixProvider)),
                diagnostic);
        }
    }
}
