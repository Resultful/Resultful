using Microsoft.CodeAnalysis;
using Xunit;

namespace Resultful.Analyzers.Tests;

/// <summary>
/// Decision 9's one negative-compile check: the operators in Resultful.Operators exist only with
/// `using Resultful.Operators;`. Without it `if (r)` is CS0029 and `a | b` is CS0019; with it both compile.
/// </summary>
public sealed class OperatorOptInTests
{
    const string Body = """
        public static class Cases
        {
            static int Pick(Result<int> a, Result<int> b)
            {
                if (a) { return 1; }
                var either = a | b;
                return either.IsSuccess ? 2 : 3;
            }
        }
        """;

    static List<string> Errors(string source) =>
        [.. AnalyzerHarness.Compile([source]).GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id)];

    [Fact]
    public void Without_the_using_the_operators_are_absent() =>
        Assert.Equal(["CS0029", "CS0019"], Errors("using Resultful;\n\n" + Body));

    [Fact]
    public void With_the_using_they_compile() =>
        Assert.Empty(Errors("using Resultful;\nusing Resultful.Operators;\n\n" + Body));
}
