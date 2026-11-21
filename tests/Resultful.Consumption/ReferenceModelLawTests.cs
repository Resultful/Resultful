using CsCheck;
using Xunit;

namespace Resultful.Consumption;

/// <summary>
/// Runs the same operations on the reference model (ReferenceModel.cs) and on Resultful, and requires the same
/// outcome: the first laws (Map identity and composition, Bind left identity and associativity, Match), then random
/// programs of Map, Bind, MapError and Plus over Result&lt;T&gt;, Result&lt;T, TError&gt; and VoidResult.
/// </summary>
public sealed class ReferenceModelLawTests
{
    private static readonly Gen<string[]> GenErrors = Gen.String[1, 6].Array[0, 3]; // 0.2 allows a failure with no errors

    private static readonly Gen<ModelResult<int>> GenModel = Gen.Frequency(
        (3, Gen.Int[-1000, 1000].Select(ModelResult<int>.Ok)),
        (2, GenErrors.Select(e => ModelResult<int>.Fail(e))));

    private static readonly Gen<ModelResult<int, TestError>> GenModelGeneric = Gen.Frequency(
        (3, Gen.Int[-1000, 1000].Select(ModelResult<int, TestError>.Ok)),
        (2, Gen.String[1, 6].Select(e => ModelResult<int, TestError>.Fail(new TestError(e)))));

    private static readonly Gen<ModelVoid> GenModelVoid = Gen.Frequency(
        (3, Gen.Const(ModelVoid.Ok)),
        (2, GenErrors.Select(e => ModelVoid.Fail(e))));

    // Function families, picked by a generated k, with a model and a library version of each.
    private static int F(int k, int x) => unchecked((x * k) + 1);

    private static string G(int k, int x) => $"#{k}:{x}";

    private static bool Keeps(int k, int x) => x % (Math.Abs(k) + 2) == 0;

    private static ModelResult<int> BindModel(int k, int x) => Keeps(k, x) ? ModelResult<int>.Ok(x / 2) : ModelResult<int>.Fail([$"b{k}:{x}"]);

    private static Result<int> BindReal(int k, int x) => Keeps(k, x) ? (x / 2).Ok() : Result.Fail<int>($"b{k}:{x}");

    private static ModelResult<int, TestError> BindModelGeneric(int k, int x) => Keeps(k, x) ? ModelResult<int, TestError>.Ok(x / 2) : ModelResult<int, TestError>.Fail(new TestError($"b{k}:{x}"));

    private static Result<int, TestError> BindRealGeneric(int k, int x) => Keeps(k, x) ? (x / 2).Ok<int, TestError>() : new TestError($"b{k}:{x}").Fail<int, TestError>();

    private static TestError Merge(TestError left, TestError right) => new($"{left.Message}+{right.Message}");

    [Fact]
    public void Map_identity_and_composition_hold_and_agree_with_the_model()
    {
        Gen.Select(GenModel, Gen.Int[-5, 5], Gen.Int[-5, 5]).Sample((m, j, k) =>
        {
            var real = Real(m);
            Same(m, real.Map(x => x));
            Same(m.Map(x => G(k, F(j, x))), real.Map(x => F(j, x)).Map(x => G(k, x)));
            Same(m.Map(x => G(k, F(j, x))), real.Map(x => G(k, F(j, x))));
        });

        Gen.Select(GenModelGeneric, Gen.Int[-5, 5], Gen.Int[-5, 5]).Sample((m, j, k) =>
        {
            var real = Real(m);
            Same(m, real.Map(x => x));
            Same(m.Map(x => G(k, F(j, x))), real.Map(x => F(j, x)).Map(x => G(k, x)));
            Same(m.Map(x => G(k, F(j, x))), real.Map(x => G(k, F(j, x))));
        });
    }

