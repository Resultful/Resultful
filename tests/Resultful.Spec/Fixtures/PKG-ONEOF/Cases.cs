// Decision 1 (11): Resultful.OneOf carries ToOneOf (and the Task forms), FromOneOf and the None conversions.
// spec: uses Resultful.OneOf
using System.Threading.Tasks;
using OneOf;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static async Task Interop_round_trips()
    {
        OneOf<int, string> one = Result.Ok<int, string>(5).ToOneOf();
        Check.True(one.IsT0 && one.AsT0 == 5, "ToOneOf");
        Check.Equal(5, one.FromOneOf().Value, "FromOneOf");
        OneOf<int, Error> general = await Task.FromResult(Result.Fail<int>("x")).ToOneOf();
        Check.True(general.IsT1 && general.AsT1.Message == "x", "Task form, Result<T>");
        Check.Equal(None.Value, new OneOf.Types.None().ToResultful(), "None in");
        Check.True(Option<int>.None.ToOneOf().IsT1, "Option");
    }
}
