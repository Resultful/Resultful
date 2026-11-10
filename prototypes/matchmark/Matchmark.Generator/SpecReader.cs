using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Path = System.IO.Path;

namespace Matchmark.Generator
{
    /// <summary>
    /// Reads a spec leniently: whatever Microsoft.OpenApi can build is used, and what it objected to is passed on as
    /// problems for the caller to report. Real specs carry keywords one reader version disagrees with (Bump.sh's Train
    /// Travel API is refused over a valid OpenAPI 3.1 <c>xml.wrapped</c>); one such objection shouldn't refuse a whole API.
    /// Only a spec that yields no document at all is unreadable.
    /// </summary>
    public static class SpecReader
    {
        /// <param name="overlays">Overlay documents (text and path) applied to the spec, in order, before it's read.</param>
        public static OpenApiDocument? Read(string text, string path, out IReadOnlyList<string> problems, IEnumerable<(string Text, string Path)>? overlays = null)
        {
            var settings = new OpenApiReaderSettings();
            settings.AddYamlReader();
            ReadResult read;
            IReadOnlyList<string> overlayProblems = Array.Empty<string>();
            try
            {
                if (AsyncApi.IsAsyncApi(text))
                {
                    overlayProblems = AsyncApi.Problems(text, path);
                    text = AsyncApi.ToOpenApi(text, path);
                    path = Path.ChangeExtension(path, ".json");
                }

                if (overlays?.ToList() is { Count: > 0 } applied)
                {
                    text = Overlay.Apply(text, path, applied, out var applying);
                    overlayProblems = overlayProblems.Concat(applying).ToList();
                    path = Path.ChangeExtension(path, ".json");
                }

                read = OpenApiDocument.Parse(text, path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "json" : "yaml", settings);
            }
#pragma warning disable CA1031 // A spec that can't be read is reported, never thrown into the caller.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                problems = new[] { ex.Message };
                return null;
            }

            problems = overlayProblems.Concat(read.Diagnostic?.Errors.Select(e => e.ToString()) ?? Enumerable.Empty<string>()).ToList();
            return read.Document;
        }
    }
}
