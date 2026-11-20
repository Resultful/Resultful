using System.Globalization;
using static Resultful.AspNetCore.Tests.Http;

namespace Resultful.AspNetCore.Tests;

public sealed record InvalidId(int Id) : Error($"ID {Id} is invalid");

// Nothing this package writes depends on the current culture: keys, titles and statuses are the same under tr-TR,
// where "I".ToLower() is "ı". The whole suite also runs under tr-TR (eng/run-tests.cs, TestCultures).
public class CultureTests
{
    static async Task<string[]> Bodies()
    {
        var v = Validation.Fail<int>(new Error[] { new InvalidId(1), new InvalidId(2) });
        return
        [
            (await Execute(v.ToHttpResult())).Text,
            (await Execute(Result.Fail<int>(new InvalidId(3)).ToHttpResult(), configure: o => o.Map<InvalidId>(400))).Text,
            (await Execute(Result.Fail<int>(new InvalidId(4)).ToHttpResult())).Text,
            (await Execute(Result.Ok(new Order(1, "İstanbul")).ToHttpResult())).Text,
        ];
    }

    [Fact]
    public async Task Bodies_are_the_same_under_tr_TR_and_invariant()
    {
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var invariant = await Bodies();
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkish = await Bodies();
            Assert.Equal(invariant, turkish);
            Assert.Contains("\"InvalidId\"", turkish[0], StringComparison.Ordinal);
            Assert.Contains("ID 3 is invalid", turkish[1], StringComparison.Ordinal);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }
}
