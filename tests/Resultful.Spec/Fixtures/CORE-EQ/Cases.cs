// 08b: results compare by case and content, with == and !=, and print their case.
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void Results_compare_by_case_and_content()
    {
        Check.True(Result.Ok<int, string>(1) == Result.Ok<int, string>(1), "ok == ok");
        Check.True(Result.Ok<int, string>(1) != Result.Fail<int, string>("1"), "ok != fail");
        Check.True(Result.Fail<int, string>("x") == Result.Fail<int, string>("x"), "error compared by value");
        Check.Equal(Result.Ok(2).GetHashCode(), Result.Ok(2).GetHashCode(), "hash");
    }

    [SpecCase]
    public static void ToString_names_the_case()
    {
        Check.True(Result.Ok(5).ToString().Contains('5', System.StringComparison.Ordinal), "ok: " + Result.Ok(5));
        Check.True(Result.Fail<int>("boom").ToString().Contains("boom", System.StringComparison.Ordinal), "fail: " + Result.Fail<int>("boom"));
    }
}
