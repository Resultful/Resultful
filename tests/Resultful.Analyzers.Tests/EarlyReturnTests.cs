using Microsoft.CodeAnalysis;
using Xunit;
using static Resultful.Analyzers.Tests.AnalyzerHarness;

namespace Resultful.Analyzers.Tests;

/// <summary>
/// Decision 22 items 1 and 2 (24b): the compiler's view of <c>Failed</c> (nullable flow on the net and netstandard2.0
/// builds of the core, <c>return error;</c>, the ambiguity when <c>T</c> is <c>TError</c>), RF0001 and RF0003 reading
/// <c>Failed</c>, and RF0011.
/// </summary>
public sealed class EarlyReturnTests
{
    // The canonical early return on every result type, sync and async, with no `!`; and the two places the compiler must
    // warn: the value inside the failed branch, the error after it.
    const string Flow = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Resultful;

        public sealed record Order(string Id);

        public static class Flow
        {
            static Result<Order, string> Find(string id) => new Order(id);

            public static Result<int, string> Typed(string id)
            {
                if (Find(id).Failed(out var order, out var error))
                    return error;
                return order.Id.Length;
            }

            public static Result<int> Core(Result<Order> r)
            {
                if (r.Failed(out var order, out var error))
                    return error;
                return order.Id.Length;
            }

            public static Validation<int, string> Gathered(Validation<Order, string> v)
            {
                if (v.Failed(out var order, out var errors))
                    return Validation.Fail<int, string>(errors);
                return order.Id.Length;
            }

            public static int Validated(Validation<Order> v)
            {
                if (v.Failed(out var order, out var errors))
                    return errors.Count;
                return order.Id.Length;
            }

            public static async Task<Result<int, string>> Later(Task<Result<Order, string>> pending)
            {
                if ((await pending).Failed(out var order, out var error))
                    return error;
                return order.Id.Length;
            }

            public static int Wrong(Result<Order, string> r)
            {
                var length = r.Failed(out var order, out var error) ? order.Id.Length : 0; // expect: CS8602
                return length + error.Length; // expect: CS8602
            }
        }
        """;

    [Fact]
    public void Nullable_flow_knows_the_error_is_set_in_the_branch_and_the_value_after_it() => Verify(Flow);

    // The netstandard2.0 build carries its own internal copies of the attributes (Polyfills.cs); the compiler reads them by name.
    [Fact]
    public void Nullable_flow_is_the_same_against_the_netstandard20_build()
    {
        var path = Metadata("CoreNetStandardBuild");
        Assert.True(File.Exists(path), path);
        var compilation = Compile([Flow], withCore: false).AddReferences(MetadataReference.CreateFromFile(path));
        var reported = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => (d.Location.GetMappedLineSpan().Path, d.Location.GetMappedLineSpan().StartLinePosition.Line + 1, d.Id))
            .ToHashSet();
        Assert.Equal(Markers([Flow]).Order(), reported.Order());
    }

    [Fact]
    public void Return_error_compiles_for_typed_results_but_not_when_T_is_TError_or_TError_is_an_interface() => Verify("""
        using Resultful;

        public sealed record Missing() : Error("missing");
        public interface IFailure { }

        public static class Conversions
        {
            static Result<int, string> Typed(bool ok) => ok ? 1 : "bad";
            static Result<int, Error> Base(Missing m) => m;
            static Result<string, string> Same(string e) => e; // expect: CS0457
            static Result<string, string> Said(string e) => e.Fail<string, string>();
            static Result<int, IFailure> Interface(IFailure e) => e; // expect: CS0029
            static Result<int, IFailure> InterfaceSaid(IFailure e) => e.Fail<int, IFailure>();
        }
        """);

    [Fact]
    public void RF0001_counts_Failed_as_observing_and_RF0003_counts_it_as_the_check() => Verify("""
        using System.Threading.Tasks;
        using Resultful;

        public static class Observed
        {
            static Result<int> Get() => Result.Ok(1);
            static Task<Result<int>> GetAsync() => Task.FromResult(Result.Ok(1));

