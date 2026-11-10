using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Matchmark.Generator
{
    /// <summary>
    /// The model (docs/reference/model.md, decision Q10), read only: what the emitter decides about a spec, once, before any
    /// output is written: each operation, the C# type of each parameter and body, and its outcomes. Every output is a
    /// function of these decisions, and the model's own tests pin them independently of any output.
    /// </summary>
    public sealed class ApiModel
    {
        private ApiModel(IReadOnlyList<ModelOperation> operations, IReadOnlyList<string> diagnostics)
        {
            Operations = operations;
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<ModelOperation> Operations { get; }

        /// <summary>What the model couldn't represent, or what the spec leaves out, as the generator reports it (MM001 and on).</summary>
        public IReadOnlyList<string> Diagnostics { get; }

        /// <summary>Reads a spec (YAML or JSON) into the model; null when it can't be read at all.</summary>
        public static ApiModel? Read(string text, string path, string apiName = "Api", string ns = "Api")
        {
            var document = SpecReader.Read(text, path, out _);
            if (document is null)
            {
                return null;
            }

            var emitter = new Emitter(document, ns, apiName, Location.None, describesItself: false, hasFakes: false, hasRules: false, ownProblems: false);
            return new ApiModel(emitter.Model(), emitter.Diagnostics.Select(d => $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}").ToList());
        }
    }

    /// <summary>An operation as the model decides it.</summary>
    public sealed record ModelOperation(string Id, string Method, string Path, string Tag, IReadOnlyList<ModelParameter> Parameters, ModelBody? Body,
        IReadOnlyList<ModelOutcome> Outcomes);

    /// <summary>A parameter: its name in the spec, where it's bound from (FromQuery…), and its C# type.</summary>
    public sealed record ModelParameter(string Name, string Binding, string Type);

    /// <summary>A request body: its C# type and media type, whether it's required, and whether it's validated as a model.</summary>
    public sealed record ModelBody(string Type, string MediaType, bool Required, bool Validated);

    /// <summary>An outcome: its status, the C# type of its body (null for none), and whether it's a success or the validation problem.</summary>
    public sealed record ModelOutcome(int Status, string? Type, bool Success, bool Validation);
}
