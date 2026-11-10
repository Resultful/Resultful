using System.Text.Json.Nodes;
using Matchmark.Generator;

namespace Matchmark.Cli;

/// <summary>
/// <c>bundle</c>: one spec from a spec split across files (question 2, option b). Every <c>$ref</c> to another file is
/// resolved, relative to the file that makes it, and its target hoisted into the components section its place calls
/// for (a schema, parameter or response), named after its pointer or its file, so the generator, which reads one file,
/// can read the result. References inside a referenced file are resolved the same way; a cycle ends at the hoisted name.
/// </summary>
public static class BundleCommand
{
    public const string Usage = """
        usage: matchmark bundle <spec> [--output <file>]

          Writes a spec split across files (a $ref to ../common/Error.yaml, or parameters.yaml#/tagsParam) as one file: each
          referenced schema, parameter or response becomes a component named after it, so the generator can read it.
          --output  the file to write (.json for JSON, anything else YAML); without it, YAML goes to standard output
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length < 1 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            await output.WriteLineAsync(Usage);
            return 2;
        }

        JsonObject bundled;
        try
        {
            bundled = Bundle(args[0]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or SharpYaml.YamlException or System.Text.Json.JsonException)
        {
            await output.WriteLineAsync($"error: {e.Message}");
            return 2;
        }

        var file = Array.IndexOf(args, "--output") is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        var text = file?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == true
            ? bundled.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
            : SpecUpgrade.Yaml(bundled).TrimStart('\n');
        if (file is null)
        {
            await output.WriteLineAsync(text);
        }
        else
        {
            await File.WriteAllTextAsync(file, text, cancellationToken);
            await output.WriteLineAsync($"Wrote {file}.");
        }

        return 0;
    }

    /// <summary>The spec at <paramref name="path"/>, with every reference to another file brought into it.</summary>
    public static JsonObject Bundle(string path)
    {
        var root = Path.GetFullPath(path);
        var bundler = new Bundler(root);
        return bundler.Run();
    }

    private sealed class Bundler(string rootPath)
    {
        private readonly Dictionary<string, JsonNode> _files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _hoisted = new(StringComparer.Ordinal);
        private JsonObject _root = null!;
        private bool _swagger;

        public JsonObject Run()
        {
            _root = Load(rootPath) as JsonObject ?? throw new InvalidOperationException($"{rootPath} isn't a spec.");
            _swagger = _root["swagger"] is not null;
            Walk(_root, rootPath, null);
            return _root;
        }

        private JsonNode Load(string file)
        {
            if (!_files.TryGetValue(file, out var node))
            {
                // YAML or JSON, read as JSON: an overlay-free Overlay.Apply is the reader every Matchmark command uses.
                node = JsonNode.Parse(Overlay.Apply(File.ReadAllText(file), file, [])) ?? throw new InvalidOperationException($"{file} is empty.");
                _files[file] = node;
            }

            return node;
        }

        /// <summary>Resolves the references under <paramref name="node"/>, which is in <paramref name="file"/>; <paramref name="context"/> is the key it sits under.</summary>
        private void Walk(JsonNode? node, string file, string? context)
        {
            switch (node)
            {
                case JsonObject obj:
                    if (obj["$ref"] is JsonValue reference && reference.GetValueKind() == System.Text.Json.JsonValueKind.String)
                    {
                        obj["$ref"] = Resolve(reference.GetValue<string>(), file, context);
                        return;
                    }

                    foreach (var (key, child) in obj.ToList())
                    {
                        // A response sits under its status in a responses map: it's still a response.
                        var isStatus = context == "responses" && System.Text.RegularExpressions.Regex.IsMatch(key, "^([1-5][0-9X]{2}|default)$");
                        Walk(child, file, isStatus ? "responses" : key);
                    }

                    break;
                case JsonArray array:
                    foreach (var child in array.ToList())
                    {
                        Walk(child, file, context);
                    }

                    break;
            }
        }

        /// <summary>A reference as it reads in the bundle: local ones in the root unchanged; anything else hoisted and referred to locally.</summary>
        private string Resolve(string reference, string file, string? context)
        {
            var hash = reference.IndexOf('#');
            var target = hash < 0 ? reference : reference[..hash];
            var pointer = hash < 0 ? string.Empty : reference[(hash + 1)..];
            var targetFile = target.Length == 0 ? file : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, target));
            if (targetFile == rootPath)
            {
                return "#" + pointer;
            }

            var key = targetFile + "#" + pointer;
            if (_hoisted.TryGetValue(key, out var local))
            {
                return local;
            }

            var section = Section(context);
            var name = Name(pointer, targetFile, section);
            local = "#/" + string.Join('/', section) + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
            _hoisted[key] = local;
            var value = Pointer(Load(targetFile), pointer)?.DeepClone() ?? throw new InvalidOperationException($"{reference}, from {file}, names nothing.");
            Container(section)[name] = value;
            Walk(value, targetFile, section[^1]);
            return local;
        }

        /// <summary>Where a referenced thing goes, by the key its reference sits under: parameters, responses, or schemas.</summary>
        private string[] Section(string? context) => (context, _swagger) switch
        {
            ("parameters", true) => ["parameters"],
            ("responses", true) => ["responses"],
            (_, true) => ["definitions"],
            ("parameters", false) => ["components", "parameters"],
            ("responses", false) => ["components", "responses"],
            ("requestBodies" or "requestBody", false) => ["components", "requestBodies"],
            ("headers", false) => ["components", "headers"],
            (_, false) => ["components", "schemas"],
        };

        private JsonObject Container(string[] section)
        {
            var node = _root;
            foreach (var part in section)
            {
                if (node[part] is not JsonObject next)
                {
                    node[part] = next = new JsonObject();
                }

                node = next;
            }

            return node;
        }

        /// <summary>A component's name: the pointer's last segment, else the file's name; numbered when it's taken.</summary>
        private string Name(string pointer, string file, string[] section)
        {
            var name = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)
                       ?? Path.GetFileNameWithoutExtension(file);
            var container = Container(section);
            var candidate = name;
            for (var n = 2; container.ContainsKey(candidate); n++)
            {
                candidate = name + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return candidate;
        }

        private static JsonNode? Pointer(JsonNode? node, string pointer)
        {
            foreach (var segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                node = node?[segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)];
            }

            return node;
        }
    }
}
