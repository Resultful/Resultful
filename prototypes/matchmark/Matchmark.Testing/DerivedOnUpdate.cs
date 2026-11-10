using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>
/// Derived fields on update (x-matchmark-derived with on: update or always, docs/reference/invariants.md#derived): the
/// resource is read before a change, so the expression has oldSelf, and the change's response is checked against it.
/// </summary>
internal sealed partial class RegressionRunner
{
    /// <summary>The resource before a PUT or PATCH whose schema derives a field on update; null when there's nothing to check.</summary>
    private async Task<JsonObject?> DerivedBeforeAsync(RegressionCase @case, CancellationToken cancellationToken)
    {
        var operation = @case.Operation;
        if (operation.Method is not ("PUT" or "PATCH") || options.Skip.Contains(Checks.DerivedHolds)
            || @case.Kind is not (CaseKind.Valid or CaseKind.Example or CaseKind.Boundary)
            || !Traits.DerivedFields(suite.Spec, Traits.ResourceSchema(operation)).Any(d => d.OnUpdate)
            || suite.Spec.Operations.FirstOrDefault(o => o.Method == "GET" && o.Path == operation.Path) is null)
        {
            return null;
        }

        var read = await SendAsync(new RegressionRequest("GET", @case.Request.Path, [], @case.Request.Headers.Where(h => !string.Equals(h.Key, "If-Match", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(h.Key, "Idempotency-Key", StringComparison.OrdinalIgnoreCase)).ToList(), null, null), cancellationToken).ConfigureAwait(false);
        return read.Status == 200 ? read.Json() as JsonObject : null;
    }

    /// <summary>After a successful change: each field derived on update equals its expression over the request, the response and the resource before.</summary>
    private void DerivedAfter(RegressionCase @case, Exchange exchange, JsonObject before, List<Finding> findings)
    {
        if (exchange.Status is < 200 or >= 300 || exchange.Json() is not JsonObject changed)
        {
            return;
        }

        foreach (var derived in Traits.DerivedFields(suite.Spec, Traits.ResourceSchema(@case.Operation)).Where(d => d.OnUpdate))
        {
            var self = (JsonObject)changed.DeepClone();
            self.Remove(derived.Field);
            if (Traits.Evaluate(derived, RequestJson(@case.Request, @case.Operation), self, before) is { } expected && !JsonNode.DeepEquals(expected, changed[derived.Field]))
            {
                Add(findings, Checks.DerivedHolds, $"{@case.Name}, {derived.Field} derived on update", exchange,
                    $"{derived.Field} is {changed[derived.Field]?.ToJsonString() ?? "missing"}; the spec derives it as {derived.Expression}, which gives {expected.ToJsonString()}");
            }
        }
    }
}
