// Regenerates a target project's PublicAPI.Shipped.txt files with the PublicAPI analyzers' own code fix (RS0016),
// so the target lists are exactly what the analyzer would write.
//
//   dotnet run tests/Resultful.TargetApi/RegenApi.cs -- Resultful [Resultful.OneOf …]
//
// `dotnet format` applies a code fix to one target framework only, so this runs it once per target, narrowing the
// project with TargetApiTfm (Directory.Build.targets).

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

var root = Path.GetDirectoryName(SourceFile.Path())!;
if (args.Length == 0) { Console.Error.WriteLine("usage: RegenApi.cs <Package> [<Package> …]"); return 2; }
foreach (var package in args)
{
    var dir = Path.Combine(root, package);
    var project = Path.Combine(dir, package + ".csproj");
    if (!File.Exists(project)) { Console.Error.WriteLine($"no project {project}"); return 1; }
    foreach (var tfmDir in Directory.GetDirectories(Path.Combine(dir, "PublicAPI")).Order(StringComparer.Ordinal))
    {
        var tfm = Path.GetFileName(tfmDir);
        var shipped = Path.Combine(tfmDir, "PublicAPI.Shipped.txt");
        var unshipped = Path.Combine(tfmDir, "PublicAPI.Unshipped.txt");
        File.WriteAllText(shipped, "#nullable enable\n");
        File.WriteAllText(unshipped, "#nullable enable\n");
        var start = new ProcessStartInfo("dotnet", ["format", "analyzers", project, "--diagnostics", "RS0016", "--severity", "info", "-v", "q"]);
        start.Environment["TargetApiTfm"] = tfm;
        using (var process = Process.Start(start)!) process.WaitForExit();
        var lines = File.ReadAllLines(unshipped).Where(l => l.Length > 0 && !l.StartsWith('#')).Order(StringComparer.Ordinal);
        File.WriteAllLines(shipped, new[] { "#nullable enable" }.Concat(lines));
        File.WriteAllText(unshipped, "#nullable enable\n");
        Console.WriteLine($"{package} [{tfm}]: {lines.Count()} members");
    }
}

return 0;

static class SourceFile
{
    public static string Path([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}