            static int Calls()
            {
                if (Get().Failed(out var v, out _)) return 0;
                Get().Failed(out _, out _);
                Get(); // expect: RF0001
                return v;
            }

            static async Task<int> Awaited()
            {
                if ((await GetAsync()).Failed(out var v, out _)) return 0;
                return v;
            }

            static int Exit(Result<int> r)
            {
                if (r.Failed(out _, out _)) return 0;
                return r.Value;
            }

            static int Else(Result<int> r, Validation<int> v)
            {
                if (r.Failed(out _, out _)) return 0; else return r.Value;
            }

            static int Negated(Result<int, string> r)
            {
                if (!r.Failed(out _, out _)) return r.Value;
                return r.Value; // expect: RF0003
            }

            static int Validated(Validation<int> v)
            {
                if (v.Failed(out _, out var errors)) return errors.Count;
                return v.Value;
            }

            static int Other(Result<int> r, Result<int> other)
            {
                if (other.Failed(out _, out _)) return 0;
                return r.Value; // expect: RF0003
            }
        }
        """);

    [Fact]
    public void RF0011_reports_a_value_type_value_read_where_Failed_returned_true() => Verify("""
        using System;
        using System.Threading.Tasks;
        using Resultful;

        public static class Cases
        {
            static Result<int> Get() => Result.Ok(1);
            static Task<Result<int>> GetAsync() => Task.FromResult(Result.Ok(1));

            static Result<int> Branch()
            {
                if (Get().Failed(out var value, out var error))
                {
                    Console.WriteLine(value); // expect: RF0011
                    return error;
                }

                return value + 1;
            }

            static int Exit(Result<int, string> r)
            {
                if (!r.Failed(out var v, out _)) return v;
                return v; // expect: RF0011
            }

            static int Else(Result<int, string> r)
            {
                if (!r.Failed(out var v, out _)) return v;
                else return -v; // expect: RF0011
            }

            static int Operators(Result<int> r)
            {
                var a = r.Failed(out var v, out _) ? v : 0; // expect: RF0011
                var b = r.Failed(out var w, out _) && w > 0; // expect: RF0011
                var c = !r.Failed(out var x, out _) || x > 0; // expect: RF0011
                var d = r.Failed(out var y, out _) || y > 0;
                var e = !(r.Failed(out var z, out _)) ? z : 0;
                return a + w + x + y + z + (b && c && d ? 1 : 0) + e;
            }

            static async Task<int> Awaited()
            {
                if ((await GetAsync()).Failed(out var v, out var e))
                    return v + e.Message.Length; // expect: RF0011
                return v;
            }

            static int Nested(Result<int> a, Result<int> b)
            {
                if (a.Failed(out var x, out _))
                {
                    if (b.Failed(out var y, out _))
                        return x + y; // expect: RF0011
                    return y + x; // expect: RF0011
                }

                return x;
            }

            static int Reassigned(Result<int> r)
            {
                if (r.Failed(out var v, out _))
                {
                    v = 42;
                    return v;
                }

                if (r.Failed(out var w, out _))
                {
                    Console.WriteLine(w); // expect: RF0011
                    w = 1;
                    return w;
                }

                if (r.Failed(out var u, out _) && Get().TryGetValue(out u)) return u;
                return v + w + u;
            }

            static int Declared(Result<int> r)
            {
                int v;
                if (r.Failed(out v, out _)) return v; // expect: RF0011
                return v;
            }

            static int Loop(Result<int> r)
            {
                var n = 0;
                while (r.Failed(out var v, out _))
                {
                    n += v; // expect: RF0011
                    r = 1;
                }

                return n;
            }

            static int Captured(Result<int> r)
            {
                if (r.Failed(out var v, out _))
                {
                    Func<int> later = () => v; // expect: RF0011
                    return later();
                }

                return v;
            }

