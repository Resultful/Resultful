using Xunit;

namespace Resultful.Consumption;

/// <summary>
/// Pins what each public member does with <c>null</c>, where its nullable annotation depends on it (the 0.2 nullable
/// audit). The five <c>Fail</c> overloads that take a nullable list or array, and the <c>string[]</c> conversion to
/// <c>VoidResult</c>, treat <c>null</c> as no errors; the <c>Unit</c> conversions give a success for <c>null</c>. That is
/// why they are annotated <c>?</c>. The members next to them keep a non-nullable annotation, because a <c>null</c>
/// there gives a broken result or throws.
/// </summary>
public sealed class NullabilityTests
{
    [Fact]
    public void Fail_overloads_annotated_nullable_treat_null_as_no_errors()
    {
        Assert.Empty(Result.Fail<int, TestError>((IEnumerable<TestError>?)null).Match(_ => [new TestError("ok")], e => e));
        Assert.Empty(Result.Fail<int, TestError>((TestError[]?)null).Match(_ => [new TestError("ok")], e => e));
        Assert.Empty(Result.Fail<TestError>((TestError[]?)null).Match(_ => [new TestError("ok")], e => e));
        Assert.Empty(Result.Fail((IEnumerable<string>?)null).Errors());
        Assert.Empty(Result.Fail((string[]?)null).Errors());
    }

    [Fact]
    public void Conversions_annotated_nullable_handle_null()
    {
        // The string[] conversion passes the array to Fail(params string[]?), so null is a failure with no errors.
        VoidResult fromNullArray = (string[]?)null;
        Assert.Empty(fromNullArray.Errors());

        // The Unit conversions ignore the Unit they are given, so null gives a success holding Unit.Value.
        VoidResult fromNullUnit = (Unit?)null;
        Assert.Same(Unit.Value, fromNullUnit.Match(u => u, _ => null!));
        VoidResult<TestError> fromNullUnitTyped = (Unit?)null;
        Assert.Same(Unit.Value, fromNullUnitTyped.Match(u => u, _ => null!));
    }

    [Fact]
    public void Members_left_non_nullable_do_not_accept_null()
    {
        // Result<T> keeps a null error list as is, so it is a failure whose errors are null.
        Assert.Null(Result.Fail<int>((string[])null!).Match(_ => [], e => e));
        Assert.Null(Result.Fail<int>((IEnumerable<string>)null!).Match(_ => [], e => e));

        // The List<string> conversion to VoidResult copies the list, so a null list throws.
        Assert.Throws<NullReferenceException>(() =>
        {
            VoidResult converted = (List<string>)null!;
            return converted;
        });
    }
}
