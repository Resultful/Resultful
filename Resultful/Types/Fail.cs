using System.Collections.Generic;

namespace Resultful
{
    public struct Fail
    {
        internal Fail(IEnumerable<string> value)
        {
            Value = value;
        }


        public static implicit operator Fail(string[] value)
            => value.Fail();

        public static implicit operator Fail(List<string> value)
            => value.Fail();

        public static implicit operator Fail(string value)
            => value.Fail();

        public IEnumerable<string> Value { get; }

        public Result<T> Result<T>()
            => this;

        public VoidResult Result()
            => this;
    }

    public struct Fail<TError>
    {
        internal Fail(TError value)
        {
            Value = value;
        }

        public TError Value { get; }

        public static implicit operator Fail<TError>(TError value)
            => value.Err();

        public Result<T, TError> Result<T>()
            => this;

        public VoidResult<TError> Result()
            => this;
    }
}
