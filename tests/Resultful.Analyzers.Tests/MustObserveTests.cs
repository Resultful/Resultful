using Xunit;
using static Resultful.Analyzers.Tests.AnalyzerHarness;

namespace Resultful.Analyzers.Tests;

/// <summary>RF0001 on a user's own [MustObserve] types (VERIFY.md section 4): positive, negative, async, `_ =`.</summary>
public sealed class MustObserveTests
{
    // A user's own result type, marked with the core's attribute (the target's Resultful.CodeAnalysis).
    const string Reply = """
        using System;
        using System.Threading.Tasks;
        using Resultful.CodeAnalysis;

        [MustObserve]
        public readonly struct Reply<T>
        {
            public T Body { get; init; }
            public Reply<T> Tee(Action<T> action) => this;
            public void Switch(Action<T> onOk, Action<string> onError) { }
            public Task SwitchAsync(Func<T, Task> onOk, Func<string, Task> onError) => Task.CompletedTask;
        }

        public static class ReplyExtensions
        {
            public static void Each<T>(this Reply<T> reply, Action<T> action) { }
            public static Task Switch<T>(this Task<Reply<T>> reply, Action<T> onOk, Action<string> onError) => reply;
            public static ValueTask<Reply<T>> Tee<T>(this ValueTask<Reply<T>> reply, Action<T> action) => reply;
        }

        public sealed class Plain { public Plain Next() => this; }

        public static class Api
        {
            public static Reply<int> Make(int id) => new() { Body = id };
            public static Reply<int>? Maybe(int id) => null;
            public static Task<Reply<int>> MakeAsync(int id) => Task.FromResult(Make(id));
            public static ValueTask<Reply<int>> CheckAsync(int id) => new(Make(id));
            public static Task DoAsync() => Task.CompletedTask;
            public static async void FireAndForget(int v) => await Task.Yield();
            public static void Handle(int v) { }
            [MustObserve] public static int Count() => 1;
            public static int Uncounted() => 1;
            public static void Use(Reply<int> reply) { }
        }

        """;

    static void Check(string body, string? mustObserveTypes = null) => Verify(Reply + body, mustObserveTypes);

    [Fact]
    public void A_call_whose_result_is_dropped_is_reported() => Check("""
        public static class Cases
        {
            static void Forgets() { Api.Make(1); } // expect: RF0001
        }
        """);

    [Fact]
    public void A_new_value_dropped_is_reported() => Check("""
        public static class Cases
        {
            static void Forgets() { new Reply<int>(); } // expect: RF0001
        }
        """);

    [Fact]
    public void A_conditional_call_dropped_is_reported() => Check("""
        public static class Cases
        {
            static void Forgets(Reply<int>? r) { r?.Tee(_ => { }); } // expect: RF0001
        }
        """);

    [Fact]
    public void A_chained_call_that_returns_the_result_again_is_reported() => Check("""
        public static class Cases
        {
            static void Forgets() { Api.Make(1).Tee(_ => { }); } // expect: RF0001
        }
        """);

    [Fact]
    public void A_void_lambda_and_an_expression_bodied_void_member_drop_the_value() => Check("""
        public static class Cases
        {
            static readonly Action Lambda = () => Api.Make(1); // expect: RF0001
            static void Member() => Api.Make(1); // expect: RF0001
        }
        """);

    [Fact]
    public void A_dropped_value_from_a_must_observe_method_is_reported_whatever_its_type() => Check("""
        public static class Cases
        {
            static void Forgets() { Api.Count(); Api.Uncounted(); } // expect: RF0001
        }
        """);

    [Fact]
    public void Using_the_value_in_any_way_observes_it() => Check("""
        public static class Cases
        {
            static Reply<int> Returns() => Api.Make(1);
            static void Assigns() { var r = Api.Make(1); Reply<int> s; s = Api.Make(2); Api.Use(r); }
            static void Passes() => Api.Use(Api.Make(1));
            static int Reads() => Api.Make(1).Body;
            static void Handles() { Api.Make(1).Switch(v => { }, e => { }); }
            static void Plain() { new Plain().Next(); Api.Uncounted(); }
            static Func<Reply<int>> Lambda() => () => Api.Make(1);
        }
        """);

    [Fact]
    public void Only_an_explicit_discard_passes() => Check("""
        public static class Cases
        {
            static void Discards()
            {
                _ = Api.Make(1);
                _ = Api.MakeAsync(1);
                _ = Api.CheckAsync(1);
                _ = Api.Count();
                _ = new Reply<int>();
            }

            static async Task DiscardsAwaited() { _ = await Api.MakeAsync(1); }
            static readonly Action Lambda = () => _ = Api.Make(1);
        }
        """);

    [Fact]
    public void An_unawaited_task_or_value_task_of_a_result_is_reported() => Check("""
        public static class Cases
        {
            static void Sync()
            {
                Api.MakeAsync(1); // expect: RF0001
                Api.CheckAsync(2); // expect: RF0001
                Api.MakeAsync(3).ConfigureAwait(false); // expect: RF0001
            }

            static async Task Async()
            {
                Api.MakeAsync(1); // expect: RF0001, CS4014
                await Api.DoAsync();
            }
        }
        """);

    [Fact]
    public void An_awaited_result_that_is_dropped_is_reported() => Check("""
        public static class Cases
        {
            static async Task Forgets()
            {
                await Api.MakeAsync(1); // expect: RF0001
                await Api.CheckAsync(2); // expect: RF0001
                await Api.MakeAsync(3).ConfigureAwait(false); // expect: RF0001
                await Api.DoAsync();
                var r = await Api.MakeAsync(4);
                Api.Use(await Api.MakeAsync(5));
            }
        }
        """);

