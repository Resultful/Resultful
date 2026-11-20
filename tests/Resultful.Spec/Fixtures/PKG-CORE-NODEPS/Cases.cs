// Decision 1 (11): the core references nothing outside the framework.
using System.Linq;
using Resultful;
using Resultful.Spec.Support;

public static class Cases
{
    [SpecCase]
    public static void The_core_assembly_references_only_the_framework()
    {
        var refs = typeof(Unit).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        var foreign = refs.Where(n => n is not ("netstandard" or "mscorlib") && !n.StartsWith("System", System.StringComparison.Ordinal)).ToList();
        Check.True(foreign.Count == 0, "foreign references: " + string.Join(", ", foreign));
        Check.True(typeof(None).Assembly == typeof(Unit).Assembly, "None is the core's own");
    }
}
