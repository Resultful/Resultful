using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace Matchmark.Sample.Tests;

internal static class AssertionSetup
{
    /// <summary>
    /// Compare members by their runtime type. By default a <c>PetDetails</c> member is compared as <c>PetDetails</c>,
    /// which has no members, so a <c>Dog</c> with a different <c>GoodBoy</c> would pass BeEquivalentTo silently.
    /// </summary>
    [ModuleInitializer]
    internal static void CompareRuntimeTypes()
        => AssertionConfiguration.Current.Equivalency.Modify(options => options.PreferringRuntimeMemberTypes());
}
