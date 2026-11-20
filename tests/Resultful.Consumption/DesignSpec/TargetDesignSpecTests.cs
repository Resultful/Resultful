using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Resultful.Consumption.DesignSpec;

/// <summary>
/// Compile-level design-target specification for PR #34 (Decisions 14, 15, 16; docs/error-shape.md; docs/errors-in-practice.md).
/// Active tests verify C# / .NET 11 compiler contracts against stand-in shapes today.
/// Skipped tests name the milestone (M2-M6) that will turn them on against the real Resultful APIs.
/// </summary>
public sealed class TargetDesignSpecTests
{
    private const string StandInCore = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.CompilerServices;

        namespace Resultful.StandIn
        {
            public readonly struct Unit { public static Unit Value => default; }

            public abstract record Error
            {
                protected Error(string message) => Message = message;
                public string Message { get; }
                public Error? Cause { get; init; }
                public ImmutableList<string> Context { get; init; } = ImmutableList<string>.Empty;
                public static Error From(string message) => new MessageError(message);
            }

            public sealed record MessageError : Error
            {
                public MessageError(string message) : base(message) { }
            }

            public static class ErrorExtensions
            {
                public static TError WithContext<TError>(this TError error, string note) where TError : Error
                    => error with { Context = error.Context.Add(note) };
            }

            public readonly struct Result<T, TError>
            {
                private readonly byte _state;
                private readonly T _value;
                private readonly TError _error;
                [Obsolete("Use Ok or Fail", error: true)]
                public Result() => throw new InvalidOperationException();
                private Result(T v) { _state = 1; _value = v; _error = default!; }
                private Result(TError e) { _state = 2; _value = default!; _error = e; }
                public static Result<T, TError> Ok(T v) => new(v);
                public static Result<T, TError> Fail(TError e) => new(e);
                public bool IsSuccess { get { if (_state == 0) throw new InvalidOperationException(); return _state == 1; } }
                public bool TryGetValue([MaybeNullWhen(false)] out T value)
                {
                    if (_state == 0) throw new InvalidOperationException();
                    if (_state == 1) { value = _value; return true; }
                    value = default; return false;
                }
                public bool TryGetError([MaybeNullWhen(false)] out TError error)
                {
                    if (_state == 0) throw new InvalidOperationException();
                    if (_state == 2) { error = _error; return true; }
                    error = default; return false;
                }
            }

            public abstract record OrderError : Error
            {
                private protected OrderError(string message) : base(message) { }
            }
            public sealed record OrderNotFound(int Id) : OrderError($"order {Id} not found");
            public sealed record OrderLocked(int Id) : OrderError($"order {Id} locked");
            public sealed record Order(int Id);
        }
        """;

    [Fact]
    public void Decision14_bare_string_error_does_not_compile_with_CS0029()
    {
        // Doc claim: docs/decisions/0014-result-and-validation.md & docs/errors-in-practice.md (#10)
        // "Result<int> F() => \"bad\"; no longer compiles (CS0029)."
        var code = StandInCore + """
            namespace Probe
            {
                using Resultful.StandIn;
                public static class C
                {
                    public static Result<int, Error> F() => "bad";
                }
            }
            """;

        var diagnostics = CompilationHarness.Compile(code);
        Assert.Contains(diagnostics, d => d.Id == "CS0029" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Decision10_new_result_parameterless_ctor_fails_with_CS0619()
    {
        // Doc claim: docs/decisions/0010-default-is-not-success.md
        // "new Result<T>() doesn't compile (CS0619)."
        var code = StandInCore + """
            namespace Probe
            {
                using Resultful.StandIn;
                public static class C
                {
                    public static Result<int, Error> F() => new Result<int, Error>();
                }
            }
            """;

        var diagnostics = CompilationHarness.Compile(code);
        Assert.Contains(diagnostics, d => d.Id == "CS0619" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Decision14_WithContext_preserves_concrete_error_type_for_closed_switches()
    {
        // Doc claim: docs/decisions/0014-result-and-validation.md & docs/error-shape.md
        // "WithContext adds a note to ImmutableList<string> Context via with, so the error keeps its type."
        var code = StandInCore + """
            namespace Probe
            {
                using Resultful.StandIn;
                public static class C
                {
                    public static OrderError Wrap(OrderError e) => e.WithContext("placing order");
                    public static int Handle(OrderNotFound e)
                    {
                        OrderNotFound noted = e.WithContext("checkout");
                        return noted.Id;
                    }
                }
            }
            """;

        var diagnostics = CompilationHarness.Compile(code);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void Decision16_narrow_union_shape_switches_exhaustively_and_warns_CS8509_when_arm_missing()
    {
        // Doc claim: docs/decisions/0016-source-generated-result-shapes.md (item 8 & 9)
        // "[Union] structs wrapping a core result switch exhaustively; removing any arm gives CS8509."
        var shapeDef = """
            namespace Probe
            {
                using System;
                using System.Diagnostics.CodeAnalysis;
                using System.Runtime.CompilerServices;
                using Resultful.StandIn;

