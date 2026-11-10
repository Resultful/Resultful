using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Matchmark.Specs;
using Matchmark.Specs.Overlay;

namespace Matchmark.Generator
{
    /// <summary>
    /// Overlays applied to a spec before it's read, through Matchmark's own Overlay reader and applier
    /// (Matchmark.Specs.Overlay, decision Q56). Behaviour and invariants can be attached to a spec this way without
    /// editing it, including a third-party one.
    /// </summary>
    public static class Overlay
    {
        /// <summary>The spec (YAML or JSON) with each overlay (YAML or JSON) applied in turn, as JSON.</summary>
        public static string Apply(string spec, string specPath, IEnumerable<(string Text, string Path)> overlays) => Apply(spec, specPath, overlays, out _);

        /// <summary>
        /// As <see cref="Apply(string, string, IEnumerable{ValueTuple{string, string}})"/>, with each overlay's problems: an
        /// overlay that reads with problems is applied as far as it was read, and one that can't be read at all throws.
        /// </summary>
        public static string Apply(string spec, string specPath, IEnumerable<(string Text, string Path)> overlays, out IReadOnlyList<string> problems)
        {
            var document = Parse(spec, specPath);
            var found = new List<string>();
            foreach (var (text, path) in overlays)
            {
                var read = OverlayReader.Read(text, path);
                found.AddRange(read.Problems.Select(p => $"{path} {p}"));
                if (read.Document is null)
                {
                    throw new InvalidOperationException($"{path} isn't an Overlay document Matchmark can apply: {string.Join("; ", read.Problems)}");
                }

                OverlayApplier.Apply(document, read.Document);
            }

            problems = found;
            return document.ToJsonString();
        }

        /// <summary>A document (YAML or JSON) as a JSON tree, by Matchmark's own YAML reading.</summary>
        internal static JsonNode Parse(string text, string path) => Documents.Load(text, path);
    }
}
