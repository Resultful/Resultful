using Resultful.OpenApi.Generator;
using Resultful.OpenApi.Scaffold;

const string Usage = """
    Writes the server side of an OpenAPI spec as ASP.NET source you own. The contract (types, unions, handler
    interfaces) stays generated: reference a project that generates it (ResultfulParts="Contract").

    usage: scaffold <spec> <output dir> [--style controllers|minimal] [--contract-namespace NS] [--namespace NS]
                    [--api NAME] [--force]

      --style               controllers (default): [ApiController] classes; minimal: Minimal API endpoints
      --contract-namespace  where the generated contract lives (default: <api>.Api)
      --namespace           the scaffolded code's namespace (default: <api>)
      --api                 the API name used in type names (default: from the spec file name)
      --force               overwrite files that exist. Without it, existing files are kept, so re-running after a
                            spec change only adds what is new; the compiler then shows what no longer fits.
    """;

if (args.Length < 2 || args.Contains("--help"))
{
    Console.WriteLine(Usage);
    return args.Contains("--help") ? 0 : 1;
}

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

var files = Scaffolder.Scaffold(
    File.ReadAllText(spec), Path.GetFileName(spec), Option("--contract-namespace", $"{api}.Api"), Option("--namespace", api), api,
    controllers: style == "controllers", out var warnings);

foreach (var warning in warnings)
{
    Console.Error.WriteLine($"warning: {warning}");
}

foreach (var (path, written) in ScaffoldWriter.WriteTo(files, output, force))
{
    Console.WriteLine(written ? $"  wrote    {path}" : $"  kept     {path} (exists; --force to overwrite)");
}

return 0;
