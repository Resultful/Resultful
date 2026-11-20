using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Resultful.EndToEnd;

internal static class Repo
{
    public static string Root { get; } = typeof(Repo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "RepoRoot").Value!;

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    // Unique per run and per attempt, so a version is never restored from an earlier run.
    public static string RunId { get; } = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") is { } id
        ? $"{id}.{Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "1"}"
        : DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);

    // One folder per run under artifacts/ (gitignored): the feed, the isolated package cache, every build's output.
    public static string RunDir { get; } = CreateRunDir();

    private static string CreateRunDir()
    {
        var dir = Path("artifacts", "e2e", RunId);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

internal sealed record ProcessResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;

    // The distinct error lines, paths made relative; else the last lines, where the test runners say what went wrong.
    public string Tail(int lines = 40)
    {
        var errors = Output.Split('\n')
            .Where(l => l.Contains(": error ", StringComparison.Ordinal))
            .Select(l => l.Replace(Repo.Root + "/", "", StringComparison.Ordinal).Split(" [", 2)[0].Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return errors.Count > 0
            ? string.Join('\n', errors.Take(lines / 4)) + (errors.Count > lines / 4 ? $"\n… {errors.Count - (lines / 4)} more" : "")
            : string.Join('\n', Output.Split('\n').TakeLast(lines));
    }
}

internal static class Run
{
    public static async Task<ProcessResult> Process(string file, IEnumerable<string> args, IDictionary<string, string>? env = null, TimeSpan? timeout = null, CancellationToken cancel = default)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Repo.Root };
        foreach (var a in args) info.ArgumentList.Add(a);
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var (k, v) in env ?? new Dictionary<string, string>()) info.Environment[k] = v;

        using var process = System.Diagnostics.Process.Start(info)!;
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(timeout ?? TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            lock (output) output.AppendLine("e2e: timed out or cancelled");
            return new ProcessResult(-1, output.ToString());
        }

        lock (output) return new ProcessResult(process.ExitCode, output.ToString());
    }

    public static Task<ProcessResult> Dotnet(IEnumerable<string> args, IDictionary<string, string>? env = null, TimeSpan? timeout = null) =>
        Process("dotnet", args, env, timeout);

    // A long-running child (the Shop's API); the caller kills it.
    public static System.Diagnostics.Process Start(string file, IEnumerable<string> args, IDictionary<string, string> env, StringBuilder output)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Repo.Root };
        foreach (var a in args) info.ArgumentList.Add(a);
        foreach (var (k, v) in env) info.Environment[k] = v;
        var process = System.Diagnostics.Process.Start(info)!;
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }
}
