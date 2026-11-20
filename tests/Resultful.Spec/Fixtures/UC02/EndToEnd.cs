// UC02: use case 2, a sign-up form that reports every mistake at once. The evidence is end to end, not compiled
// here: samples/Resultful.Shop.Domain/UC02 (InputError) and samples/Resultful.Shop.Api/UC02 build from the packed
// packages. tests/Resultful.EndToEnd checks that from the packed packages and writes
// artifacts/e2e/<run>/e2e-report.json. This case passes when that report (the newest, or $E2E_REPORT) says the
// line passed, so CI runs the end-to-end job before the spec. The pins below hold the oracle files this line is
// judged by: changing one changes this fixture, so it is a `spec:` commit (tests/Resultful.EndToEnd checks the
// pins on every run).
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

        var report = Environment.GetEnvironmentVariable("E2E_REPORT") ?? Newest(root);
        Check.True(report is not null && File.Exists(report), "an end-to-end report: run tests/Resultful.EndToEnd first");
        using var doc = JsonDocument.Parse(File.ReadAllText(report!));
        var line = doc.RootElement.EnumerateArray().FirstOrDefault(l => l.GetProperty("id").GetString() == "UC02");
        Check.True(line.ValueKind == JsonValueKind.Object, "UC02 in " + report);
        Check.Equal("pass", line.GetProperty("outcome").GetString(), "UC02's end-to-end outcome");
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
