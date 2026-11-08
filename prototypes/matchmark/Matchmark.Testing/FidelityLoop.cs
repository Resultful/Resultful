using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Matchmark.Testing;

/// <summary>
/// The loop that grows a twin's behaviour (GAPS.md, gap 3), without the writer in it: whoever writes the behaviour (a
/// person or an agent) sees the tasks of one part of the recording, and a change is judged on the other part, which
/// they never see. A sequence (a case, or a whole workflow) is in one part or the other, by a stable hash of its name,
/// so the split is the same on every run and every machine.
/// </summary>
public static class FidelityLoop
{
    /// <summary>The recording split into what the writer sees and what a change is judged on.</summary>
    public static (FidelityRecording Training, FidelityRecording HeldOut) Split(FidelityRecording real, int heldOutPercent = 30)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(heldOutPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(heldOutPercent, 100);
        var heldOut = real.Steps.Select(Sequence).Distinct(StringComparer.Ordinal).Where(s => Hash(s) % 100 < heldOutPercent).ToHashSet(StringComparer.Ordinal);
        return (new FidelityRecording(real.Steps.Where(s => !heldOut.Contains(Sequence(s))).ToList()),
                new FidelityRecording(real.Steps.Where(s => heldOut.Contains(Sequence(s))).ToList()));
    }

    /// <summary>
    /// Whether a change to the twin is kept: only if its score on the held-out part rises and no operation's score
    /// falls. <paramref name="before"/> and <paramref name="after"/> are both measured against the held-out part.
    /// </summary>
    public static FidelityVerdict Judge(FidelityReport before, FidelityReport after)
    {
        var reasons = new List<string>();
        var now = after.Operations.ToDictionary(o => o.Operation, StringComparer.Ordinal);
        foreach (var operation in before.Operations)
        {
            var score = now.GetValueOrDefault(operation.Operation)?.Score ?? 0;
            if (score < operation.Score)
            {
                reasons.Add($"{operation.Operation} fell from {operation.Score * 100:0.#}% to {score * 100:0.#}%");
            }
        }

        if (after.Score <= before.Score)
        {
            reasons.Add($"the held-out score didn't rise ({before.Score * 100:0.#}% before, {after.Score * 100:0.#}% after)");
        }

        return new FidelityVerdict(reasons.Count == 0, reasons.Count == 0
            ? [$"the held-out score rose from {before.Score * 100:0.#}% to {after.Score * 100:0.#}%, and no operation fell"]
            : reasons);
    }

    /// <summary>
    /// Unique keys the mismatches suggest (docs/reference/invariants.md#unique), for a person to review and declare: a
    /// create the twin accepted and the real API refused with 409, whose body repeats a value an earlier create in the run
    /// sent. Proposing a declaration rather than code keeps it reviewable, and it tests the real service too.
    /// </summary>
    public static IReadOnlyList<UniqueProposal> ProposeUnique(RegressionReport twin, FidelityReport fidelity)
    {
        var steps = RegressionReport.Steps(twin).ToList();
        var proposals = new List<UniqueProposal>();
        foreach (var task in fidelity.Tasks.Where(t => t.Status is >= 200 and < 300 && t.RealStatus == 409))
        {
            var index = steps.FindIndex(s => s.Subject == task.Subject);
            if (index < 0 || steps[index].Exchange.Request is not { Method: "POST" } request || Json(request.Body) is not JsonObject body)
            {
                continue;
            }

            var earlier = steps.Take(index).Select(s => s.Exchange.Request)
                .Where(r => r.Method == "POST" && r.Path == request.Path).Select(r => Json(r.Body)).OfType<JsonObject>().ToList();
            foreach (var (field, value) in body)
            {
                if (value is not JsonValue repeated || repeated.GetValueKind() != JsonValueKind.String)
                {
                    continue;
                }

                var text = repeated.GetValue<string>();
                var same = earlier.Any(e => e[field] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == text);
                var otherCase = !same && earlier.Any(e => e[field] is JsonValue v && v.GetValueKind() == JsonValueKind.String
                                                          && string.Equals(v.GetValue<string>(), text, StringComparison.OrdinalIgnoreCase));
                var path = "/" + request.Path.TrimStart('/');
                if ((same || otherCase) && proposals.All(p => p.Path != path || p.Field != field))
                {
                    proposals.Add(new UniqueProposal(path, field, otherCase, task.Subject));
                }
            }
        }

        return proposals;
    }

    private static JsonNode? Json(string? text)
    {
        try
        {
            return text is null ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A step's sequence: its workflow's name for a workflow step, else the case itself.</summary>
    private static string Sequence(RecordedStep step)
        => step.Subject.StartsWith(step.Operation + ", step ", StringComparison.Ordinal) ? step.Operation : step.Subject;

    /// <summary>FNV-1a over the name's UTF-8: stable across runs and runtimes, unlike string.GetHashCode.</summary>
    private static uint Hash(string text)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash;
    }
}

/// <summary>Whether a change is kept, and why.</summary>
public sealed record FidelityVerdict(bool Keep, IReadOnlyList<string> Reasons)
{
    public override string ToString() => $"{(Keep ? "Keep" : "Reject")}: {string.Join("; ", Reasons)}.";
}

/// <summary>
/// One mismatch as a task for whoever writes the twin's behaviour: the step, what the twin answered (no status when it
/// never reached it) and what the real API did, and where the two first differ.
/// </summary>
public sealed record FidelityTask(string Operation, string Subject, int? Status, string? Body, int RealStatus, string RealBody, string Difference);

/// <summary>
/// A unique key the mismatches suggest: the collection a create posted to, the field whose value repeated, whether it
/// repeated only in another case (so the key ignores case), and the step that showed it.
/// </summary>
public sealed record UniqueProposal(string Path, string Field, bool IgnoreCase, string Evidence)
{
    public override string ToString() => $"{Path}: {Field} looks unique{(IgnoreCase ? ", ignoring case" : string.Empty)} (the real API refused a repeat: {Evidence})";
}
