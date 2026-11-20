using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Resultful.Consumption;

/// <summary>
/// Every public type and member in src/Resultful has a doc comment with a non-empty summary, and names each of its
/// parameters and type parameters. CS1591 (checked in CI with GenerateDocumentationFile) only requires some comment;
/// this also catches an empty summary or an undocumented parameter.
/// </summary>
public sealed class XmlDocTests
{
    [Fact]
    public void Every_public_member_has_a_summary_and_documents_its_parameters()
    {
        var srcDir = Path.Combine(AssemblyPaths.RepoRootDir, "src", "Resultful");
        var files = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);

        var problems = new List<string>();
        var documentedPartials = new HashSet<string>(StringComparer.Ordinal);
        var checkedCount = 0;
        foreach (var file in files)
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);
            foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                if (member is BaseNamespaceDeclarationSyntax || !IsPublic(member))
                {
                    continue;
                }

                checkedCount++;
                if (member is TypeDeclarationSyntax { } partialType && partialType.Modifiers.Any(SyntaxKind.PartialKeyword)
                    && partialType.GetLeadingTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)))
                {
                    documentedPartials.Add(partialType.Identifier.Text);
                }

                var where = $"{Path.GetRelativePath(srcDir, file)}:{member.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";
                problems.AddRange(Check(member).Select(p => $"{where}: {p}"));
            }
        }

        // A partial class is documented on one part only (the parts' comments would be joined), so a part without a
        // comment is fine if another part has one.
        problems.RemoveAll(p => documentedPartials.Any(name => p.EndsWith($"[partial {name}] no doc comment on this part", StringComparison.Ordinal)));

        Assert.True(checkedCount > 400, $"only {checkedCount} public members found; is the source path right?");
        Assert.True(problems.Count == 0, $"{problems.Count} doc problem(s):\n" + string.Join("\n", problems.Take(40)));
    }

    private static IEnumerable<string> Check(MemberDeclarationSyntax member)
    {
        var docText = string.Concat(member.GetLeadingTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
            .Select(t => t.ToFullString()));
        if (docText.Length == 0)
        {
            if (member is TypeDeclarationSyntax type && type.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                yield return $"[partial {type.Identifier.Text}] no doc comment on this part";
                yield break;
            }

            yield return "no doc comment";
            yield break;
        }

        var xml = XElement.Parse("<doc>" + string.Join("\n", docText.Split('\n').Select(l => l.Trim().TrimStart('/'))) + "</doc>");
        if (string.IsNullOrWhiteSpace(xml.Element("summary")?.Value))
        {
            yield return "empty or missing <summary>";
        }

        var parameters = member switch
        {
            BaseMethodDeclarationSyntax m => m.ParameterList.Parameters.Select(p => p.Identifier.Text),
            _ => [],
        };
        var typeParameters = member switch
        {
            MethodDeclarationSyntax m => m.TypeParameterList?.Parameters.Select(p => p.Identifier.Text) ?? [],
            TypeDeclarationSyntax t => t.TypeParameterList?.Parameters.Select(p => p.Identifier.Text) ?? [],
            _ => [],
        };

        foreach (var name in parameters)
        {
            if (string.IsNullOrWhiteSpace(xml.Elements("param").FirstOrDefault(e => (string?)e.Attribute("name") == name)?.Value))
            {
                yield return $"no <param> for '{name}'";
            }
        }

        foreach (var name in typeParameters)
        {
            if (string.IsNullOrWhiteSpace(xml.Elements("typeparam").FirstOrDefault(e => (string?)e.Attribute("name") == name)?.Value))
            {
                yield return $"no <typeparam> for '{name}'";
            }
        }
    }

    private static bool IsPublic(MemberDeclarationSyntax member)
    {
        for (SyntaxNode? node = member; node is MemberDeclarationSyntax m and not BaseNamespaceDeclarationSyntax; node = node.Parent)
        {
            var inInterface = m.Parent is InterfaceDeclarationSyntax;
            if (!m.Modifiers.Any(SyntaxKind.PublicKeyword) && !(inInterface && !m.Modifiers.Any(SyntaxKind.PrivateKeyword)))
            {
                return false;
            }
        }

        return true;
    }
}
