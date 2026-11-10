using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>The emitter's responses (OneOf unions) (decision Q10: the model and its outputs, split by output).</summary>
    internal sealed partial class Emitter
    {
        // ---------------------------------------------------------------- responses (OneOf unions)

        private string EmitResponses(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            // Usings also writes the namespace: none for plain unions, which are self-contained.
            w.Usings(PlainOutcomes ? System.Array.Empty<string>() : OneOfUsing);

            foreach (var operation in operations)
            {
                if (operation.SuccessIsUnion)
                {
                    EmitUnion(w, operation, operation.SuccessType, operation.Successes, "The success responses");
                }

                if (operation.Errors.Count > 0)
                {
                    EmitUnion(w, operation, operation.ErrorType, operation.Errors, "The error responses");
                }
            }

            return w.ToString();
        }

        private void EmitUnion(CodeWriter w, Operation operation, string unionName, List<Response> cases, string summary)
        {
            foreach (var response in cases)
            {
                var caseName = operation.Name + response.CaseName;
                if (response.CarriesStatus)
                {
                    w.Line($"/// <summary>Any status the spec doesn't name for <c>{operation.Id}</c> (its default response), with the status.</summary>");
                    w.Line(response.BodyType is null
                        ? $"public sealed record {caseName}(int Status);"
                        : $"public sealed record {caseName}(int Status, {response.BodyType} Body);");
                    w.Line();
                    continue;
                }

                w.Line($"/// <summary>{response.Status.Invariant()} {response.CaseName} from <c>{operation.Id}</c>.</summary>");
                w.Line(response.BodyType is null
                    ? $"public sealed record {caseName};"
                    : $"public sealed record {caseName}({response.BodyType} Body);");
                w.Line();
            }

            var caseTypes = string.Join(", ", cases.Select(c => operation.Name + c.CaseName));
            w.Line($"/// <summary>{summary} of <c>{operation.Id}</c>, from the spec.</summary>");
            if (PlainOutcomes)
            {
                EmitPlainUnion(w, unionName, cases.Select(c => operation.Name + c.CaseName).ToList());
            }
            else
            {
                w.Open($"public sealed class {unionName} : OneOfBase<{caseTypes}>");
                w.Line($"private {unionName}(OneOf<{caseTypes}> value) : base(value) {{ }}");
            }

            foreach (var response in cases)
            {
                var caseName = operation.Name + response.CaseName;
                w.Line();
                w.Line($"public static implicit operator {unionName}({caseName} value) => new(value);");
                w.Line();
                w.Line((response.CarriesStatus, response.BodyType is null) switch
                {
                    (true, true) => $"public static {unionName} {response.CaseName}(int status) => new {caseName}(status);",
                    (true, false) => $"public static {unionName} {response.CaseName}(int status, {response.BodyType} body) => new {caseName}(status, body);",
                    (false, true) => $"public static {unionName} {response.CaseName}() => new {caseName}();",
                    _ => $"public static {unionName} {response.CaseName}({response.BodyType} body) => new {caseName}(body);",
                });
            }

            w.Close();
            w.Line();
        }
    }
}
