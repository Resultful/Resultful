using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using static Resultful.Analyzers.Tests.AnalyzerHarness;

namespace Resultful.Analyzers.Tests;

/// <summary>RF0003 (Value unchecked), RF0004 (a bare string), RF0006 (an unassigned result field) and RF0009 (Map to Bind), against the real core.</summary>
public sealed class UsageRulesTests
{
    [Fact]
    public void Value_is_reported_unless_a_check_of_the_same_variable_guards_it() => Verify("""
        using System;
        using Resultful;

        public sealed class Orders
        {
            Result<int> _last = Result.Ok(0);
            static Result<int> Get(int id) => Result.Ok(id);

            int Cases(Result<int> r, Result<int> other, Validation<int> v)
            {
                var a = r.Value; // expect: RF0003
                var b = Get(1).Value; // expect: RF0003
                if (r.IsSuccess) a = r.Value;
                if (r.IsSuccess && other.IsSuccess) a = r.Value + other.Value;
                if (r.IsSuccess || other.IsSuccess) a = r.Value; // expect: RF0003
                if (other.IsSuccess) a = r.Value; // expect: RF0003
                if (r.IsFailure) a = 0; else a = r.Value;
                if (!r.IsFailure) a = r.Value;
                if (r.IsSuccess == true) a = r.Value;
                if (r.IsSuccess == false) a = r.Value; // expect: RF0003
                a = r.IsSuccess ? r.Value : 0;
                a = r.IsFailure ? 0 : r.Value;
                var ok = r.IsSuccess && r.Value > 0;
                var bad = r.IsFailure || r.Value > 0;
                if (r.TryGetValue(out var x)) a = x + r.Value;
                if (!r.TryGetError(out _)) a = r.Value;
                if (_last.IsSuccess) a = _last.Value;
                if (this._last.IsSuccess) a = _last.Value;
                if (v.IsSuccess) a = v.Value;
                a = v.Value; // expect: RF0003
                Func<int> later = () => r.Value; // expect: RF0003
                if (r.IsSuccess) later = () => r.Value;
                return a + b;
            }

            static int EarlyExit(Result<int> r, Result<int> other)
            {
                if (!r.IsSuccess) return 0;
                if (other.IsFailure) throw new InvalidOperationException();
                return r.Value + other.Value;
            }

            static int NoExit(Result<int> r)
            {
                if (!r.IsSuccess) Console.WriteLine("failed");
                return r.Value; // expect: RF0003
            }

            static int Loop(Result<int> r)
            {
                while (r.IsSuccess) return r.Value;
                foreach (var i in new[] { 1 })
                {
                    if (r.IsFailure) continue;
                    return r.Value;
                }

                return 0;
            }
        }
        """);

    [Fact]
    public void Patterns_switch_sections_and_top_level_statements_guard_a_read() => Verify("""
        using System;
        using Resultful;

        Result<int> r = Result.Ok(1);
        if (r.IsFailure) return;
        Console.WriteLine(r.Value);

        static class Cases
        {
            static int Patterns(Result<int> r, Result<int> other)
            {
                var a = 0;
                if (r is { IsSuccess: true }) a = r.Value;
                if (r is { IsFailure: false }) a = r.Value;
                if (r.IsSuccess is true) a = r.Value;
                if (r.IsFailure is not true) a = r.Value;
                if (r is not { IsSuccess: true }) a = r.Value; // expect: RF0003
                if (other is { IsSuccess: true }) a = r.Value; // expect: RF0003
                return a;
            }

            static int Section(Result<int> r, int kind)
            {
                switch (kind)
                {
                    case 1:
                        if (!r.IsSuccess) return 0;
                        return r.Value;
                    default:
                        return r.Value; // expect: RF0003
                }
            }
        }
        """, executable: true);

    [Fact]
    public void Many_unguarded_reads_after_many_checks_stay_linear()
    {
        var body = new StringBuilder();
        for (var i = 0; i < 2000; i++) body.Append("        if (r0.IsFailure) return 0;\n");
        for (var i = 0; i < 2000; i++) body.Append("        a += r1.Value;\n");
        var source = "using Resultful;\npublic static class Cases\n{\n    static int M(Result<int> r0, Result<int> r1)\n    {\n        var a = 0;\n"
            + body + "        return a;\n    }\n}\n";
        var compilation = Compile([source]);
        // Every read of r1 looks past 2000 checks of r0 for a guard. Building the operation tree of the method dominates; RF0002's
        // analyzer walks the same tree and is the baseline. The quadratic version took ten times the baseline; this takes
        // about 1.2 times it, so the bound (three times, relative because CI machines are slower) holds under load.
        // The best of three runs each, so a busy machine (the other target's tests run alongside) doesn't fail it.
        _ = Time(new DefaultResultAnalyzer(), 0);
        var baseline = Enumerable.Range(0, 3).Min(_ => Time(new DefaultResultAnalyzer(), 0));
        var rule = Enumerable.Range(0, 3).Min(_ => Time(new UncheckedValueAnalyzer(), 2000));
        Assert.True(rule < 3 * baseline, $"2000 checks and 2000 reads took {rule} ms, the baseline {baseline} ms");

        long Time(DiagnosticAnalyzer analyzer, int expected)
        {
            var watch = Stopwatch.StartNew();
            Assert.Equal(expected, compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult().Length);
            return watch.ElapsedMilliseconds;
        }
    }

