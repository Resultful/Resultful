using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

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
        public static OpenApiDocument? Read(string text, string path, out IReadOnlyList<string> problems)
        {
            var settings = new OpenApiReaderSettings();
            settings.AddYamlReader();
            ReadResult read;
            try
            {
                read = OpenApiDocument.Parse(text, path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "json" : "yaml", settings);
            }
#pragma warning disable CA1031 // A spec that can't be read is reported, never thrown into the caller.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                problems = new[] { ex.Message };
                return null;
            }

            problems = read.Diagnostic?.Errors.Select(e => e.ToString()).ToList() ?? new List<string>();
            return read.Document;
        }
    }
}
