// Compiled into every fixture by SpecHarness. Not part of any package.
#nullable enable
namespace Resultful.Spec.Support
{
    /// <summary>A static, parameterless method the harness calls. It passes if it returns (or its task completes).</summary>
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class SpecCaseAttribute : System.Attribute
    {
    }

    public sealed class SpecFailure(string message) : System.Exception(message);

    /// <summary>Plain assertions, so fixtures depend on no test framework.</summary>
    public static class Check
    {
        public static void True(bool condition, string what)
        {
            if (!condition) throw new SpecFailure("expected true: " + what);
        }

        public static void False(bool condition, string what)
        {
            if (condition) throw new SpecFailure("expected false: " + what);
        }

        public static void Equal<T>(T expected, T actual, string what = "")
        {
            if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(expected, actual))
                throw new SpecFailure($"{what}: expected <{expected}>, got <{actual}>");
        }

        public static void SequenceEqual<T>(System.Collections.Generic.IEnumerable<T> expected, System.Collections.Generic.IEnumerable<T> actual, string what = "")
        {
            var e = System.Linq.Enumerable.ToList(expected);
            var a = System.Linq.Enumerable.ToList(actual);
            if (!System.Linq.Enumerable.SequenceEqual(e, a))
                throw new SpecFailure($"{what}: expected [{string.Join(", ", e)}], got [{string.Join(", ", a)}]");
        }

        public static TException Throws<TException>(System.Action action, string what = "") where TException : System.Exception
        {
            try { action(); }
            catch (TException ex) { return ex; }
            catch (System.Exception ex) { throw new SpecFailure($"{what}: expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}"); }
            throw new SpecFailure($"{what}: expected {typeof(TException).Name}, nothing was thrown");
        }

        public static async System.Threading.Tasks.Task<TException> ThrowsAsync<TException>(System.Func<System.Threading.Tasks.Task> action, string what = "") where TException : System.Exception
        {
            try { await action(); }
            catch (TException ex) { return ex; }
            catch (System.Exception ex) { throw new SpecFailure($"{what}: expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}"); }
            throw new SpecFailure($"{what}: expected {typeof(TException).Name}, nothing was thrown");
        }

        public static void DoesNotThrow(System.Action action, string what)
        {
            try { action(); }
            catch (System.Exception ex) { throw new SpecFailure($"{what}: threw {ex.GetType().Name}: {ex.Message}"); }
        }

        /// <summary>Bytes allocated by <paramref name="action"/> on this thread, after a warm-up run.</summary>
        public static long Allocated(System.Action action, int iterations = 100)
        {
            for (var i = 0; i < iterations; i++) action();
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; i++) action();
            return (System.GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        }
    }
}