                [Union]
                public readonly struct PlaceOrderResult : IUnion
                {
                    private readonly Result<Order, OrderError> _inner;
                    public PlaceOrderResult(Order value) => _inner = Result<Order, OrderError>.Ok(value);
                    public PlaceOrderResult(OrderNotFound error) => _inner = Result<Order, OrderError>.Fail(error);
                    public PlaceOrderResult(OrderLocked error) => _inner = Result<Order, OrderError>.Fail(error);
                    public bool HasValue => true;
                    public object? Value => _inner.TryGetValue(out var v) ? (object?)v : (_inner.TryGetError(out var e) ? e : null);
                    public bool TryGetValue([MaybeNullWhen(false)] out Order v) => _inner.TryGetValue(out v);
                    public bool TryGetValue([MaybeNullWhen(false)] out OrderNotFound e) { if (_inner.TryGetError(out var err) && err is OrderNotFound nf) { e = nf; return true; } e = default; return false; }
                    public bool TryGetValue([MaybeNullWhen(false)] out OrderLocked e) { if (_inner.TryGetError(out var err) && err is OrderLocked l) { e = l; return true; } e = default; return false; }
                }
            """;

        var exhaustive = StandInCore + shapeDef + """
                public static class Exhaustive
                {
                    public static int Status(PlaceOrderResult r) => r switch
                    {
                        Order => 200,
                        OrderNotFound => 404,
                        OrderLocked => 409,
                    };
                }
            }
            """;

        var missingArm = StandInCore + shapeDef + """
                public static class MissingArm
                {
                    public static int Status(PlaceOrderResult r) => r switch
                    {
                        Order => 200,
                        OrderNotFound => 404,
                    };
                }
            }
            """;

        var okDiags = CompilationHarness.Compile(exhaustive);
        Assert.DoesNotContain(okDiags, d => d.Id == "CS8509" || d.Severity == DiagnosticSeverity.Error);

        var warnDiags = CompilationHarness.Compile(missingArm);
        Assert.Contains(warnDiags, d => d.Id == "CS8509");
    }

    [Fact]
    public void Decision16_void_union_shape_requires_Unit_arm_or_warns_CS8509()
    {
        // Doc claim: docs/decisions/0016-source-generated-result-shapes.md (item 8)
        // "Removing the Unit arm of a void shape gives CS8509."
        var shapeDef = """
            namespace Probe
            {
                using System.Diagnostics.CodeAnalysis;
                using System.Runtime.CompilerServices;
                using Resultful.StandIn;

                [Union]
                public readonly struct CancelOrderResult : IUnion
                {
                    private readonly Result<Unit, OrderError> _inner;
                    public CancelOrderResult(Unit value) => _inner = Result<Unit, OrderError>.Ok(value);
                    public CancelOrderResult(OrderNotFound error) => _inner = Result<Unit, OrderError>.Fail(error);
                    public bool HasValue => true;
                    public object? Value => _inner.TryGetValue(out var v) ? (object?)v : (_inner.TryGetError(out var e) ? e : null);
                    public bool TryGetValue(out Unit v) => _inner.TryGetValue(out v);
                    public bool TryGetValue([MaybeNullWhen(false)] out OrderNotFound e) { if (_inner.TryGetError(out var err) && err is OrderNotFound nf) { e = nf; return true; } e = default; return false; }
                }
            """;

        var exhaustive = StandInCore + shapeDef + """
                public static class C
                {
                    public static int Status(CancelOrderResult r) => r switch
                    {
                        Unit => 204,
                        OrderNotFound => 404,
                    };
                }
            }
            """;

        var missingUnit = StandInCore + shapeDef + """
                public static class C
                {
                    public static int Status(CancelOrderResult r) => r switch
                    {
                        OrderNotFound => 404,
                    };
                }
            }
            """;

        Assert.DoesNotContain(CompilationHarness.Compile(exhaustive), d => d.Id == "CS8509" || d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(CompilationHarness.Compile(missingUnit), d => d.Id == "CS8509");
    }

    [Fact]
    public void Decision16_many_errors_union_shape_switches_over_list_and_inner_error_switch_warns_CS8509_without_suppressor()
    {
        // Doc claim: docs/decisions/0016-source-generated-result-shapes.md (item 7)
        // "Outer switch over Success and IReadOnlyList<TError> is exhaustive; inner switch over each error warns CS8509 without suppressor."
        var code = StandInCore + """
            namespace Probe
            {
                using System.Collections.Generic;
                using System.Diagnostics.CodeAnalysis;
                using System.Runtime.CompilerServices;
                using Resultful.StandIn;

                [Union]
                public readonly struct RegisterResult : IUnion
                {
                    private readonly Order? _ok;
                    private readonly IReadOnlyList<OrderError>? _errs;
                    public RegisterResult(Order ok) => _ok = ok;
                    public RegisterResult(IReadOnlyList<OrderError> errs) => _errs = errs;
                    public bool HasValue => _ok is not null || _errs is not null;
                    public object? Value => (object?)_ok ?? _errs;
                    public bool TryGetValue([MaybeNullWhen(false)] out Order v) { v = _ok; return v is not null; }
                    public bool TryGetValue([MaybeNullWhen(false)] out IReadOnlyList<OrderError> e) { e = _errs; return e is not null; }
                }

                public static class C
                {
                    public static int Outer(RegisterResult r) => r switch
                    {
                        Order => 200,
                        IReadOnlyList<OrderError> => 400,
                    };

                    public static int InnerWithoutSuppressor(OrderError e) => e switch
                    {
                        OrderNotFound => 404,
                        OrderLocked => 409,
                    };
                }
            }
            """;

        var diags = CompilationHarness.Compile(code);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        // Only InnerWithoutSuppressor should emit CS8509, not Outer.
        var cs8509 = diags.Where(d => d.Id == "CS8509").ToList();
        Assert.Single(cs8509);
        Assert.Contains("InnerWithoutSuppressor", cs8509[0].Location.SourceTree!.GetText(TestContext.Current.CancellationToken).Lines[cs8509[0].Location.GetLineSpan().StartLinePosition.Line].ToString());
    }
#endif

    [Fact]
    public void Decision15_record_copy_ctor_allows_cross_assembly_derivation_so_analyzer_rule_is_required()
    {
        // Doc claim: docs/decisions/0015-analyzer-rules-read-attributes.md (27613a4)
        // Even with a private protected base constructor, C# synthesizes a protected copy constructor
        // on records, so another assembly can still derive through it; declaring it private protected is CS8878.
        var externalDerived = """
            using Resultful.StandIn;

            namespace ExternalAssembly
            {
                public sealed record Evil : OrderError
                {
                    public Evil(OrderError original) : base(original) { }
                }
            }
            """;

        var crossAssemblyDiags = CompilationHarness.CompileTwoAssemblies(StandInCore, externalDerived);
        Assert.DoesNotContain(crossAssemblyDiags, d => d.Severity == DiagnosticSeverity.Error);

        var privateProtectedCopyCtor = StandInCore + """
            namespace Probe
            {
                using Resultful.StandIn;
                public abstract record ClosedAttempt : Error
                {
                    private protected ClosedAttempt(string message) : base(message) { }
                    private protected ClosedAttempt(ClosedAttempt original) : base(original) { }
                }
            }
            """;

        var cs8878Diags = CompilationHarness.Compile(privateProtectedCopyCtor);
        Assert.Contains(cs8878Diags, d => d.Id == "CS8878" && d.Severity == DiagnosticSeverity.Error);
    }

    // --- Milestone-gated specifications for unbuilt Resultful APIs ---

    [Fact(Skip = "Pending M2: opt-in Resultful.Operators (Decision 9: |, ||, >>, &, named OrElse/ValueOr)")]
    public void M2_opt_in_operators_and_named_equivalents_compile_and_obey_laws()
    {
        // Doc claim: docs/decisions/0009-operators-are-opt-in-sugar.md & docs/roadmap.md (M2)
    }

    [Fact(Skip = "Pending M3: dependency-free core, Error, Validation<T, TError>, Validation.Combine, ValidationFailed<TError>, Result<Unit, TError>, Decision 10 zero-value guard")]
    public void M3_core_error_validation_unit_and_default_guard()
    {
        // Doc claim: docs/decisions/0010-default-is-not-success.md & docs/decisions/0014-result-and-validation.md
    }

    [Fact(Skip = "Pending M4: Resultful.Analyzers ([MustObserve], [ClosedHierarchy] CS8509 suppressor with nullable guard & cross-assembly derivation rule, bare string on Result<string>, default(Result<T>))")]
    public void M4_analyzers_enforce_MustObserve_ClosedHierarchy_and_hazards()
    {
        // Doc claim: docs/decisions/0015-analyzer-rules-read-attributes.md & docs/roadmap.md (M4)
    }

    [Fact(Skip = "Pending M4: Resultful.Generators ([ClosedHierarchy] Match/TryMap, [ResultShape] narrow & ManyErrors shapes, overlapping Success generator error)")]
    public void M4_generators_emit_shapes_and_report_overlapping_Success_error()
    {
        // Doc claim: docs/decisions/0016-source-generated-result-shapes.md (items 3-8)
    }

    [Fact(Skip = "Pending M5: Resultful.AspNetCore ProblemDetails / ValidationFailed 400 mapping and Resultful.Text.Json AOT converters")]
    public void M5_framework_satellites_map_errors_and_preserve_AOT()
    {
        // Doc claim: docs/roadmap.md (M5) & docs/error-shape.md (shape 6)
    }

    [Fact(Skip = "Pending M6: .NET 11 [Union] on core Result<T, TError> / Validation<T, TError> and generated [ResultShape] structs")]
    public void M6_net11_unions_on_core_and_generated_shapes_switch_exhaustively()
    {
        // Doc claim: docs/decisions/0003-unions-are-chosen-by-measurement.md & docs/decisions/0016-source-generated-result-shapes.md (item 9)
    }
}
