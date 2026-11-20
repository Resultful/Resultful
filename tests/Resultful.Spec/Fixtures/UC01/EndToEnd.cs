// UC01: use case 1, an endpoint that returns the right status. The evidence is end to end, not compiled here:
// samples/Resultful.Shop.Domain/UC01 (OrderError, the GetOrderResult shape), samples/Resultful.Shop.Api/UC01 and
// samples/Resultful.Shop.Net11/UC01 (union errors) build from the packed packages. tests/Resultful.EndToEnd checks
// that from the packed packages and writes artifacts/e2e/<run>/e2e-report.json. This case passes when that report
// says the line passed: $E2E_REPORT names it, and CI must set it (the end-to-end job runs before the spec);
// locally the newest report is used. The pins below hold the oracle files this line is judged by: changing one
// changes this fixture, so it is a `spec:` commit (tests/Resultful.EndToEnd checks the pins on every run).
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Resultful.Spec.Support;

public static class Cases
{
    static readonly (string Path, string Sha256)[] Pins =
    [
    ];

    [SpecCase]
    public static void Passes_end_to_end()
    {
        var root = Root();
        foreach (var (path, sha256) in Pins)
        {
            var text = File.ReadAllText(Path.Combine(root, path)).Replace("\r\n", "\n", StringComparison.Ordinal);
            Check.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), path);
        }

        var report = Environment.GetEnvironmentVariable("E2E_REPORT");
        Check.True(report is not null || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true", "E2E_REPORT is set in CI");
        report ??= Newest(root);
        Check.True(report is not null && File.Exists(report), "an end-to-end report: run tests/Resultful.EndToEnd first");
        using var doc = JsonDocument.Parse(File.ReadAllText(report!));
        var line = doc.RootElement.EnumerateArray().FirstOrDefault(l => l.GetProperty("id").GetString() == "UC01");
        Check.True(line.ValueKind == JsonValueKind.Object, "UC01 in " + report);
        Check.Equal("pass", line.GetProperty("outcome").GetString(), "UC01's end-to-end outcome");
    }

    static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Resultful.slnx"))) return dir.FullName;
        throw new SpecFailure("no repository root above " + AppContext.BaseDirectory);
    }

    static string? Newest(string root)
    {
        var dir = Path.Combine(root, "artifacts", "e2e");
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "e2e-report.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
    }
}
