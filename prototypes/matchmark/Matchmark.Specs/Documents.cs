using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SharpYaml.Serialization;

namespace Matchmark.Specs
{
    /// <summary>
    /// A document as a JSON tree: JSON as it is, YAML by YAML 1.2's core schema (plain <c>true</c>, <c>null</c>, <c>12</c>
    /// and <c>1.5</c> are a boolean, null and numbers; <c>1.0.0</c>, <c>'12'</c> and anything quoted are strings).
    /// </summary>
    public static class Documents
    {
        private static readonly Regex Integer = new Regex(@"^[-+]?[0-9]+$", RegexOptions.CultureInvariant);
        private static readonly Regex Octal = new Regex(@"^0o[0-7]+$", RegexOptions.CultureInvariant);
        private static readonly Regex Hexadecimal = new Regex(@"^0x[0-9a-fA-F]+$", RegexOptions.CultureInvariant);
        private static readonly Regex Float = new Regex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant);

        /// <summary>The document at <paramref name="path"/> (its extension says JSON or YAML), or an exception saying why not.</summary>
        public static JsonNode Load(string text, string path)
        {
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    return JsonNode.Parse(text) ?? throw new InvalidOperationException($"{path} is empty.");
                }
                catch (JsonException) when (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    // YAML that happens to start with a brace: read as YAML below.
                }
            }

            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count == 0)
            {
                throw new InvalidOperationException($"{path} is empty.");
            }

            return Convert(stream.Documents[0].RootNode, 0) ?? throw new InvalidOperationException($"{path} is null.");
        }

        private static JsonNode? Convert(YamlNode node, int depth)
        {
            if (depth > 256)
            {
                throw new InvalidOperationException("The document nests too deeply (or an alias refers to itself).");
            }

            switch (node)
            {
                case YamlMappingNode mapping:
                    var obj = new JsonObject();
                    foreach (var pair in mapping.Children)
                    {
                        var key = pair.Key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : pair.Key.ToString();
                        obj[key] = Convert(pair.Value, depth + 1);
                    }

                    return obj;
                case YamlSequenceNode sequence:
                    return new JsonArray(sequence.Children.Select(c => Convert(c, depth + 1)).ToArray());
                case YamlScalarNode scalar:
                    return Scalar(scalar);
                default:
                    return null;
            }
        }

        private static JsonValue? Scalar(YamlScalarNode scalar)
        {
            var text = scalar.Value ?? string.Empty;
            if (scalar.Style != SharpYaml.ScalarStyle.Plain || scalar.Tag == "tag:yaml.org,2002:str")
            {
                return JsonValue.Create(text);
            }

            switch (text)
            {
                case "" or "~" or "null" or "Null" or "NULL":
                    return null;
                case "true" or "True" or "TRUE":
                    return JsonValue.Create(true);
                case "false" or "False" or "FALSE":
                    return JsonValue.Create(false);
                case ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF" or "-.inf" or "-.Inf" or "-.INF" or ".nan" or ".NaN" or ".NAN":
                    return JsonValue.Create(text); // JSON has no infinity or NaN: kept as the text
            }

            if (Integer.IsMatch(text))
            {
                return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)
                    ? JsonValue.Create(whole)
                    : decimal.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var big) ? JsonValue.Create(big) : JsonValue.Create(text);
            }

            if (Octal.IsMatch(text))
            {
                return JsonValue.Create(System.Convert.ToInt64(text.Substring(2), 8));
            }

            if (Hexadecimal.IsMatch(text))
            {
                return JsonValue.Create(System.Convert.ToInt64(text.Substring(2), 16));
            }

            if (Float.IsMatch(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact) ? JsonValue.Create(exact) : JsonValue.Create(number);
            }

            return JsonValue.Create(text);
        }
    }

    /// <summary>A problem with a document: where it is (a JSON pointer, <c>/actions/0/target</c>) and what's wrong.</summary>
    public sealed class Problem : IEquatable<Problem>
    {
        public Problem(string pointer, string message)
        {
            Pointer = pointer;
            Message = message;
        }

        public string Pointer { get; }

        public string Message { get; }

        public bool Equals(Problem? other) => other is not null && Pointer == other.Pointer && Message == other.Message;

        public override bool Equals(object? obj) => Equals(obj as Problem);

        public override int GetHashCode() => (Pointer, Message).GetHashCode();

        public override string ToString() => $"{(Pointer.Length == 0 ? "/" : Pointer)}: {Message}";
    }

    /// <summary>A document read: the model when it could be built, and every problem found, which a valid document has none of.</summary>
    public sealed class ReadResult<T>
        where T : class
    {
        public ReadResult(T? document, IReadOnlyList<Problem> problems)
        {
            Document = document;
            Problems = problems;
        }

        public T? Document { get; }

        public IReadOnlyList<Problem> Problems { get; }

        public bool Valid => Document is not null && Problems.Count == 0;
    }

    /// <summary>JSON pointers (RFC 6901): a name escaped (<c>~</c> as <c>~0</c>, <c>/</c> as <c>~1</c>) and joined on.</summary>
    public static class Pointer
    {
        public static string Join(string pointer, string name) => pointer + "/" + Escape(name);

        public static string Join(string pointer, int index) => pointer + "/" + index.ToString(CultureInfo.InvariantCulture);

        public static string Escape(string name) => name.Replace("~", "~0").Replace("/", "~1");

        public static string Unescape(string token) => token.Replace("~1", "/").Replace("~0", "~");
    }

    /// <summary>
    /// The checks a reader makes of an object, each a problem at its pointer: fields of a type, required fields, and no
    /// field the specification doesn't define but an <c>x-</c> extension.
    /// </summary>
    public sealed class Shape
    {
        private readonly List<Problem> _problems;

        public Shape(List<Problem> problems) => _problems = problems;

        public void Add(string pointer, string message) => _problems.Add(new Problem(pointer, message));

        /// <summary>The object at <paramref name="pointer"/>, or null (a problem) when it isn't one.</summary>
        public JsonObject? Object(JsonNode? node, string pointer, string what)
        {
            if (node is JsonObject obj)
            {
                return obj;
            }

            Add(pointer, $"{what} must be an object.");
            return null;
        }

        /// <summary>A string field; a problem when it's required and missing, or isn't a string.</summary>
        public string? String(JsonObject obj, string field, string pointer, bool required = false)
        {
            if (!obj.TryGetPropertyValue(field, out var value))
            {
                if (required)
                {
                    Add(pointer, $"'{field}' is required.");
                }

                return null;
            }

            if (value is JsonValue v && v.GetValueKind() == JsonValueKind.String)
            {
                return v.GetValue<string>();
            }

            Add(Pointer.Join(pointer, field), $"'{field}' must be a string.");
            return null;
        }

        /// <summary>A boolean field, or the default.</summary>
        public bool Boolean(JsonObject obj, string field, string pointer, bool fallback = false)
        {
            if (!obj.TryGetPropertyValue(field, out var value))
            {
                return fallback;
            }

            if (value is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            {
                return v.GetValue<bool>();
            }

            Add(Pointer.Join(pointer, field), $"'{field}' must be true or false.");
            return fallback;
        }

        /// <summary>A problem for each field that's neither one of <paramref name="allowed"/> nor an <c>x-</c> extension.</summary>
        public void Only(JsonObject obj, string pointer, string what, params string[] allowed)
        {
            foreach (var field in obj.Select(p => p.Key).Where(k => !k.StartsWith("x-", StringComparison.Ordinal) && System.Array.IndexOf(allowed, k) < 0))
            {
                Add(Pointer.Join(pointer, field), $"{what} has no field '{field}'.");
            }
        }

        /// <summary>A problem for each field that isn't one of <paramref name="allowed"/>: an object that takes no extensions.</summary>
        public void Exactly(JsonObject obj, string pointer, string what, params string[] allowed)
        {
            foreach (var field in obj.Select(p => p.Key).Where(k => System.Array.IndexOf(allowed, k) < 0))
            {
                Add(Pointer.Join(pointer, field), $"{what} has no field '{field}'.");
            }
        }

        /// <summary>An array field; a problem when it's there and isn't one.</summary>
        public JsonArray? Array(JsonObject obj, string field, string pointer)
        {
            if (!obj.TryGetPropertyValue(field, out var value))
            {
                return null;
            }

            if (value is JsonArray array)
            {
                return array;
            }

            Add(Pointer.Join(pointer, field), $"'{field}' must be an array.");
            return null;
        }

        /// <summary>A problem for each item that repeats an earlier one.</summary>
        public void Unique(JsonArray array, string pointer, string field)
        {
            for (var i = 1; i < array.Count; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    if (JsonNode.DeepEquals(array[i], array[j]))
                    {
                        Add(Pointer.Join(pointer, i), $"'{field}' repeats item {j}: each must be unique.");
                        break;
                    }
                }
            }
        }

        /// <summary>The extensions: every field that starts with <c>x-</c>.</summary>
        public static IReadOnlyDictionary<string, JsonNode?> Extensions(JsonObject obj)
            => obj.Where(p => p.Key.StartsWith("x-", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }
}
