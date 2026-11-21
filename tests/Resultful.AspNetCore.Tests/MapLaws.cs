using CsCheck;
using Microsoft.AspNetCore.Http;
using static Resultful.AspNetCore.Tests.Http;

namespace Resultful.AspNetCore.Tests;

// Laws of the map and the no-leak rule, over generated errors: wrappers, context notes, causes and inner errors.
public class MapLaws
{
    static readonly Gen<string> Text = Gen.String[Gen.Char['!', '~'], 1, 16];
    static readonly Gen<string[]> Notes = Text.Select(t => "secret-" + t).Array[0, 3];

    // An unmapped error with a secret in its message, context and cause.
    static readonly Gen<Error> Unmapped = Gen.Frequency(
        (2, Gen.Select(Text, Notes, (secret, notes) => (Error)new DbTimeout("secret-" + secret) { Context = notes })),
        (2, Gen.Select(Text, Notes, (secret, notes) => (Error)new MessageError("secret-" + secret) { Context = notes })),
        (1, Gen.Select(Gen.Int[0, 999], Text, (id, secret) => (Error)new SaveFailed(id) { Cause = Error.From("secret-" + secret) })));

    // Wraps an error in up to three unmapped layers: as a Cause, inside a BatchFailed, or with a context note.
    static Gen<Error> Wrapped(Error inner) => Gen.Int[0, 2].Array[0, 3].Select(layers => layers.Aggregate(inner, (error, layer) => layer switch
    {
        0 => new SaveFailed(1) { Cause = error },
        1 => new BatchFailed([new DbTimeout("q"), error]),
        _ => error.WithContext("note"),
    }));

    [Fact]
    public Task A_mapped_error_anywhere_under_unmapped_wrappers_decides_the_status() =>
        Gen.Select(Gen.Int[1, 999], Gen.Int[400, 499], Gen.Bool)
            .SelectMany((id, status, locked) => Wrapped(locked ? new OrderLocked(id, "ann") : new OrderNotFound(id)).Select(error => (id, status, locked, error)))
            .SampleAsync(async t =>
            {
                var response = await Execute(Result.Fail<int>(t.error).ToHttpResult(), configure: o =>
                {
                    if (t.locked) o.Map<OrderLocked>(t.status); else o.Map<OrderNotFound>(t.status);
                });
                Assert.Equal(t.status, response.Status);
                Assert.Equal(t.locked ? $"order {t.id} is locked by ann" : $"order {t.id} was not found", response.Get("detail"));
                Assert.DoesNotContain("note", response.Text, StringComparison.Ordinal);
            }, iter: 200);

    [Fact]
    public Task A_base_type_entry_maps_every_leaf_and_a_leaf_entry_overrides_it() =>
        Gen.Select(Gen.Int[0, 2], Gen.Int[1, 999]).SampleAsync(async t =>
        {
            var (leaf, id) = t;
            OrderError error = leaf switch { 0 => new OrderNotFound(id), 1 => new OrderLocked(id, "ann"), _ => new OrderCancelled(id) };
            var response = await Execute(Result.Fail<int, OrderError>(error).ToHttpResult(),
                configure: o => o.Map<OrderError>(StatusCodes.Status422UnprocessableEntity).Map<OrderNotFound>(StatusCodes.Status404NotFound));
            Assert.Equal(leaf == 0 ? 404 : 422, response.Status);
            Assert.Equal(error.Message, response.Get("detail"));
        }, iter: 60);

    [Fact]
    public Task Outside_development_no_5xx_shows_anything_of_the_error() =>
        Gen.Select(Unmapped, Gen.Int[500, 599], Gen.Bool.Select(b => b ? "Production" : "Staging"), Gen.Bool)
            .SampleAsync(async t =>
            {
                var (error, status, environment, viaDefault) = t;
                var response = await Execute(Result.Fail<int>(error).ToHttpResult(), environment, o =>
                {
                    if (viaDefault) o.DefaultStatusCode = status;
                    else o.Map<DbTimeout>(status).Map<MessageError>(status).Map<SaveFailed>(status);
                });
                Assert.Equal(status, response.Status);
                Assert.Null(response.Get("detail"));
                Assert.DoesNotContain("secret-", response.Text, StringComparison.Ordinal);
            }, iter: 200);

    static readonly string[] Fields = ["name", "email", "password"];

    static string Field(Error e) => e switch { Required r => r.Field, TooShort t => t.Field, _ => "" };

    [Fact]
    public Task A_validation_problem_lists_every_error_once_grouped_by_key_in_order() =>
        Gen.Select(Gen.Int[0, 2].Select(i => Fields[i]), Gen.Int[0, 1], (field, kind) => kind == 0 ? (Error)new Required(field) : new TooShort(field, 8))
            .Array[1, 8]
            .SampleAsync(async errors =>
            {
                var response = await Execute(Validation.Fail<int>(errors).ToHttpResult(), configure: ByField);
                Assert.Equal(400, response.Status);
                foreach (var group in errors.GroupBy(Field))
                    Assert.Equal(group.Select(e => e.Message), response.Errors(group.Key));
                Assert.Equal(errors.Length, response.Body.GetProperty("errors").EnumerateObject().Sum(p => p.Value.GetArrayLength()));
            }, iter: 100);
}
