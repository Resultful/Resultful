#nullable enable
namespace Resultful
{
    internal static class ObsoleteMessages
    {
        public const string VoidResult = "Use Result<Unit> (or Result<Unit, TError>); return Unit.Value for success. VoidResult is removed at the 1.0 review.";
        public const string Default = "A result has no default value: use Result.Ok or Result.Fail.";
    }
}
