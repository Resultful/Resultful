using System.Collections.Generic;
using System.Linq;

namespace Matchmark.Generator
{
    /// <summary>The model's read-only view (ApiModel): the decisions every output is a function of.</summary>
    internal sealed partial class Emitter
    {
        internal IReadOnlyList<ModelOperation> Model()
        {
            CollectSchemas();
            return CollectOperations().Select(o => new ModelOperation(o.Id, o.Method, o.Path, o.Tag,
                o.Parameters.Select(p => new ModelParameter(p.Name, p.Attribute.Trim('[', ']'), p.Type)).ToList(),
                o.Body is { } body ? new ModelBody(body.Type, body.MediaType, o.RequiredBody, body.Validated) : null,
                o.Successes.Select(r => new ModelOutcome(r.Status, r.BodyType, true, false))
                    .Concat(o.Errors.Select(r => new ModelOutcome(r.Status, r.BodyType, false, r.IsValidation))).ToList())).ToList();
        }
    }
}
