// Migration (12): VoidResult is obsolete (CS0618 where it's named); Result.Ok() returns Result<Unit>.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    static VoidResult Old() => Result.Ok(); // expect: CS0618

    [SpecCase]
    public static void The_replacement_is_Result_of_Unit()
    {
        Result<Unit> u = Result.Ok();
        Check.True(u.IsSuccess, "Result<Unit>");
    }
}
