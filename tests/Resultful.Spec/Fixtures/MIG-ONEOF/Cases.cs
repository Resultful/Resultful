// Migration (11): ToOneOf leaves the core; without Resultful.OneOf it doesn't exist.
using Resultful;

public static class Cases
{
    static object Old(Result<int, string> r) => r.ToOneOf(); // expect: CS1061
    static int New(Result<int, string> r) => r.ValueOr(0);
}
