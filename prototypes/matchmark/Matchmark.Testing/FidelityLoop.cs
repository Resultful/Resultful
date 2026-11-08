using System.Text;

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
