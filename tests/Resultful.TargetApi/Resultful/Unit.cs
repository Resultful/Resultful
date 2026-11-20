#nullable enable
namespace Resultful
{
    // Decision 14 and DECISIONS-TAKEN (04c, accepted): Unit stays a class, as shipped in 0.2.
    public class Unit
    {
        private Unit() => throw null!;
        public static Unit Value => throw null!;
    }
}
