using System;
using System.Text;

namespace Resultful
{
    public struct Ok<T>
    {
        public Ok(T value)
        {
            Value = value;
        }

        public T Value { get;  }

        public Result<T, TError> Result<TError>()
            => this;

        public Result<T> Result()
            => this;
    }

    public struct Ok
    {
        internal Ok(Unit value)
        {
            Value = value;
        }

        public Unit Value { get; }

        public VoidResult<TError> Result<TError>()
            => this;

        public VoidResult Result()
            => this;
    }
}
