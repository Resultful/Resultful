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
using Microsoft.CodeAnalysis.Formatting;

namespace Resultful.Analyzers;

/// <summary>
/// RF0007's fix, "add a private protected constructor", with fix-all: the base's declared constructors (in this file)
/// become <c>private protected</c>, or <c>private protected Name() { }</c> replaces the implicit one.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PrivateProtectedCodeFixProvider)), Shared]
public sealed class PrivateProtectedCodeFixProvider : CodeFixProvider
{
    /// <summary>The code action's title.</summary>
    public const string Title = "Make the constructor private protected";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Descriptors.PrivateProtected.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(ClosedHierarchyAnalyzer.FixProperty, out var fix) || fix is null) continue;
            if (root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } type) continue;
            context.RegisterCodeFix(CodeAction.Create(Title, ct => Close(context.Document, type, fix, ct), nameof(PrivateProtectedCodeFixProvider)), diagnostic);
        }
    }

    static async Task<Document> Close(Document document, TypeDeclarationSyntax type, string fix, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;
        TypeDeclarationSyntax closed;
        if (fix == ClosedHierarchyAnalyzer.AddConstructor)
        {
            var constructor = SyntaxFactory.ConstructorDeclaration(type.Identifier.WithoutTrivia())
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ProtectedKeyword)))
                .WithBody(SyntaxFactory.Block())
                .WithAdditionalAnnotations(Formatter.Annotation);
            closed = type.WithMembers(type.Members.Insert(0, constructor));
            if (closed.OpenBraceToken.IsMissing)
                // `abstract record OrderError : Error;` has no body to put the constructor in.
                closed = closed.WithSemicolonToken(default).WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                    .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)).WithAdditionalAnnotations(Formatter.Annotation);
        }
        else
        {
            closed = type.ReplaceNodes(
                type.Members.OfType<ConstructorDeclarationSyntax>().Where(c => !c.Modifiers.Any(SyntaxKind.StaticKeyword) && IsOpen(c.Modifiers)),
                (c, _) => c.WithModifiers(PrivateProtected(c.Modifiers)));
        }

        return document.WithSyntaxRoot(root.ReplaceNode(type, closed));
    }

    static bool IsOpen(SyntaxTokenList modifiers) =>
        modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword) && !modifiers.Any(SyntaxKind.PrivateKeyword);

    /// <summary>The accessibility keywords become `private protected`, in their place and with their trivia.</summary>
    static SyntaxTokenList PrivateProtected(SyntaxTokenList modifiers)
    {
        var access = modifiers.Where(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword) || m.IsKind(SyntaxKind.InternalKeyword)).ToList();
        var first = access[0];
        var replacement = new[]
        {
            SyntaxFactory.Token(first.LeadingTrivia, SyntaxKind.PrivateKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space)),
            SyntaxFactory.Token(SyntaxTriviaList.Empty, SyntaxKind.ProtectedKeyword, access[access.Count - 1].TrailingTrivia),
        };
        var index = modifiers.IndexOf(first);
        var rest = modifiers.Where(m => !access.Contains(m)).ToList();
        return SyntaxFactory.TokenList(rest.Take(index).Concat(replacement).Concat(rest.Skip(index)));
    }
}
