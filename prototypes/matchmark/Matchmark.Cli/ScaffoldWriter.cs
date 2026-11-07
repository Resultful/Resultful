namespace Matchmark.Cli;

/// <summary>Writes scaffolded files without overwriting the team's own work.</summary>
public static class ScaffoldWriter
{
    /// <summary>
    /// Writes <paramref name="files"/> under <paramref name="directory"/>. A file that exists is kept unless
    /// <paramref name="force"/>, so re-running after a spec change only adds what is new. Returns what happened to each.
    /// </summary>
    public static IReadOnlyList<(string Path, bool Written)> WriteTo(IReadOnlyDictionary<string, string> files, string directory, bool force)
    {
        var results = new List<(string, bool)>();
        foreach (var (path, content) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var target = Path.Combine(directory, path);
            if (File.Exists(target) && !force)
            {
                results.Add((path, false));
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
            results.Add((path, true));
        }

        return results;
    }
}
