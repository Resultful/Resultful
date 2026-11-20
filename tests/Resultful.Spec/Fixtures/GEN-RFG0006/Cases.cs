// GEN-DIAG (13a): RFG0006, the attribute on a declaration the generator can't extend.
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy] public abstract partial record OrderError : Error;
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");
[ResultShape(typeof(OrderError), Success = typeof(int))] public readonly struct NotPartial; // expect: RFG0006
[ResultShape(typeof(OrderError), Success = typeof(int))] public partial struct NotReadonly; // expect: RFG0006
[ClosedHierarchy] public abstract record NotPartialBase : Error { protected NotPartialBase(string m) : base(m) { } } // expect: RFG0006
[ClosedHierarchy] public sealed partial record NotAbstract() : Error("x"); // expect: RFG0006
