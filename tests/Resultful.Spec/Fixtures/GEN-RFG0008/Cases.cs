// GEN-DIAG (13b, round 4): RFG0008, two leaves of one hierarchy share a simple name, so the JSON "type" would be
// ambiguous. Reported on the [ClosedHierarchy] base. (Supersedes the prefixing rule in building-the-generator.md.)
// spec: uses generator
using Resultful;
using Resultful.CodeAnalysis;

[ClosedHierarchy] public abstract partial record AppError : Error; // expect: RFG0008
namespace Orders { public sealed record NotFound() : global::AppError("order"); }
namespace Payments { public sealed record NotFound() : global::AppError("payment"); }
