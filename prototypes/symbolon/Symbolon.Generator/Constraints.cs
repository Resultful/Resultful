using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.OpenApi;

namespace Symbolon.Generator
{
    /// <summary>
    /// Validation constraints in one vocabulary, the schema's keywords: minLength, maxLength, pattern, minimum, maximum,
    /// exclusiveMinimum, exclusiveMaximum, minItems and maxItems. On the C# side they are DataAnnotations, read as
    /// ASP.NET Core's OpenAPI document writes them, so a match here is a match in the served document.
    /// </summary>
    internal static class Constraints
    {
        public static IReadOnlyList<(string Keyword, string Value)> Of(IOpenApiSchema schema)
        {
            var found = new List<(string, string)>();
            var array = (schema.Type ?? 0).HasFlag(JsonSchemaType.Array);
            Add(found, array ? "minItems" : "minLength", (array ? schema.MinItems : schema.MinLength)?.ToString(CultureInfo.InvariantCulture));
            Add(found, array ? "maxItems" : "maxLength", (array ? schema.MaxItems : schema.MaxLength)?.ToString(CultureInfo.InvariantCulture));
            Add(found, "pattern", schema.Pattern);
            Add(found, "minimum", schema.Minimum);
            Add(found, "maximum", schema.Maximum);
            Add(found, "exclusiveMinimum", schema.ExclusiveMinimum);
            Add(found, "exclusiveMaximum", schema.ExclusiveMaximum);
            return Sorted(found);
        }

        public static ImmutableArray<(string Keyword, string Value)> Of(ImmutableArray<AttributeData> attributes, bool array)
        {
            var found = new List<(string, string)>();
            foreach (var attribute in attributes.Where(a => a.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations"))
            {
                var args = attribute.ConstructorArguments;
                switch (attribute.AttributeClass!.Name)
                {
                    case "MinLengthAttribute" when args.Length == 1:
                        Add(found, array ? "minItems" : "minLength", Text(args[0]));
                        break;
                    case "MaxLengthAttribute" when args.Length == 1:
                        Add(found, array ? "maxItems" : "maxLength", Text(args[0]));
                        break;
                    case "LengthAttribute" when args.Length == 2:
                        Add(found, array ? "minItems" : "minLength", Text(args[0]));
                        Add(found, array ? "maxItems" : "maxLength", Text(args[1]));
                        break;
                    case "StringLengthAttribute" when args.Length == 1:
                        Add(found, "minLength", Named(attribute, "MinimumLength"));
                        Add(found, "maxLength", Text(args[0]));
                        break;
                    case "RegularExpressionAttribute" when args.Length == 1:
                        Add(found, "pattern", Text(args[0]));
                        break;
                    case "RangeAttribute" when args.Length >= 2:
                        var (min, max) = args.Length == 3 ? (args[1], args[2]) : (args[0], args[1]);
                        Add(found, Named(attribute, "MinimumIsExclusive") == "true" ? "exclusiveMinimum" : "minimum", Text(min));
                        Add(found, Named(attribute, "MaximumIsExclusive") == "true" ? "exclusiveMaximum" : "maximum", Text(max));
                        break;
                }
            }

            return Sorted(found).ToImmutableArray();
        }

        /// <summary>Each way <paramref name="actual"/> differs from <paramref name="expected"/>, worded for <paramref name="what"/>.</summary>
        public static IEnumerable<string> Differences(string what, IReadOnlyList<(string Keyword, string Value)> expected, IReadOnlyList<(string Keyword, string Value)> actual,
            IReadOnlyList<(string Keyword, string Value)>? misplaced = null)
        {
            foreach (var (keyword, value) in expected)
            {
                var declared = actual.Where(a => a.Keyword == keyword).Select(a => a.Value).FirstOrDefault();
                if (declared is null && misplaced?.Any(m => m.Keyword == keyword) == true)
                {
                    yield return $"{what} declares {keyword} on the record's parameter, which the OpenAPI document doesn't see; target the property: [property: {Attribute(keyword, value, expected).Trim('[', ']')}]";
                }
                else if (declared is null)
                {
                    yield return $"{what} doesn't declare the schema's {keyword} {value}; add {Attribute(keyword, value, expected)}";
                }
                else if (declared != value)
                {
                    yield return $"{what} declares {keyword} {declared}; the schema says {value}";
                }
            }

            foreach (var (keyword, value) in actual.Where(a => expected.All(e => e.Keyword != a.Keyword)))
            {
                yield return $"{what} declares {keyword} {value}, which the schema doesn't have";
            }
        }

        private static string Attribute(string keyword, string value, IReadOnlyList<(string Keyword, string Value)> all)
        {
            string? Other(string k) => all.Where(a => a.Keyword == k).Select(a => a.Value).FirstOrDefault();
            return keyword switch
            {
                "minLength" or "minItems" => $"[MinLength({value})]",
                "maxLength" or "maxItems" => $"[MaxLength({value})]",
                "pattern" => $"[RegularExpression({Naming.Literal(value)})]",
                "minimum" or "exclusiveMinimum" or "maximum" or "exclusiveMaximum" =>
                    $"[Range({Other("minimum") ?? Other("exclusiveMinimum") ?? "double.MinValue"}, {Other("maximum") ?? Other("exclusiveMaximum") ?? "double.MaxValue"}"
                    + (Other("exclusiveMinimum") is not null ? ", MinimumIsExclusive = true" : string.Empty)
                    + (Other("exclusiveMaximum") is not null ? ", MaximumIsExclusive = true" : string.Empty) + ")]",
                _ => keyword,
            };
        }

        private static void Add(List<(string, string)> found, string keyword, string? value)
        {
            if (value is null)
            {
                return;
            }

            // A zero lower bound on a length says nothing; ASP.NET Core may or may not write it.
            if (keyword is "minLength" or "minItems" && value == "0")
            {
                return;
            }

            found.Add((keyword, keyword == "pattern" ? value : Normalize(value)));
        }

        private static string Normalize(string value)
            => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d.ToString(CultureInfo.InvariantCulture) : value;

        private static string? Text(TypedConstant constant)
            => constant.Value switch
            {
                null => null,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                var other => other.ToString(),
            };

        private static string? Named(AttributeData attribute, string name)
            => attribute.NamedArguments.Where(n => n.Key == name).Select(n => n.Value.Value is bool b ? (b ? "true" : "false") : Text(n.Value)).FirstOrDefault();

        private static List<(string Keyword, string Value)> Sorted(List<(string Keyword, string Value)> found)
            => found.GroupBy(f => f.Keyword).Select(g => g.Last()).OrderBy(f => f.Keyword, StringComparer.Ordinal).ToList();
    }
}
