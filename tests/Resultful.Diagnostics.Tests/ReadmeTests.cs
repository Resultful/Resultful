using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Resultful.Diagnostics.Tests;

// Every line of every C# block in src/Resultful.Diagnostics/README.md appears in this file's compiled snippet, so a
// README sample that stops compiling fails here.
public sealed partial class ReadmeTests
{
    public sealed record Order(int Id);

    sealed class Payments
    {
        public async Task<Result<int>> Charge(Order order)
        {
            await Task.Yield();
            return Result.Fail<int>(new CardDeclined("4111"));
        }
    }

    static async Task<Result<int>> Snippet(ActivitySource source, Payments payments, Order order, ILogger logger)
    {
        using var activity = source.StartActivity("charge");
        var charged = (await payments.Charge(order))
            .LogFailure(logger, LogLevel.Warning) // one entry: "Result failed with CardDeclined: the card was declined"
            .RecordFailure(activity); // status Error, tag resultful.error.type = "CardDeclined"
        return charged;
    }

    [Fact]
    public async Task The_snippet_runs_and_does_what_its_comments_say()
    {
        using var spans = new Spans();
        var logger = new ListLogger();

        await Snippet(spans.Source, new Payments(), new Order(7), logger);

        Assert.Equal("Result failed with CardDeclined: the card was declined", Assert.Single(logger.Entries).Text);
        var span = Assert.Single(spans.Stopped);
        Assert.Equal((ActivityStatusCode.Error, (object?)"CardDeclined"), (span.Status, span.GetTagItem("resultful.error.type")));
    }

    [Fact]
    public void Every_readme_snippet_line_is_in_this_file()
    {
        var root = Root();
        var self = File.ReadAllText(Path.Combine(root, "tests", "Resultful.Diagnostics.Tests", "ReadmeTests.cs"));
        var readme = File.ReadAllText(Path.Combine(root, "src", "Resultful.Diagnostics", "README.md"));
        var lines = CSharpBlock().Matches(readme).SelectMany(m => m.Groups[1].Value.Split('\n')).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Contains(line, self, StringComparison.Ordinal));
    }

    // The repository root, found from the test binary (CallerFilePath is path-mapped in CI builds).
    static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Resultful.slnx"))) directory = directory.Parent ?? throw new InvalidOperationException("Resultful.slnx not found");
        return directory.FullName;
    }

    [GeneratedRegex("```csharp\\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex CSharpBlock();
}
