// GEN-DIAG (13a): RFG0005, an attributed declaration nested in a type that isn't partial. Reported on the enclosing type.
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;

public class Outer { [ClosedHierarchy] public abstract partial record NestedError : Error; } // expect: RFG0005
public partial class Fine { [ClosedHierarchy] public abstract partial record NestedError : Error; }
