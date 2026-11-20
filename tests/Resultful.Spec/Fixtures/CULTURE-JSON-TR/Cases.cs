// Quality (12b): JSON is the same under tr-TR: numbers keep a dot, names keep their i.
// spec: uses Resultful.Text.Json
using System.Globalization;
using System.Text.Json;
using Resultful;
using Resultful.Text.Json;
using Resultful.Spec.Support;

public sealed record InvalidItem(decimal Price) : Error("invalid item");

public static class Cases
{
    [SpecCase]
    public static void Json_doesnt_depend_on_the_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddResultful().AddErrorCases<Error>(new[] { typeof(InvalidItem) });
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var json = JsonSerializer.Serialize(Result.Fail<int>(new InvalidItem(1.5m)), options);
            Check.True(json.Contains("\"price\":1.5", System.StringComparison.Ordinal) && json.Contains("\"InvalidItem\"", System.StringComparison.Ordinal), json);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
