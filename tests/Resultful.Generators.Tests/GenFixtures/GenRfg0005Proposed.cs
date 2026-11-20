// GEN-DIAG (13a): RFG0005, an attributed declaration nested in a type that isn't partial. Reported on the enclosing type.
// spec: uses generator
// Proposed fix to 04c's GEN-RFG0005 fixture: NestedError in the non-partial Outer declares its own constructor. As written
// (no constructor) the declaration is CS1729 on the marked line whatever the generator does, since it can't reopen
// Outer to add one; reported to the orchestrator as a spec finding.
using Resultful;
using Resultful.CodeAnalysis;

public class Outer { [ClosedHierarchy] public abstract partial record NestedError : Error { protected NestedError(string m) : base(m) { } } } // expect: RFG0005
public partial class Fine { [ClosedHierarchy] public abstract partial record NestedError : Error; }
