namespace Resultful.Consumption;

// A reference model of today's (0.2) results: a bool and either a value or the errors, in plain lists.
// Nothing here uses Resultful, so each operation can be read and checked by eye. ReferenceModelLawTests runs
// the same operations on the model and on the library and requires the same outcome.
//
// The model copies 0.2 as shipped, hazards included: a failure may carry no errors, and default(Result<T>)
// is a success holding default(T).

/// <summary>Model of <c>Result&lt;T&gt;</c>: a success with a value, or a failure with a list of strings.</summary>
internal sealed record ModelResult<T>(bool IsOk, T Value, IReadOnlyList<string> Errors)
{
    public static ModelResult<T> Ok(T value) => new(true, value, []);

    public static ModelResult<T> Fail(IEnumerable<string> errors) => new(false, default!, errors.ToList());

    public ModelResult<TResult> Map<TResult>(Func<T, TResult> f) => IsOk ? ModelResult<TResult>.Ok(f(Value)) : ModelResult<TResult>.Fail(Errors);

    public ModelResult<TResult> Bind<TResult>(Func<T, ModelResult<TResult>> f) => IsOk ? f(Value) : ModelResult<TResult>.Fail(Errors);

    public TResult Match<TResult>(Func<T, TResult> ok, Func<IReadOnlyList<string>, TResult> error) => IsOk ? ok(Value) : error(Errors);

    public ModelResult<T, TError> MapError<TError>(Func<IReadOnlyList<string>, TError> f) => IsOk ? ModelResult<T, TError>.Ok(Value) : ModelResult<T, TError>.Fail(f(Errors));

    /// <summary>Both succeed: combine the values. Otherwise: the left errors, then the right errors.</summary>
    public ModelResult<TResult> Plus<TRight, TResult>(ModelResult<TRight> right, Func<T, TRight, TResult> f) =>
        IsOk && right.IsOk ? ModelResult<TResult>.Ok(f(Value, right.Value)) : ModelResult<TResult>.Fail(Errors.Concat(right.Errors));

    public override string ToString() => IsOk ? $"Ok({Value})" : $"Fail[{string.Join(" | ", Errors)}]";
}

/// <summary>Model of <c>Result&lt;T, TError&gt;</c>: a success with a value, or a failure with one error.</summary>
internal sealed record ModelResult<T, TError>(bool IsOk, T Value, TError Error)
{
    public static ModelResult<T, TError> Ok(T value) => new(true, value, default!);

    public static ModelResult<T, TError> Fail(TError error) => new(false, default!, error);

    public ModelResult<TResult, TError> Map<TResult>(Func<T, TResult> f) => IsOk ? ModelResult<TResult, TError>.Ok(f(Value)) : ModelResult<TResult, TError>.Fail(Error);

    public ModelResult<TResult, TError> Bind<TResult>(Func<T, ModelResult<TResult, TError>> f) => IsOk ? f(Value) : ModelResult<TResult, TError>.Fail(Error);

    public TResult Match<TResult>(Func<T, TResult> ok, Func<TError, TResult> error) => IsOk ? ok(Value) : error(Error);

    public ModelResult<T, TNewError> MapError<TNewError>(Func<TError, TNewError> f) => IsOk ? ModelResult<T, TNewError>.Ok(Value) : ModelResult<T, TNewError>.Fail(f(Error));

    /// <summary>Both succeed: combine the values. One fails: its error. Both fail: merge(left, right).</summary>
    public ModelResult<TResult, TError> Plus<TRight, TResult>(ModelResult<TRight, TError> right, Func<TError, TError, TError> merge, Func<T, TRight, TResult> f) =>
        (IsOk, right.IsOk) switch
        {
            (true, true) => ModelResult<TResult, TError>.Ok(f(Value, right.Value)),
            (true, false) => ModelResult<TResult, TError>.Fail(right.Error),
            (false, true) => ModelResult<TResult, TError>.Fail(Error),
            (false, false) => ModelResult<TResult, TError>.Fail(merge(Error, right.Error)),
        };

    public override string ToString() => IsOk ? $"Ok({Value})" : $"Fail({Error})";
}

/// <summary>Model of <c>VoidResult</c>: success, or a failure with a list of strings.</summary>
internal sealed record ModelVoid(bool IsOk, IReadOnlyList<string> Errors)
{
    public static readonly ModelVoid Ok = new(true, []);

    public static ModelVoid Fail(IEnumerable<string> errors) => new(false, errors.ToList());

    public ModelVoid Bind(Func<ModelVoid> f) => IsOk ? f() : this;

    public ModelResult<T> Map<T>(T value) => IsOk ? ModelResult<T>.Ok(value) : ModelResult<T>.Fail(Errors);

    public ModelVoid Plus(ModelVoid right) => IsOk && right.IsOk ? Ok : Fail(Errors.Concat(right.Errors));

    public override string ToString() => IsOk ? "Ok" : $"Fail[{string.Join(" | ", Errors)}]";
}
