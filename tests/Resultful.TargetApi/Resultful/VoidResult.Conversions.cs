#nullable enable
using System;
namespace Resultful
{
    // M2 (10): VoidResult converts to Result<Unit>, so code can move off it one call at a time.
    public readonly partial struct VoidResult
    {
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static implicit operator Result<Unit>(VoidResult value) => throw null!;
    }

    public readonly partial struct VoidResult<TError>
    {
        [Obsolete(ObsoleteMessages.VoidResult)]
        public static implicit operator Result<Unit, TError>(VoidResult<TError> value) => throw null!;
    }
}
