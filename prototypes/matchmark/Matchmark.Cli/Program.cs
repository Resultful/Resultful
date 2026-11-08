using Matchmark.Generator;
using Matchmark.Cli;

const string Usage = """
    matchmark: the OpenAPI document as an enforceable contract.

    matchmark scaffold <spec> <output dir> [--style controllers|minimal] [--contract-namespace NS] [--namespace NS]
                      [--api NAME] [--overlay <file>]... [--force]

      Writes the server side of an OpenAPI spec as ASP.NET source you own. The contract (types, unions, handler
      interfaces) stays generated: reference a project that generates it (MatchmarkParts="Contract").

      --style               controllers (default): [ApiController] classes; minimal: Minimal API endpoints
      --contract-namespace  where the generated contract lives (default: <api>.Api)
      --namespace           the scaffolded code's namespace (default: <api>)
      --api                 the API name used in type names (default: from the spec file name)
      --force               overwrite files that exist. Without it, existing files are kept, so re-running after a
                            spec change only adds what is new; the compiler then shows what no longer fits.

    matchmark regress <spec> <base url> [--arazzo <file>]... [--overlay <file>]... [--no-workflows]

      --overlay applies an OpenAPI Overlay to the spec first (repeat for more, applied in order).

      Runs the regression suite generated from the spec against any HTTP API.

    matchmark audit <spec> [--arazzo <file>]... [--overlay <file>]... [--sarif <file>] [--spectral <file>] [--strict]

      Reports what the spec leaves out, each finding measured in the cases and workflows its change would add.

    matchmark diff <old spec> <new spec> [--allow-breaking]

      Lists what changed and whom it breaks; exits 1 on a breaking change the major version didn't announce.
    """;

if (args.Length > 0 && args[0] == "regress")
{
    return await Regress.RunAsync(args[1..], Console.Out);
}

if (args.Length > 0 && args[0] == "audit")
{
    return await AuditCommand.RunAsync(args[1..], Console.Out);
}

if (args.Length > 0 && args[0] == "diff")
{
    return await SpecDiff.RunAsync(args[1..], Console.Out);
}

if (args.Length < 3 || args[0] != "scaffold" || args.Contains("--help"))
{
    Console.WriteLine(Usage);
    return args.Contains("--help") ? 0 : 1;
}

args = args[1..];

string Option(string name, string fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

var spec = args[0];
var output = args[1];
var api = Option("--api", string.Concat(Path.GetFileNameWithoutExtension(spec).Split('-', '_', '.').Select(p => char.ToUpperInvariant(p[0]) + p[1..])));
var style = Option("--style", "controllers");
var force = args.Contains("--force");
if (style is not ("controllers" or "minimal"))
{
    Console.Error.WriteLine($"Unknown style '{style}'. Use controllers or minimal.");
    return 1;
}

IReadOnlyDictionary<string, string> files;
IReadOnlyList<string> warnings;
try
{
    var overlays = args.Select((a, i) => (a, i)).Where(x => x.a == "--overlay" && x.i + 1 < args.Length)
        .Select(x => (File.ReadAllText(args[x.i + 1]), args[x.i + 1])).ToList();
    files = Scaffolder.Scaffold(
        File.ReadAllText(spec), Path.GetFileName(spec), Option("--contract-namespace", $"{api}.Api"), Option("--namespace", api), api,
        controllers: style == "controllers", overlays, out warnings);
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 2;
}

foreach (var warning in warnings)
{
    Console.Error.WriteLine($"warning: {warning}");
}

foreach (var (path, written) in ScaffoldWriter.WriteTo(files, output, force))
{
    Console.WriteLine(written ? $"  wrote    {path}" : $"  kept     {path} (exists; --force to overwrite)");
}

return 0;
