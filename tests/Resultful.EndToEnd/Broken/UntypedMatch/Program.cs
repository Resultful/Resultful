using Resultful.Shop.Domain;

namespace Resultful.EndToEnd.Broken;

// Handlers that ignore their argument: if the generated Match's parameters were reordered, these would swap silently.
public static class UntypedMatch
{
    public static int Status(OrderError error) => error.Match(_ => 409, _ => 404);
}
