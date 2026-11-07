using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Matchmark.Generator
{
    internal static class Naming
    {
        /// <summary>The name used for each HTTP status in generated case types and in the TypedResults factory.</summary>
        public static readonly IReadOnlyDictionary<int, string> StatusNames = new Dictionary<int, string>
        {
            [200] = "Ok",
            [201] = "Created",
            [202] = "Accepted",
            [203] = "NonAuthoritativeInformation",
            [204] = "NoContent",
            [205] = "ResetContent",
            [206] = "PartialContent",
            [207] = "MultiStatus",
            [300] = "MultipleChoices",
            [301] = "MovedPermanently",
            [302] = "Found",
            [303] = "SeeOther",
            [304] = "NotModified",
            [307] = "TemporaryRedirect",
            [308] = "PermanentRedirect",
            [400] = "BadRequest",
            [401] = "Unauthorized",
            [402] = "PaymentRequired",
            [403] = "Forbidden",
            [404] = "NotFound",
            [405] = "MethodNotAllowed",
            [406] = "NotAcceptable",
            [408] = "RequestTimeout",
            [409] = "Conflict",
            [410] = "Gone",
            [411] = "LengthRequired",
            [412] = "PreconditionFailed",
            [413] = "ContentTooLarge",
            [414] = "UriTooLong",
            [415] = "UnsupportedMediaType",
            [416] = "RangeNotSatisfiable",
            [417] = "ExpectationFailed",
            [421] = "MisdirectedRequest",
            [422] = "UnprocessableEntity",
            [423] = "Locked",
            [424] = "FailedDependency",
            [425] = "TooEarly",
            [426] = "UpgradeRequired",
            [428] = "PreconditionRequired",
            [429] = "TooManyRequests",
            [431] = "RequestHeaderFieldsTooLarge",
            [451] = "UnavailableForLegalReasons",
            [500] = "InternalServerError",
            [501] = "NotImplemented",
            [502] = "BadGateway",
            [503] = "ServiceUnavailable",
            [504] = "GatewayTimeout",
            [507] = "InsufficientStorage",
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
