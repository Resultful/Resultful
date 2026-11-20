// Quality (13b): adding an error case changes Match's signature, a binary break for callers in other assemblies.
// The consumer is compiled against lib/ (one leaf) and run against lib.v2/ (two leaves): MissingMethodException.
// The API diff reports it as breaking, and the generator docs say so (25).
// spec: uses generator
using Resultful.Spec.Support;

public static class Cases
{
    static int Status(int id) => Library.Place(id).Match((Order o) => 201, (OrderNotFound n) => 404);

    [SpecCase]
    public static void A_new_case_breaks_compiled_callers()
    {
        Check.Throws<System.MissingMethodException>(() => Status(1), "compiled against the old Match");
    }
}
