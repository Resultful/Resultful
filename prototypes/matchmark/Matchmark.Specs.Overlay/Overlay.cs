using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Matchmark.Specs.Overlay
{
    /// <summary>An Overlay document, read: its version, info, the document it extends, its actions and (1.2) components.</summary>
    public sealed class OverlayDocument
    {
        public OverlayDocument(string version, OverlayInfo info, string? extends, string? self, IReadOnlyList<OverlayAction> actions,
            IReadOnlyDictionary<string, ReusableAction> reusable, IReadOnlyDictionary<string, JsonNode?> extensions)
        {
            Version = version;
            Info = info;
            Extends = extends;
            Self = self;
            Actions = actions;
            Reusable = reusable;
            Extensions = extensions;
        }

        /// <summary>The <c>overlay</c> field: the specification's version, 1.0.x, 1.1.x or 1.2.x.</summary>
        public string Version { get; }

        public OverlayInfo Info { get; }

        public string? Extends { get; }

        /// <summary>1.2's <c>$self</c>: the document's own URI.</summary>
        public string? Self { get; }

        /// <summary>The actions in order, each reusable action reference (1.2) already resolved to the action it stands for.</summary>
        public IReadOnlyList<OverlayAction> Actions { get; }

        /// <summary>1.2's <c>components.actions</c>, by key.</summary>
        public IReadOnlyDictionary<string, ReusableAction> Reusable { get; }

        public IReadOnlyDictionary<string, JsonNode?> Extensions { get; }
    }

    public sealed class OverlayInfo
    {
        public OverlayInfo(string title, string version, string? description)
        {
            Title = title;
            Version = version;
            Description = description;
        }

        public string Title { get; }

        public string Version { get; }

        public string? Description { get; }
    }

    /// <summary>One action: what it targets, and whether it removes, copies (1.1) or updates what it selects.</summary>
    public sealed class OverlayAction
    {
        public OverlayAction(string target, string? description, JsonNode? update, string? copy, bool remove, string? reference = null)
        {
            Target = target;
            Description = description;
            Update = update;
            Copy = copy;
            Remove = remove;
            Reference = reference;
        }

        /// <summary>An RFC 9535 JSONPath query.</summary>
        public string Target { get; }

        public string? Description { get; }

        public JsonNode? Update { get; }

        /// <summary>A JSONPath query selecting the one node to copy into each target (1.1).</summary>
        public string? Copy { get; }

        public bool Remove { get; }

        /// <summary>The reusable action this was referred to as (1.2's <c>$ref</c>), when it was.</summary>
        public string? Reference { get; }
    }

    /// <summary>1.2's reusable action: an action's fields, without a target, given one where it's referred to.</summary>
    public sealed class ReusableAction
    {
        public ReusableAction(string? description, JsonNode? update, string? copy, bool remove)
        {
            Description = description;
            Update = update;
            Copy = copy;
            Remove = remove;
        }

        public string? Description { get; }

        public JsonNode? Update { get; }

        public string? Copy { get; }

        public bool Remove { get; }
    }

    /// <summary>
    /// Reads an Overlay document as the specification's text and schema define it (specs/overlay), for the version it
    /// declares: 1.0, 1.1 (<c>copy</c>, <c>info.description</c>) or 1.2 (<c>$self</c>, <c>components</c>, reusable action
    /// references). Every problem is reported at its JSON pointer; a document with none is valid.
    /// </summary>
    public static class OverlayReader
    {
        private static readonly Regex VersionPattern = new Regex(@"^1\.(?<minor>[012])\.\d+$", RegexOptions.CultureInvariant);

        public static ReadResult<OverlayDocument> Read(string text, string path) => Read(Documents.Load(text, path));

        public static ReadResult<OverlayDocument> Read(JsonNode? node)
        {
            var problems = new List<Problem>();
            var shape = new Shape(problems);
            if (shape.Object(node, string.Empty, "An Overlay document") is not { } root)
            {
                return new ReadResult<OverlayDocument>(null, problems);
            }

            var version = shape.String(root, "overlay", string.Empty, required: true);
            var minor = -1;
            if (version is not null)
            {
                if (VersionPattern.Match(version) is { Success: true } match)
                {
                    minor = match.Groups["minor"].Value[0] - '0';
                }
                else
                {
                    shape.Add("/overlay", $"'{version}' isn't an Overlay version this reader knows: 1.0.x, 1.1.x or 1.2.x.");
                }
            }

            var v12 = minor >= 2;
            shape.Only(root, string.Empty, "An Overlay document", v12 ? ["overlay", "info", "extends", "actions", "$self", "components"] : ["overlay", "info", "extends", "actions"]);

            var info = Info(root, shape, minor);
            var extends = shape.String(root, "extends", string.Empty);
            var self = v12 ? shape.String(root, "$self", string.Empty) : null;
            if (v12)
            {
                // 1.2: both are URI references to whole documents, so neither may have a fragment.
                foreach (var (field, value) in new[] { ("extends", extends), ("$self", self) })
                {
                    if (value is not null && value.Contains("#"))
                    {
                        shape.Add(Pointer.Join(string.Empty, field), $"'{field}' refers to a whole document, so it can't have a fragment ('#').");
                    }
                }
            }

            var reusable = v12 ? Components(root, shape) : new Dictionary<string, ReusableAction>(StringComparer.Ordinal);
            var actions = Actions(root, shape, minor, reusable);
            var document = version is null || info is null || actions is null
                ? null
                : new OverlayDocument(version, info, extends, self, actions, reusable, Shape.Extensions(root));
            return new ReadResult<OverlayDocument>(document, problems);
        }

        private static OverlayInfo? Info(JsonObject root, Shape shape, int minor)
        {
            if (!root.TryGetPropertyValue("info", out var node))
            {
                shape.Add(string.Empty, "'info' is required.");
                return null;
            }

            if (shape.Object(node, "/info", "'info'") is not { } info)
            {
                return null;
            }

            shape.Only(info, "/info", "The Info Object", minor >= 1 ? ["title", "version", "description"] : ["title", "version"]);
            var title = shape.String(info, "title", "/info", required: true);
            var version = shape.String(info, "version", "/info", required: true);
            var description = minor >= 1 ? shape.String(info, "description", "/info") : null;
            return title is null || version is null ? null : new OverlayInfo(title, version, description);
        }

        private static Dictionary<string, ReusableAction> Components(JsonObject root, Shape shape)
        {
            var reusable = new Dictionary<string, ReusableAction>(StringComparer.Ordinal);
            if (!root.TryGetPropertyValue("components", out var node) || shape.Object(node, "/components", "'components'") is not { } components)
            {
                return reusable;
            }

            shape.Only(components, "/components", "The Components Object", "actions");
            if (!components.TryGetPropertyValue("actions", out var actionsNode) || shape.Object(actionsNode, "/components/actions", "'components.actions'") is not { } actions)
            {
                return reusable;
            }

            foreach (var pair in actions)
            {
                var pointer = Pointer.Join("/components/actions", pair.Key);
                if (shape.Object(pair.Value, pointer, $"The reusable action '{pair.Key}'") is not { } action)
                {
                    continue;
                }

                // A reusable action holds its fields under 'fields': an action's own fields beside them aren't allowed.
                shape.Only(action, pointer, $"The reusable action '{pair.Key}'", "description", "fields");
                var description = shape.String(action, "description", pointer);
                JsonObject? fields = null;
                if (action.TryGetPropertyValue("fields", out var fieldsNode))
                {
                    fields = shape.Object(fieldsNode, Pointer.Join(pointer, "fields"), "'fields'");
                }

                fields ??= new JsonObject();
                var fieldsPointer = Pointer.Join(pointer, "fields");
                shape.Only(fields, fieldsPointer, "A reusable action's fields", "description", "update", "copy", "remove");
                fields.TryGetPropertyValue("update", out var update);
                reusable[pair.Key] = new ReusableAction(
                    shape.String(fields, "description", fieldsPointer) ?? description,
                    update,
                    shape.String(fields, "copy", fieldsPointer),
                    shape.Boolean(fields, "remove", fieldsPointer));
            }

            return reusable;
        }

        private static List<OverlayAction>? Actions(JsonObject root, Shape shape, int minor, Dictionary<string, ReusableAction> reusable)
        {
            if (!root.TryGetPropertyValue("actions", out var node))
            {
                shape.Add(string.Empty, "'actions' is required.");
                return null;
            }

            if (node is not JsonArray actions)
            {
                shape.Add("/actions", "'actions' must be an array.");
                return null;
            }

            if (actions.Count == 0)
            {
                shape.Add("/actions", "'actions' must hold at least one action.");
            }

            var read = new List<OverlayAction>();
            for (var i = 0; i < actions.Count; i++)
            {
                var pointer = Pointer.Join("/actions", i);
                for (var j = 0; j < i; j++)
                {
                    if (JsonNode.DeepEquals(actions[i], actions[j]))
                    {
                        shape.Add(pointer, $"The action repeats action {j}: each must be unique.");
                        break;
                    }
                }

                if (shape.Object(actions[i], pointer, "An action") is not { } action)
                {
                    continue;
                }

                if (minor >= 2 && action.ContainsKey("$ref"))
                {
                    if (Reference(action, pointer, shape, reusable) is { } referred)
                    {
                        read.Add(referred);
                    }

                    continue;
                }

                shape.Only(action, pointer, "An Action Object", minor >= 1 ? ["target", "description", "update", "copy", "remove"] : ["target", "description", "update", "remove"]);
                var target = Target(action, pointer, shape);
                var description = shape.String(action, "description", pointer);
                var copy = minor >= 1 ? shape.String(action, "copy", pointer) : null;
                var remove = shape.Boolean(action, "remove", pointer);
                action.TryGetPropertyValue("update", out var update);
                if (target is not null)
                {
                    read.Add(new OverlayAction(target, description, update, copy, remove));
                }
            }

            return read;
        }

        private static string? Target(JsonObject action, string pointer, Shape shape)
        {
            var target = shape.String(action, "target", pointer, required: true);
            if (target is not null && !target.StartsWith("$", StringComparison.Ordinal))
            {
                shape.Add(Pointer.Join(pointer, "target"), $"'{target}' isn't a JSONPath query: it must start with '$'.");
                return null;
            }

            return target;
        }

        /// <summary>1.2: <c>$ref</c> to <c>#/components/actions/{key}</c> (escaped as a JSON pointer), with a target of its own.</summary>
        private static OverlayAction? Reference(JsonObject action, string pointer, Shape shape, Dictionary<string, ReusableAction> reusable)
        {
            shape.Only(action, pointer, "A Reusable Action Reference Object", "$ref", "target", "description");
            var reference = shape.String(action, "$ref", pointer, required: true);
            var target = Target(action, pointer, shape);
            var description = shape.String(action, "description", pointer);
            if (reference is null)
            {
                return null;
            }

            const string Prefix = "#/components/actions/";
            var key = reference.StartsWith(Prefix, StringComparison.Ordinal) ? reference.Substring(Prefix.Length) : null;
            // The key is one JSON pointer token: a '/' in it must be '~1', and a '~' must start '~0' or '~1'.
            if (key is null || key.Contains("/") || Regex.IsMatch(key, "~(?![01])"))
            {
                shape.Add(Pointer.Join(pointer, "$ref"), $"'{reference}' isn't a reference to a reusable action: '{Prefix}' and one escaped key.");
                return null;
            }

            if (!reusable.TryGetValue(Pointer.Unescape(key), out var found))
            {
                shape.Add(Pointer.Join(pointer, "$ref"), $"'{reference}' refers to no reusable action.");
                return null;
            }

            return target is null ? null : new OverlayAction(target, description ?? found.Description, found.Update, found.Copy, found.Remove, reference);
        }
    }

    /// <summary>
    /// Applies an Overlay to a document, action by action: <c>remove</c> removes each node the target selects, <c>copy</c>
    /// merges a copy of the node it selects into each one, and <c>update</c> merges its value into each one (objects merge
    /// recursively, arrays get the update appended, anything else is replaced).
    /// </summary>
    public static class OverlayApplier
    {
        public static void Apply(JsonNode document, OverlayDocument overlay)
        {
            foreach (var action in overlay.Actions)
            {
                var nodes = JsonPath.Select(document, action.Target);
                if (action.Remove)
                {
                    foreach (var node in nodes)
                    {
                        Remove(node);
                    }

                    continue;
                }

                var update = action.Copy is { } source ? JsonPath.Select(document, source).FirstOrDefault()?.DeepClone() : action.Update;
                if (update is null)
                {
                    continue;
                }

                foreach (var node in nodes)
                {
                    Merge(node, update);
                }
            }
        }

        private static void Merge(JsonNode target, JsonNode update)
        {
            switch (target, update)
            {
                case (JsonObject to, JsonObject from):
                    foreach (var property in from.ToList())
                    {
                        // Objects merge and arrays append, all the way down (the traits example keeps 'id' and adds 'top' and 'skip').
                        if (to[property.Key] is JsonObject && property.Value is JsonObject || to[property.Key] is JsonArray && property.Value is JsonArray)
                        {
                            Merge(to[property.Key]!, property.Value!);
                        }
                        else
                        {
                            to[property.Key] = property.Value?.DeepClone();
                        }
                    }

                    break;
                case (JsonArray to, JsonArray from):
                    foreach (var item in from)
                    {
                        to.Add(item?.DeepClone());
                    }

                    break;
                case (JsonArray to, _):
                    to.Add(update.DeepClone());
                    break;
                default:
                    if (target.Parent is JsonObject parent)
                    {
                        parent[target.GetPropertyName()] = update.DeepClone();
                    }
                    else if (target.Parent is JsonArray array)
                    {
                        array[target.GetElementIndex()] = update.DeepClone();
                    }

                    break;
            }
        }

        private static void Remove(JsonNode node)
        {
            switch (node.Parent)
            {
                case JsonObject parent:
                    parent.Remove(node.GetPropertyName());
                    break;
                case JsonArray array:
                    array.RemoveAt(node.GetElementIndex());
                    break;
            }
        }
    }
}