            static int Validations(Validation<int> v, Validation<int, string> w)
            {
                if (v.Failed(out var a, out _)) return a; // expect: RF0011
                if (w.Failed(out var b, out _)) return b; // expect: RF0011
                return a + b;
            }

            static int Constrained<T>(Result<T> r, Func<T, int> f) where T : struct
            {
                if (r.Failed(out var v, out _)) return f(v); // expect: RF0011
                return f(v);
            }

            // Left to nullable analysis or not a value: a reference type, Nullable<T>, an unconstrained T, a discard.
            static int Others<T>(Result<string> s, Result<int?> n, Result<T> t, Func<T, int> f)
            {
                if (s.Failed(out var text, out _)) return 0;
                if (n.Failed(out var number, out _)) return number ?? 0;
                if (t.Failed(out var any, out _)) return 0;
                if (Get().Failed(out _, out _)) return 0;
                return text.Length + (number ?? 0) + f(any);
            }

            static int Unrelated(Result<int> r)
            {
                var v = 0;
                if (r.Failed(out _, out _)) return v;
                if (r.IsFailure) return r.Error.Message.Length;
                return v;
            }
        }
        """);

    // Review of #86: what isn't the failed code, and writes that end the scan.
    [Fact]
    public void RF0011_skips_local_functions_labels_and_every_kind_of_write() => Verify("""
        using System;
        using Resultful;

        public static class Writes
        {
            static int LocalFunction(Result<int> r)
            {
                if (!r.Failed(out var v, out _)) return Double();
                return -1;
                int Double() => v * 2;
            }

            static int LocalFunctionInTheBranch(Result<int> r)
            {
                if (r.Failed(out var v, out _))
                {
                    return Twice();
                    int Twice() => v * 2; // expect: RF0011
                }

                return v;
            }

            static int Label(Result<int> r, bool again)
            {
                if (again) goto done;
                if (!r.Failed(out var v, out _)) return v;
                Console.WriteLine(v); // expect: RF0011
                done:
                return 0;
            }

            static int Deconstructed(Result<int> r)
            {
                if (r.Failed(out var v, out _))
                {
                    (v, _) = (1, 2);
                    return v;
                }

                return v;
            }

            static int Nested(Result<int> r)
            {
                if (r.Failed(out var v, out _))
                {
                    ((v, _), _) = ((1, 2), 3);
                    return v;
                }

                return v;
            }

            static int Aliased(Result<int> r)
            {
                if (r.Failed(out var v, out _))
                {
                    ref var alias = ref v;
                    alias = 5;
                    return v;
                }

                return v;
            }

            static int ByRef(Result<int> r)
            {
                if (r.Failed(out var v, out _))
                {
                    Set(ref v);
                    return v;
                }

                return v;
            }

            static void Set(ref int x) => x = 3;

            static int Incremented(Result<int> r)
            {
                if (r.Failed(out var v, out _))
                {
                    v++;
                    return v;
                }

                if (r.Failed(out var w, out _))
                {
                    --w;
                    return w;
                }

                return v + w;
            }

            static int Compound(Result<int> r)
            {
                if (r.Failed(out var v, out _))
                {
                    v += 4;
                    return v;
                }

                return v;
            }

            static int Read(Result<int> r)
            {
                if (r.Failed(out var v, out _))
                {
                    var t = (v, 1); // expect: RF0011
                    return t.Item2;
                }

                return v;
            }
        }
        """);

    [Fact]
    public void RF0011_and_return_error_work_on_a_generated_shape() => Verify("""
        using System;
        using Resultful;
        using Resultful.CodeAnalysis;

        [ClosedHierarchy]
        public abstract partial record CountError : Error;
        public sealed record TooMany() : CountError("too many");

        [ResultShape(typeof(CountError), Success = typeof(int))]
        public readonly partial struct CountResult;

        public static class Counting
        {
            static CountResult Next(CountResult r)
            {
                if (r.Failed(out var n, out var error))
                {
                    Console.WriteLine(n); // expect: RF0011
                    return error;
                }

                return n + 1;
            }
        }
        """, generate: true);
}
