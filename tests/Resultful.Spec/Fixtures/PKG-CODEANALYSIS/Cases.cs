// Decisions 15 and 16 (07): the attributes live in the core, in Resultful.CodeAnalysis, and the core's types carry them.
using System.Linq;
using Resultful;
using Resultful.CodeAnalysis;
using Resultful.Spec.Support;

public static class Cases
{
    static bool Has<TAttribute>(System.Type t) => t.GetCustomAttributes(false).OfType<TAttribute>().Any();

    [SpecCase]
    public static void The_attributes_are_in_the_core_and_on_its_types()
    {
        Check.Equal(typeof(Unit).Assembly, typeof(MustObserveAttribute).Assembly, "same assembly");
        Check.Equal("Resultful.CodeAnalysis.ClosedHierarchyAttribute", typeof(ClosedHierarchyAttribute).FullName, "full name");
        Check.True(Has<MustObserveAttribute>(typeof(Result<int>)) && Has<MustObserveAttribute>(typeof(Result<int, string>)), "results");
        Check.True(Has<MustObserveAttribute>(typeof(Option<int>)), "Option");
        var shape = new ResultShapeAttribute(typeof(Error)) { Success = typeof(int), ManyErrors = true };
        Check.True(shape.ErrorType == typeof(Error) && shape.Success == typeof(int) && shape.ManyErrors, "ResultShapeAttribute");
    }
}
