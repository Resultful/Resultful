// GEN-DIAG (13a): RFG0005, an attributed declaration nested in a type that isn't partial. Reported on the enclosing type.
// NestedError declares its own constructor: the generator can't reopen Outer to add one (CS1729 otherwise; 13a finding).
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;

public class Outer { [ClosedHierarchy] public abstract partial record NestedError : Error { protected NestedError(string m) : base(m) { } } } // expect: RFG0005
public partial class Fine { [ClosedHierarchy] public abstract partial record NestedError : Error; }
