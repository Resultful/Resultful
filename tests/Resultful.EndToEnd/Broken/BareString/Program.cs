using Resultful;

namespace Resultful.EndToEnd.Broken;

// Use case 10's dangerous case: in 0.2 this was a failure; in 0.3 it compiles and means success.
public static class BareString
{
    public static Result<string> Label(int id) => id > 0 ? Result.Ok("a label") : "no label for this id";
}
