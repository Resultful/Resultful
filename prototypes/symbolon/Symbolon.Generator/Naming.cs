using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Symbolon.Generator
{
    internal static class Naming
    {
        /// <summary>The name used for each HTTP status in generated case types and in the TypedResults factory.</summary>
        public static readonly IReadOnlyDictionary<int, string> StatusNames = new Dictionary<int, string>
        {
            [200] = "Ok",
            [201] = "Created",
            [202] = "Accepted",
            [204] = "NoContent",
            [400] = "BadRequest",
            [401] = "Unauthorized",
            [403] = "Forbidden",
            [404] = "NotFound",
            [409] = "Conflict",
            [422] = "UnprocessableEntity",
            [500] = "InternalServerError",
        };

        /// <summary>"petId" → "PetId", "list-pets" → "ListPets", "available" → "Available".</summary>
        public static string Pascal(string value)
        {
            var builder = new StringBuilder(value.Length);
            var upperNext = true;
            foreach (var c in value)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    upperNext = true;
                    continue;
                }

                builder.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }

            if (builder.Length == 0 || char.IsDigit(builder[0]))
            {
                builder.Insert(0, '_');
            }

            return builder.ToString();
        }

        /// <summary>A valid C# parameter name: "petId" → "petId", "class" → "@class".</summary>
        public static string Camel(string value)
        {
            var pascal = Pascal(value);
            var camel = char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
            return SyntaxFacts.GetKeywordKind(camel) != SyntaxKind.None ? "@" + camel : camel;
        }

        public static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

        public static string Invariant(this int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
