using System.Text.Json.Nodes;
using Matchmark.Testing;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The Ledger twin's owned behaviour (GAPS.md, gap 3): the one file the writer in the fidelity loop edits, from the
/// training tasks alone. AgentLoopTests.The_owned_behaviour_passes_the_gate judges it on the held-out sequences.
/// </summary>
internal static class LedgerOwned
{
    public static void Apply(SpecTwin twin)
    {
        // An account opens with its opening as its balance; the opening itself isn't kept.
        twin.On("openAccount", call => call.Body is JsonObject body ? TwinAnswer.Continue(Normalised(body)) : null);

        // Accounts stored before (seeded with an opening) read as their balance: each one, replaced as a declared effect.
        twin.On("getAccount", call =>
        {
            var effects = (call.Resources.GetValueOrDefault("/accounts") ?? [])
                .Where(a => a["opening"] is not null)
                .Select(a => TwinEffect.Replace("/accounts", a["id"]!.ToString(), Normalised(a)))
                .ToArray();
            return effects.Length > 0 ? TwinAnswer.Continue(effects: effects) : null;
        });

        // A 415 has no body.
        twin.OnRejected(rejection => rejection.Status == 415 ? new TwinAnswer(415) : null);
    }

    private static JsonObject Normalised(JsonObject account)
    {
        var copy = (JsonObject)account.DeepClone();
        if (copy["opening"] is { } opening)
        {
            copy["balance"] = opening.DeepClone();
            copy.Remove("opening");
        }

        return copy;
    }
}