    [Fact]
    public void Bind_left_identity_and_associativity_hold_and_agree_with_the_model()
    {
        Gen.Select(Gen.Int[-1000, 1000], Gen.Int[-5, 5]).Sample((a, k) =>
        {
            // Left identity: Ok(a).Bind(f) == f(a)
            Same(BindModel(k, a), a.Ok().Bind(x => BindReal(k, x)));
            Same(Observe(BindReal(k, a)), a.Ok().Bind(x => BindReal(k, x)));
            Same(BindModelGeneric(k, a), a.Ok<int, TestError>().Bind(x => BindRealGeneric(k, x)));
        });

        Gen.Select(GenModel, Gen.Int[-5, 5], Gen.Int[-5, 5]).Sample((m, j, k) =>
        {
            // Associativity: r.Bind(f).Bind(g) == r.Bind(x => f(x).Bind(g)), and both equal the model.
            var expected = m.Bind(x => BindModel(j, x)).Bind(x => BindModel(k, x));
            Same(expected, Real(m).Bind(x => BindReal(j, x)).Bind(x => BindReal(k, x)));
            Same(expected, Real(m).Bind(x => BindReal(j, x).Bind(y => BindReal(k, y))));
            // Right identity: r.Bind(Ok) == r
            Same(m, Real(m).Bind(x => x.Ok()));
        });

        Gen.Select(GenModelGeneric, Gen.Int[-5, 5], Gen.Int[-5, 5]).Sample((m, j, k) =>
        {
            var expected = m.Bind(x => BindModelGeneric(j, x)).Bind(x => BindModelGeneric(k, x));
            Same(expected, Real(m).Bind(x => BindRealGeneric(j, x)).Bind(x => BindRealGeneric(k, x)));
            Same(expected, Real(m).Bind(x => BindRealGeneric(j, x).Bind(y => BindRealGeneric(k, y))));
            Same(m, Real(m).Bind(x => x.Ok<int, TestError>()));
        });
    }

    [Fact]
    public void Match_agrees_with_the_model()
    {
        GenModel.Sample(m =>
            Assert.Equal(
                m.Match(v => $"ok {v}", e => $"errors {string.Join(",", e)}"),
                Real(m).Match(v => $"ok {v}", e => $"errors {string.Join(",", e)}")));

        GenModelGeneric.Sample(m =>
            Assert.Equal(
                m.Match(v => $"ok {v}", e => $"error {e.Message}"),
                Real(m).Match(v => $"ok {v}", e => $"error {e.Message}")));

        GenModelVoid.Sample(m =>
            Assert.Equal(
                m.IsOk ? "ok" : $"errors {string.Join(",", m.Errors)}",
                Real(m).Match(_ => "ok", e => $"errors {string.Join(",", e)}")));
    }

    [Fact]
    public void The_model_keeps_the_0_2_hazards_the_library_has()
    {
        // default is a success holding default(T), and a failure may carry no errors (both pinned by 01).
        Same(ModelResult<int>.Ok(0), default(Result<int>));
        Same(ModelResult<int, TestError>.Ok(0), default(Result<int, TestError>));
        Same(ModelResult<int>.Fail([]), Result.Fail<int>(Array.Empty<string>()));
        Same(ModelVoid.Fail([]), Result.Fail(Array.Empty<string>()));
    }

    [Fact]
    public void Random_programs_over_Result_of_T_agree_with_the_model()
    {
        var step = Gen.Select(Gen.Int[0, 3], Gen.Int[-5, 5], GenModel);
        Gen.Select(GenModel, step.Array[0, 8]).Sample((start, steps) =>
        {
            var model = start;
            var real = Real(start);
            foreach (var (op, k, other) in steps)
            {
                switch (op)
                {
                    case 0:
                        model = model.Map(x => F(k, x));
                        real = real.Map(x => F(k, x));
                        break;
                    case 1:
                        model = model.Bind(x => BindModel(k, x));
                        real = real.Bind(x => BindReal(k, x));
                        break;
                    case 2:
                        model = model.Plus(other, (a, b) => unchecked(a + b));
                        real = real.Plus(Real(other), (a, b) => unchecked(a + b));
                        break;
                    default:
                        var modelMapped = model.MapError(e => new TestError(string.Join(",", e)));
                        var realMapped = real.MapError(e => new TestError(string.Join(",", e)));
                        Same(modelMapped, realMapped);
                        break;
                }

                Same(model, real);
            }
        });
    }