    [Fact]
    public void Value_on_a_users_own_must_observe_type_is_checked_the_same_way() => Verify("""
        using Resultful.CodeAnalysis;

        [MustObserve]
        public readonly struct Reply
        {
            public bool IsSuccess => true;
            public int Value => 1;
            public int Twice => IsSuccess ? Value * 2 : 0;
        }

        public readonly struct Lazy { public int Value => 1; public bool IsSuccess => true; }
        [MustObserve] public readonly struct NoCheck { public int Value => 1; }

        public static class Cases
        {
            static int M(Reply r, Lazy l, NoCheck n) => r.Value + l.Value + n.Value; // expect: RF0003
        }
        """);

    [Fact]
    public void A_bare_string_into_a_result_of_string_is_reported() => Verify(""""
        using Resultful;

        public static class Cases
        {
            const string Missing = "missing";

            static Result<string> Literal() => "order 1 was not found"; // expect: RF0004
            static Result<string> Interpolated(int id) => $"order {id} was not found"; // expect: RF0004
            static Result<string> Concatenated(string id) => "order " + id; // expect: RF0004
            static Result<string> Raw() => """raw"""; // expect: RF0004
            static Result<string, Error> TwoParameters() => "x"; // expect: RF0004
            static Result<object> Object() => "x";
            static Result<string> Variable(string name) => name;
            static Result<string> Call() => Missing.ToUpperInvariant();
            static Result<string> Cast() => (Result<string>)"intended";
            static Result<string> Ok() => Result.Ok("intended");
            static Result<string> Fail() => Result.Fail<string>("intended");
            static Result<int, string> Failure() => "a failure, by the error's conversion";

            static void Locals()
            {
                Result<string> r = "x"; // expect: RF0004
                _ = r;
            }
        }
        """");

    [Fact]
    public void A_bare_string_into_a_string_shape_is_reported() => Verify("""
        using Resultful;
        using Resultful.CodeAnalysis;

        [ClosedHierarchy] public abstract partial record NameError : Error;
        public sealed record TooLong() : NameError("too long");
        public sealed record Empty() : NameError("empty");

        [ResultShape(typeof(NameError), Success = typeof(string))]
        public readonly partial struct NameResult;

        public static class Cases
        {
            static NameResult Name() => "not a name"; // expect: RF0004
            static NameResult Error() => new TooLong();
        }
        """, generate: true);

    [Fact]
    public void A_result_field_no_constructor_assigns_is_reported() => Verify("""
        using Resultful;

        public sealed class Cache
        {
            Result<int> _never; // expect: RF0006
            Result<int> _initialised = Result.Ok(1);
            Result<int> _inEvery;
            Result<int> _inOne; // expect: RF0006
            Result<int> _byOut;
            Result<int> _byTuple;
            Result<int>? _nullable;
            int _plain;
            public required Result<int> Required;
            public Result<int> Settable { get; set; }
            public Result<int> GetOnly { get; } // expect: RF0006
            public Result<int> Computed => _inEvery;
            static Result<int> s_never; // expect: RF0006
            static Result<int> s_initialised = Result.Ok(0);

            public Cache()
            {
                _inEvery = Result.Ok(1);
                _inOne = Result.Ok(1);
                Make(out _byOut);
                (_byTuple, _plain) = (Result.Ok(2), 2);
            }

            public Cache(int id) : this() => _inEvery = Result.Ok(id);

            public Cache(string id)
            {
                _inEvery = Result.Ok(id.Length);
                Make(out _byOut);
                (_byTuple, _plain) = (Result.Ok(2), 2);
            }

            static void Make(out Result<int> r) => r = Result.Ok(0);
            void Later() { _never = Result.Ok(1); s_never = _never; _nullable = null; }
        }

        public sealed class Implicit
        {
            Result<string> _value; // expect: RF0006
            public Result<string> Get() => _value;
        }

        public sealed record Positional(Result<int> Value);

        public struct InAStruct
        {
            Result<int> _value;
            public Result<int> Get() => _value;
        }

        public static class WithStaticConstructor
        {
            static readonly Result<int> s_assigned;
            static WithStaticConstructor() => s_assigned = Result.Ok(1);
            public static Result<int> Get() => s_assigned;
        }
        """);

    [Fact]
    public void Map_whose_selector_returns_a_result_is_reported() => Verify("""
        using System.Threading.Tasks;
        using Resultful;

        public static class Cases
        {
            static Result<int> Parse(string s) => int.TryParse(s, out var n) ? Result.Ok(n) : Result.Fail<int>("not a number");
            static Result<int, Error> Typed(string s) => Result.Ok<int, Error>(1);
            static Task<Result<int>> ParseAsync(string s) => Task.FromResult(Parse(s));

            static void M(Result<string> r, Result<string, Error> t, Option<string> o)
            {
                _ = r.Map(Parse); // expect: RF0009
                _ = r.Map(s => Parse(s)); // expect: RF0009
                _ = t.Map(s => Typed(s)); // expect: RF0009
                _ = r.Map(s => s.Length);
                _ = r.Map(s => Option.Some(s));
                _ = r.Bind(Parse);
                _ = r.MapAsync(ParseAsync); // expect: RF0009
            }

            static void OnValueTasks(ValueTask<Result<string>> vt, ValueTask<Result<string, Error>> vte)
            {
                _ = vt.Map(Parse); // expect: RF0009
                _ = vte.Map(s => Typed(s)); // expect: RF0009
                _ = vt.Map(s => s.Length);
            }
        }
        """);
}
