// GEN-DIAG (13a): RFG0001, a shape's Success overlaps its errors: a leaf, the base, Error or object. Reported on the shape.
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy] public abstract partial record OrderError : Error;
public sealed record OrderNotFound(int Id) : OrderError($"order {Id} was not found");

[ResultShape(typeof(OrderError), Success = typeof(OrderNotFound))] public readonly partial struct LeafAsSuccess; // expect: RFG0001
[ResultShape(typeof(OrderError), Success = typeof(OrderError))] public readonly partial struct BaseAsSuccess; // expect: RFG0001
[ResultShape(typeof(OrderError), Success = typeof(Error))] public readonly partial struct ErrorAsSuccess; // expect: RFG0001
[ResultShape(typeof(OrderError), Success = typeof(object))] public readonly partial struct ObjectAsSuccess; // expect: RFG0001
[ResultShape(typeof(OrderError), Success = typeof(int))] public readonly partial struct Fine;