    [Fact]
    public void Random_programs_over_Result_of_T_and_TError_agree_with_the_model()
    {
        var step = Gen.Select(Gen.Int[0, 3], Gen.Int[-5, 5], GenModelGeneric);
        Gen.Select(GenModelGeneric, step.Array[0, 8]).Sample((start, steps) =>
        {
            var model = start;
            var real = Real(start);
            foreach (var (op, k, other) in steps)
            {
                switch (op)
                {
                    case 0:
                        model = model.Map(x => F(k, x));
                        real = real.Map(x => F(k, x));
                        break;
                    case 1:
                        model = model.Bind(x => BindModelGeneric(k, x));
                        real = real.Bind(x => BindRealGeneric(k, x));
                        break;
                    case 2:
                        model = model.Plus(other, Merge, (a, b) => unchecked(a - b));
                        real = real.Plus(Real(other), Merge, (a, b) => unchecked(a - b));
                        break;
                    default:
                        model = model.MapError(e => new TestError($"{e.Message}!{k}"));
                        real = real.MapError(e => new TestError($"{e.Message}!{k}"));
                        break;
                }

                Same(model, real);
            }
        });
    }

    [Fact]
    public void Random_programs_over_VoidResult_agree_with_the_model()
    {
        var step = Gen.Select(Gen.Bool, GenModelVoid);
        Gen.Select(GenModelVoid, step.Array[0, 8], Gen.Int).Sample((start, steps, value) =>
        {
            var model = start;
            var real = Real(start);
            foreach (var (bind, other) in steps)
            {
                if (bind)
                {
                    model = model.Bind(() => other);
                    real = real.Bind(_ => Real(other));
                }
                else
                {
                    model = model.Plus(other);
                    real = real.Plus(Real(other));
                }

                Same(model, real);
            }

            Same(model.Map(value), real.Map(value));
        });
    }

    private static Result<int> Real(ModelResult<int> m) => m.IsOk ? m.Value.Ok() : Result.Fail<int>(m.Errors);

    private static Result<int, TestError> Real(ModelResult<int, TestError> m) => m.IsOk ? m.Value.Ok<int, TestError>() : m.Error.Fail<int, TestError>();

    private static VoidResult Real(ModelVoid m) => m.IsOk ? Result.Ok() : Result.Fail(m.Errors.ToArray());

    private static ModelResult<T> Observe<T>(Result<T> r) => r.Match(ModelResult<T>.Ok, e => ModelResult<T>.Fail(e));

    // Each part is compared on its own: the case, then the value (successes) or the errors in order (failures).
    private static void Same<T>(ModelResult<T> expected, Result<T> actual)
    {
        var observed = Observe(actual);
        Assert.True(expected.IsOk == observed.IsOk, $"model {expected}, library {observed}");
        if (expected.IsOk)
        {
            Assert.Equal(expected.Value, observed.Value);
        }

        Assert.Equal(expected.Errors, observed.Errors);
    }

    private static void Same<T, TError>(ModelResult<T, TError> expected, Result<T, TError> actual)
    {
        var observed = actual.Match(ModelResult<T, TError>.Ok, ModelResult<T, TError>.Fail);
        Assert.True(expected.IsOk == observed.IsOk, $"model {expected}, library {observed}");
        if (expected.IsOk)
        {
            Assert.Equal(expected.Value, observed.Value);
        }
        else
        {
            Assert.Equal(expected.Error, observed.Error);
        }
    }

    private static void Same(ModelVoid expected, VoidResult actual)
    {
        var observed = actual.Match(_ => ModelVoid.Ok, e => ModelVoid.Fail(e));
        Assert.True(expected.IsOk == observed.IsOk, $"model {expected}, library {observed}");
        Assert.Equal(expected.Errors, observed.Errors);
    }
}
