// Quality (19): Result.Try is the opt-in way to turn an exception into a failure.
using System.Threading.Tasks;
using Resultful;
using Resultful.Spec.Support;

public sealed record Crashed(string What) : Error($"crashed: {What}");

public static class Cases
{
    [SpecCase]
    public static void Try_turns_an_exception_into_the_error_you_choose()
    {
        var r = Result.Try<int>(() => throw new System.FormatException("bad"), ex => new Crashed(ex.Message));
        Check.True(r.TryGetError(out var e) && e is Crashed { What: "bad" }, "mapped");
        Check.Equal(4, Result.Try(() => 4, ex => new Crashed(ex.Message)).Value, "success");
    }

    [SpecCase]
    public static async Task TryAsync_does_the_same_for_a_task()
    {
        var r = await Result.TryAsync<int>(async () => { await Task.Yield(); throw new System.IO.IOException("disk"); }, ex => new Crashed(ex.Message));
        Check.True(r.TryGetError(out var e) && e is Crashed { What: "disk" }, "mapped");
    }
}
