using Resultful.Shop.Domain;

namespace Resultful.EndToEnd.Broken;

// A type outside OrderError's assembly derives from it. The private protected constructor closes the ordinary
// path; the copy constructor a non-sealed record must expose doesn't (CS8878), so only RF0010 catches this.
public sealed record OrderLost : OrderError
{
    public OrderLost(OrderNotFound original) : base(original) { }
}
