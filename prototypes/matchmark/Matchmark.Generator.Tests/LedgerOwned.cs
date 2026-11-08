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
        twin.On("openAccount", call =>
        {
            if (call.Body is JsonObject body)
            {
                Normalise(body);
            }

            return null;
        });

        // Accounts stored before (seeded with an opening) read as their balance.
        twin.On("getAccount", call =>
        {
            foreach (var account in call.Resources.GetValueOrDefault("/accounts") ?? [])
            {
                Normalise(account);
            }

            return null;
        });

        // A 415 has no body.
        twin.OnRejected(rejection => rejection.Status == 415 ? new TwinAnswer(415) : null);
    }

    private static void Normalise(JsonObject account)
    {
        if (account["opening"] is { } opening)
        {
            account["balance"] = opening.DeepClone();
            account.Remove("opening");
        }
    }
}
