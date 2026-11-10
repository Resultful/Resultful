using System.Collections.Generic;
using System.Linq;

namespace Matchmark.Generator
{
    /// <summary>
    /// Plain outcomes (decision Q12, the default): the contract carries its own result and union types, so it depends on
    /// neither Resultful nor OneOf. They keep those libraries' member names (Ok, Fail, Match, Switch, Value), so code written
    /// against either reads the same; MatchmarkOutcomes="Resultful" generates onto Resultful and OneOf instead, the adapter.
    /// </summary>
    internal sealed partial class Emitter
    {
        /// <summary>Whether the contract uses its own outcome types (Plain, the default) rather than Resultful and OneOf.</summary>
        public bool PlainOutcomes { get; set; } = true;

        /// <summary>Where Result, Unit and VoidResult come from: the contract's own namespace, or Resultful.</summary>
        private string Outcomes => PlainOutcomes ? "global::" + (OutcomesNamespace ?? _namespace) : "global::Resultful";

        /// <summary>Where a plain contract's outcome types are, when it isn't this emission's namespace (scaffolded owned code).</summary>
        public string? OutcomesNamespace { get; set; }

        /// <summary>The usings that bring the outcome types in: none for plain ones, which are in the contract's namespace.</summary>
        private static readonly string[] ResultfulUsing = { "Resultful" };

        private static readonly string[] OneOfUsing = { "OneOf" };

        private string[] OutcomeUsings(params string[] usings) => PlainOutcomes ? usings : usings.Concat(ResultfulUsing).ToArray();

        private string? EmitOutcomes()
        {
            if (!PlainOutcomes || _shared)
            {
                return null;
            }

            var w = new CodeWriter(_namespace);
            w.Usings("System", "System.Threading.Tasks");
            w.Block(@"/// <summary>The value of an outcome with no body.</summary>
public readonly struct Unit : IEquatable<Unit>
{
    public static readonly Unit Value = default;

    public bool Equals(Unit other) => true;

    public override bool Equals(object? obj) => obj is Unit;

    public override int GetHashCode() => 0;

    public override string ToString() => ""()"";
}

/// <summary>An operation's outcome: one of its successes (<typeparamref name=""T""/>) or one of its errors (<typeparamref name=""TError""/>).</summary>
public readonly struct Result<T, TError>
{
    private readonly T _value;
    private readonly TError _error;

    private Result(T value, TError error, bool ok)
    {
        _value = value;
        _error = error;
        IsOk = ok;
    }

    /// <summary>Whether it's a success.</summary>
    public bool IsOk { get; }

    public static Result<T, TError> Ok(T value) => new(value, default!, true);

    public static Result<T, TError> Fail(TError error) => new(default!, error, false);

    public static implicit operator Result<T, TError>(T value) => Ok(value);

    public static implicit operator Result<T, TError>(TError error) => Fail(error);

    public TResult Match<TResult>(Func<T, TResult> ok, Func<TError, TResult> fail) => IsOk ? ok(_value) : fail(_error);

    public void Switch(Action<T> ok, Action<TError> fail)
    {
        if (IsOk)
        {
            ok(_value);
        }
        else
        {
            fail(_error);
        }
    }

    public Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> ok, Func<TError, Task<TResult>> fail) => IsOk ? ok(_value) : fail(_error);

    public Result<TResult, TError> Map<TResult>(Func<T, TResult> map) => IsOk ? Result<TResult, TError>.Ok(map(_value)) : Result<TResult, TError>.Fail(_error);

    public Result<T, TOther> MapError<TOther>(Func<TError, TOther> map) => IsOk ? Result<T, TOther>.Ok(_value) : Result<T, TOther>.Fail(map(_error));

    public Result<TResult, TError> Bind<TResult>(Func<T, Result<TResult, TError>> bind) => IsOk ? bind(_value) : Result<TResult, TError>.Fail(_error);

    public override string ToString() => IsOk ? $""Ok({_value})"" : $""Fail({_error})"";
}

/// <summary>An operation's outcome when its success has no body: done, or one of its errors.</summary>
public readonly struct VoidResult<TError>
{
    private readonly TError _error;

    private VoidResult(TError error, bool ok)
    {
        _error = error;
        IsOk = ok;
    }

    public bool IsOk { get; }

    public static VoidResult<TError> Ok() => new(default!, true);

    public static VoidResult<TError> Fail(TError error) => new(error, false);

    public static implicit operator VoidResult<TError>(TError error) => Fail(error);

    public static implicit operator VoidResult<TError>(Unit value) => Ok();

    public TResult Match<TResult>(Func<Unit, TResult> ok, Func<TError, TResult> fail) => IsOk ? ok(default) : fail(_error);

    public void Switch(Action<Unit> ok, Action<TError> fail)
    {
        if (IsOk)
        {
            ok(default);
        }
        else
        {
            fail(_error);
        }
    }

    public Task<TResult> MatchAsync<TResult>(Func<Unit, Task<TResult>> ok, Func<TError, Task<TResult>> fail) => IsOk ? ok(default) : fail(_error);

    public override string ToString() => IsOk ? ""Ok"" : $""Fail({_error})"";
}

/// <summary>Makes outcomes: <c>Result.Ok&lt;Pet, GetPetError&gt;(pet)</c>, <c>Result.Fail&lt;Pet, GetPetError&gt;(GetPetError.NotFound())</c>.</summary>
public static class Result
{
    public static Result<T, TError> Ok<T, TError>(T value) => Result<T, TError>.Ok(value);

    public static Result<T, TError> Fail<T, TError>(TError error) => Result<T, TError>.Fail(error);

    public static VoidResult<TError> Ok<TError>() => VoidResult<TError>.Ok();

    public static VoidResult<TError> Fail<TError>(TError error) => VoidResult<TError>.Fail(error);
}");
            return w.ToString();
        }

