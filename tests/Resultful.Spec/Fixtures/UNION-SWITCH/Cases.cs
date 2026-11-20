// M6, decision 3 (24, net11.0): the core result types are unions on .NET 11, so a switch is exhaustive with no `_`.
// The members that make it so are chosen by measurement in 24 and added to the target then (`uses core-union`).
// spec: tfm net11.0
// spec: uses core-union
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    static string Describe(Result<int, System.TimeSpan> r) => r switch { int v => "ok " + v, System.TimeSpan t => "late " + t.TotalSeconds };
    static string Missing(Result<int, System.TimeSpan> r) => r switch { int v => "ok" }; // expect: CS8509

    [SpecCase]
    public static void A_core_result_switches_flat()
    {
        Check.Equal("ok 3", Describe(3), "success");
        Check.Equal("late 2", Describe(System.TimeSpan.FromSeconds(2)), "failure");
        Check.Throws<System.Runtime.CompilerServices.SwitchExpressionException>(() => Describe(default), "the zero value matches no arm");
    }
}
