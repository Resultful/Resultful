using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Resultful.CodeAnalysis;

namespace Resultful.Tests
{
    // Decisions 15, 16 and 17 (07): the attributes the analyzers and the generator read, IResultShape and IErrorUnion.
    [TestFixture]
    public class CodeAnalysisTests
    {
        static readonly Type[] Attributes = [typeof(MustObserveAttribute), typeof(ClosedHierarchyAttribute), typeof(ResultShapeAttribute)];

        static readonly string[] ErrorOnly = ["Error"];

        static AttributeUsageAttribute Usage(Type attribute) => attribute.GetCustomAttribute<AttributeUsageAttribute>()!;

        // Decision 15: which declarations each attribute goes on. None is inherited or repeatable, so a derived type isn't
        // observed, closed or shaped unless it says so.
        [TestCase(typeof(MustObserveAttribute), AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method)]
        [TestCase(typeof(ClosedHierarchyAttribute), AttributeTargets.Class)]
        [TestCase(typeof(ResultShapeAttribute), AttributeTargets.Struct)]
        public void Each_attribute_goes_on_its_targets_once_and_is_not_inherited(Type attribute, AttributeTargets targets)
        {
            var usage = Usage(attribute);
            Assert.That(usage.ValidOn, Is.EqualTo(targets));
            Assert.That(usage.Inherited, Is.False);
            Assert.That(usage.AllowMultiple, Is.False);
        }

        [TestCaseSource(nameof(Attributes))]
        public void Each_attribute_is_sealed_public_and_in_Resultful_CodeAnalysis(Type attribute)
        {
            Assert.That(attribute.IsSealed && attribute.IsPublic, Is.True);
            Assert.That(attribute.Namespace, Is.EqualTo("Resultful.CodeAnalysis"));
            Assert.That(attribute.Assembly, Is.EqualTo(typeof(Unit).Assembly));
        }

        // A closed base closes its own cases: a derived record isn't closed by inheritance.
        [ClosedHierarchy]
        abstract record ClosedBase() : Error("closed");

        sealed record Leaf() : ClosedBase;

        [Test]
        public void A_derived_type_does_not_inherit_ClosedHierarchy()
        {
            Assert.That(typeof(ClosedBase).IsDefined(typeof(ClosedHierarchyAttribute), inherit: true), Is.True);
            Assert.That(typeof(Leaf).IsDefined(typeof(ClosedHierarchyAttribute), inherit: true), Is.False);
        }

        [TestCase(typeof(Result<int>))]
        [TestCase(typeof(Result<int, string>))]
        [TestCase(typeof(Option<int>))]
        public void The_core_results_carry_MustObserve(Type type)
            => Assert.That(type.IsDefined(typeof(MustObserveAttribute), inherit: false), Is.True);

        [Test]
        public void ResultShape_keeps_its_error_type_and_defaults_to_no_value_and_one_error()
        {
            var shape = new ResultShapeAttribute(typeof(Error));
            Assert.That(shape.ErrorType, Is.EqualTo(typeof(Error)));
            Assert.That(shape.Success, Is.Null);
            Assert.That(shape.ManyErrors, Is.False);
            Assert.Throws<ArgumentNullException>(() => _ = new ResultShapeAttribute(null!));
        }

        readonly struct HandWrittenUnion(Error error) : IErrorUnion
        {
            public Error Error { get; } = error;
        }

        [Test]
        public void IErrorUnion_works_on_every_target()
        {
            IErrorUnion union = new HandWrittenUnion(Error.From("leaf"));
            Assert.That(union.Error.Message, Is.EqualTo("leaf"));
            Assert.That(typeof(IErrorUnion).GetProperties().Select(p => p.Name), Is.EqualTo(ErrorOnly));
        }

        // What a generated shape looks like from the outside: a named struct over one core result. An extension written
        // against the interface binds to it with T and TError inferred (decision 16), which is how satellites take any shape.
        [ResultShape(typeof(Error), Success = typeof(int))]
        readonly struct CountResult(Result<int, Error> inner) : IResultShape<int, Error>
        {
            public Result<int, Error> AsResult() => inner;
        }

        [Test]
        public void An_extension_over_IResultShape_binds_to_a_shape_and_reads_the_core_result()
        {
            var ok = new CountResult(3.Ok<int, Error>());
            var failed = new CountResult(Error.From("count failed").Fail<int, Error>());

            Assert.That(ok.Describe(), Is.EqualTo("ok 3"));
            Assert.That(failed.Describe(), Is.EqualTo("failed: count failed"));

            var shape = typeof(CountResult).GetCustomAttribute<ResultShapeAttribute>()!;
            Assert.That((shape.ErrorType, shape.Success, shape.ManyErrors), Is.EqualTo((typeof(Error), typeof(int), false)));
        }
    }

    static class ShapeExtensions
    {
        public static string Describe<T, TError>(this IResultShape<T, TError> shape) where TError : Error
            => shape.AsResult().TryGetValue(out var value) ? $"ok {value}" : $"failed: {shape.AsResult().Error.Message}";
    }
}