        /// <summary>A union of outcome cases as a plain class: exactly one case, with Match and Switch over them in order.</summary>
        private static void EmitPlainUnion(CodeWriter w, string unionName, List<string> cases)
        {
            w.Open($"public sealed class {unionName}");
            w.Line($"private {unionName}(object value, int index)");
            w.Line("{");
            w.Line("    Value = value;");
            w.Line("    Index = index;");
            w.Line("}");
            w.Line();
            w.Line("/// <summary>The case, as an object.</summary>");
            w.Line("public object Value { get; }");
            w.Line();
            w.Line("/// <summary>Which case it is, in the order the spec lists them.</summary>");
            w.Line("public int Index { get; }");
            w.Line();
            // Each case converts to the union (with its index), and has a factory, as EmitUnion writes for both modes.
            w.Line($"private static int IndexOf(object value) => value switch {{ {string.Join(", ", cases.Select((c, i) => $"{c} => {i}"))}, _ => -1 }};");
            w.Line();
            w.Line($"private {unionName}(object value) : this(value, IndexOf(value)) {{ }}");
            w.Line();
            var funcs = string.Join(", ", cases.Select((c, i) => $"global::System.Func<{c}, TResult> f{i}"));
            w.Open($"public TResult Match<TResult>({funcs})");
            w.Line("return Index switch");
            w.Line("{");
            for (var i = 0; i < cases.Count; i++)
            {
                w.Line($"    {i} => f{i}(({cases[i]})Value),");
            }

            w.Line("    _ => throw new global::System.InvalidOperationException(\"No case.\"),");
            w.Line("};");
            w.Close();
            w.Line();
            var actions = string.Join(", ", cases.Select((c, i) => $"global::System.Action<{c}> f{i}"));
            w.Open($"public void Switch({actions})");
            w.Line("switch (Index)");
            w.Line("{");
            for (var i = 0; i < cases.Count; i++)
            {
                w.Line($"    case {i}: f{i}(({cases[i]})Value); break;");
            }

            w.Line("}");
            w.Close();
            w.Line();
            w.Line("public override string ToString() => Value.ToString() ?? string.Empty;");
        }
    }
}