    [Fact]
    public void An_async_lambda_passed_as_a_void_handler_is_reported() => Check("""
        public static class Cases
        {
            static void Handlers(Reply<int> r)
            {
                r.Switch(async v => await Task.Yield(), e => { }); // expect: RF0001
                r.Switch(v => { }, async e => await Task.Yield()); // expect: RF0001
                _ = r.Tee(async v => await Task.Yield()); // expect: RF0001
                r.Each(async delegate (int v) { await Task.Yield(); }); // expect: RF0001
            }
        }
        """);

    [Fact]
    public void An_async_void_handler_on_a_task_or_value_task_of_a_result_is_reported() => Check("""
        public static class Cases
        {
            static async Task Handlers()
            {
                await Api.MakeAsync(1).Switch(async v => await Task.Yield(), e => { }); // expect: RF0001
                _ = await Api.CheckAsync(2).Tee(async v => await Task.Yield()); // expect: RF0001
                await Api.MakeAsync(3).Switch(v => { }, e => { });
            }
        }
        """);

    [Fact]
    public void An_async_void_handler_on_the_cores_task_of_result_is_reported() => Check("""
        public static class Cases
        {
            static Task<Resultful.Result<int>> GetAsync() => Task.FromResult(Resultful.Result.Ok(1));

            static async Task Handlers()
            {
                await Resultful.Result.Switch(GetAsync(), async v => await Task.Yield(), e => { }); // expect: RF0001
                _ = await Resultful.Result.Tee(GetAsync(), async v => await Task.Yield()); // expect: RF0001
                Resultful.Result.Ok(1).Switch(async v => await Task.Yield(), e => { }); // expect: RF0001
            }
        }
        """);

    [Fact]
    public void An_async_void_method_group_is_a_handler_too() => Check("""
        public static class Cases
        {
            static void Handlers(Reply<int> r)
            {
                r.Switch(Api.FireAndForget, e => { }); // expect: RF0001
                r.Switch(Api.Handle, e => { });
            }
        }
        """);

    [Fact]
    public void An_array_of_results_dropped_or_awaited_and_dropped_is_reported() => Check("""
        public static class Cases
        {
            static Reply<int>[] All() => [];

            static async Task Forgets()
            {
                All(); // expect: RF0001
                await Task.WhenAll(Api.MakeAsync(1), Api.MakeAsync(2)); // expect: RF0001
                Task.WhenAll(Api.MakeAsync(1)); // expect: RF0001
                var all = await Task.WhenAll(Api.MakeAsync(1), Api.MakeAsync(2));
                _ = await Task.WhenAll(Api.MakeAsync(3));
            }
        }
        """);

    [Fact]
    public void A_task_of_a_result_converted_to_Task_is_reported() => Check("""
        public static class Cases
        {
            static Task Returns() => Api.MakeAsync(1); // expect: RF0001
            static readonly Func<Task> Lambda = () => Api.MakeAsync(1); // expect: RF0001
            static Task Stored() { Task t = Api.MakeAsync(1); return t; } // expect: RF0001
            static Task Variable(Task<Reply<int>> t) => t;
            static Task Explicit() => (Task)Api.MakeAsync(1);
            static Task<Reply<int>> Kept() => Api.MakeAsync(1);
            static async Task Awaits() { await Task.WhenAny(Api.MakeAsync(1), Api.DoAsync()); } // expect: RF0001
        }
        """);

    [Fact]
    public void An_async_handler_given_a_task_returning_parameter_or_on_another_type_is_not_reported() => Check("""
        public static class Cases
        {
            static void Run(Action action) { }

            static async Task Handlers(Reply<int> r)
            {
                await r.SwitchAsync(async v => await Task.Yield(), async e => await Task.Yield());
                r.Switch(v => { }, e => { });
                Run(async () => await Task.Yield());
            }
        }
        """);

    [Fact]
    public void Generated_code_is_not_analyzed() => Verify([Reply, """
        // <auto-generated/>
        public static class Cases
        {
            static void Forgets() { Api.Make(1); }
        }
        """]);

    [Fact]
    public void An_internal_copy_of_the_attribute_drives_the_rule_without_the_core()
    {
        // A library that doesn't reference Resultful declares its own internal attribute (decision 15).
        Verify("""
            namespace Resultful.CodeAnalysis
            {
                [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.Class | System.AttributeTargets.Method)]
                internal sealed class MustObserveAttribute : System.Attribute;
            }

            namespace Vendor
            {
                [Resultful.CodeAnalysis.MustObserve]
                public readonly struct ApiResponse<T> { public T Body { get; init; } }

                public static class Client
                {
                    public static ApiResponse<string> Post() => new() { Body = "ok" };
                    [Resultful.CodeAnalysis.MustObserve] public static bool TrySend() => true;
                    static void Forgets()
                    {
                        Post(); // expect: RF0001
                        TrySend(); // expect: RF0001
                    }
                }
            }
            """, withCore: false);
    }

    [Fact]
    public void An_attribute_with_the_same_short_name_in_another_namespace_does_not() => Verify("""
        namespace Elsewhere
        {
            internal sealed class MustObserveAttribute : System.Attribute;

            [MustObserve]
            public readonly struct ApiResponse { }

            public static class Client
            {
                public static ApiResponse Post() => new();
                static void Forgets() { Post(); }
            }
        }
        """, withCore: false);

    [Fact]
    public void An_interface_marked_must_observe_is_checked_where_it_is_the_static_type() => Check("""
        [MustObserve] public interface IOutcome { }
        public sealed class Outcome : IOutcome { }

        public static class Cases
        {
            static IOutcome Get() => new Outcome();
            static void Forgets() { Get(); } // expect: RF0001
        }
        """);
}
