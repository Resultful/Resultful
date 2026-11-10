using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Matchmark.Generator
{
    /// <summary>
    /// A spec split across files, made one (question 2): every <c>$ref</c> to another file resolved relative to the file
    /// that makes it, its target hoisted into the components section its place calls for (a schema, parameter or
    /// response) and named after its pointer or file; references in a referenced file resolved the same way, and a cycle
    /// ending at the hoisted name. The files come from <c>read</c>, so the generator can give it its AdditionalFiles
    /// (MatchmarkSpecPart) and the CLI the file system.
    /// </summary>
    public static class SpecBundle
    {
        private static readonly char[] Slash = { '/' };

        /// <summary>Whether a spec refers to another file, so it needs its parts.</summary>
        public static bool Split(string text)
            => System.Text.RegularExpressions.Regex.IsMatch(text, @"\$ref['""]?\s*:\s*['""]?[^'""#\s][^'""\s]*");

        /// <summary>The spec at <paramref name="path"/> made one, reading it and its parts through <paramref name="read"/> (null for a file that isn't there).</summary>
        public static JsonObject Bundle(string path, Func<string, string?> read) => new Bundler(Normalize(path), file => read(file)).Run();

        /// <summary>A path with . and .. resolved and one kind of separator, without touching the file system.</summary>
        public static string Normalize(string path)
        {
            var parts = new List<string>();
            foreach (var part in path.Replace('\\', '/').Split('/'))
            {
                if (part == ".." && parts.Count > 0 && parts[parts.Count - 1] != "..")
                {
                    parts.RemoveAt(parts.Count - 1);
                }
                else if (part != "." && (part.Length > 0 || parts.Count == 0))
                {
                    parts.Add(part);
                }
            }

            return string.Join("/", parts);
        }

        private sealed class Bundler(string rootPath, Func<string, string?> read)
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
                    var text = read(file) ?? throw new InvalidOperationException($"{file} isn't one of the spec's parts.");
                    node = Overlay.Parse(text, file) ?? throw new InvalidOperationException($"{file} is empty.");
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

                        foreach (var pair in obj.ToList())
                        {
                            var key = pair.Key;
                            var child = pair.Value;
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
                var target = hash < 0 ? reference : reference.Substring(0, hash);
                var pointer = hash < 0 ? string.Empty : reference.Substring(hash + 1);
                var targetFile = target.Length == 0 ? file : Normalize(file.Substring(0, Math.Max(0, file.LastIndexOf('/'))) + "/" + target);
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
                local = "#/" + string.Join("/", section) + "/" + name.Replace("~", "~0").Replace("/", "~1");
                _hoisted[key] = local;
                var value = Pointer(Load(targetFile), pointer)?.DeepClone() ?? throw new InvalidOperationException($"{reference}, from {file}, names nothing.");
                Container(section)[name] = value;
                Walk(value, targetFile, section[section.Length - 1]);
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
                var name = pointer.Split(Slash, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Replace("~1", "/").Replace("~0", "~")
                           ?? Path.GetFileNameWithoutExtension(file.Substring(file.LastIndexOf('/') + 1));
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
                foreach (var segment in pointer.Split(Slash, StringSplitOptions.RemoveEmptyEntries))
                {
                    node = node?[segment.Replace("~1", "/").Replace("~0", "~")];
                }

                return node;
            }
        }
    }
}
