// Migration (10): gathering with Result<T>.Plus moves to Validation and & (or Combine); Result<T> has no Plus.
using System.Linq;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void Result_of_T_has_no_Plus()
    {
        var plus = typeof(Result).GetMethods().Where(m => m.Name == "Plus")
            .Where(m => m.GetParameters().FirstOrDefault()?.ParameterType is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(Result<>)).ToList();
        Check.True(plus.Count == 0, "Plus on Result<T>: " + string.Join(", ", plus));
    }

    [SpecCase]
    public static void The_replacement_gathers()
    {
        Validation<int> a = Error.From("a");
        Validation<int> b = Error.From("b");
        Check.Equal(2, Validation.Combine(a, b).Errors.Count, "both errors");
    }
}
