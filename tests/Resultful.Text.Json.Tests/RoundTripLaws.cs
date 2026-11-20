using System.Text.Json;
using CsCheck;

namespace Resultful.Text.Json.Tests;

// Round-trip laws, for every core type, under the invariant culture and tr-TR, with and without a naming policy:
// read(write(x)) == x, and write(read(write(x))) == write(x).
public class RoundTripLaws
{
    public static TheoryData<string, string> Cultures() => new() { { "", "web" }, { "tr-TR", "web" }, { "", "plain" }, { "tr-TR", "plain" } };

    private static readonly Gen<string> Text = Gen.String[Gen.Char[' ', 'ɏ'], 0, 12];
    private static readonly Gen<string[]> Notes = Text.Array[0, 3];
    private static readonly Gen<Order> Orders = Gen.Select(Gen.Int, Text, (id, name) => new Order(id, name));

    private static Gen<PaymentError> Payment(int depth) =>
        Gen.Frequency(
            (2, Gen.Select(Text, Notes, Cause(depth), (reason, notes, cause) => (PaymentError)new CardDeclined(reason) { Context = notes, Cause = cause })),
            (1, Gen.Select(Gen.Date, Notes, (date, notes) => (PaymentError)new CardExpired(DateOnly.FromDateTime(date)) { Context = notes })));

    private static Gen<Error> Errors(int depth)
    {
        var leaves = new List<(int, IGen<Error>)>
        {
            (3, Gen.Select(Text, Notes, Cause(depth), (m, notes, cause) => (Error)new MessageError(m) { Context = notes, Cause = cause })),
            (2, Gen.Select(Gen.Int, Notes, Cause(depth), (id, notes, cause) => (Error)new OrderNotFound(id) { Context = notes, Cause = cause })),
            (2, Gen.Select(Gen.Int, Text, Notes, (id, by, notes) => (Error)new OrderLocked(id, by) { Context = notes })),
            (2, Gen.Select(Gen.Decimal, Notes, (price, notes) => (Error)new InvalidItem(price) { Context = notes })),
            (1, Payment(depth).Select(p => (Error)p)),
        };
        if (depth > 0) leaves.Add((1, Errors(depth - 1).Array[1, 3].Select(list => (Error)new ValidationFailed<Error>(list))));
        return Gen.Frequency([.. leaves]);
    }

    private static Gen<Error?> Cause(int depth) =>
        depth <= 0 ? Gen.Const((Error?)null) : Gen.Frequency((2, Gen.Const((Error?)null)), (1, Errors(depth - 1).Select(e => (Error?)e)));

    private static readonly Gen<Error> AnyError = Errors(2);

    private static JsonSerializerOptions OptionsFor(string naming) => naming == "web" ? Wire.Web() : Wire.Plain();

    private static void Law<T>(Gen<T> gen, string culture, string naming) => Wire.InCulture(culture, () =>
    {
        var options = OptionsFor(naming);
        gen.Sample(value =>
        {
            var json = JsonSerializer.Serialize(value, options);
            var back = JsonSerializer.Deserialize<T>(json, options);
            Assert.Equal(value, back);
            Assert.Equal(json, JsonSerializer.Serialize(back, options));
        }, iter: 300);
    });

    private static Gen<Result<T>> Results<T>(Gen<T> values) =>
        Gen.Bool.SelectMany(ok => ok ? values.Select(Result.Ok) : AnyError.Select(Result.Fail<T>));

    [Theory, MemberData(nameof(Cultures))]
    public void Result_of_int(string culture, string naming) => Law(Results(Gen.Int), culture, naming);

    [Theory, MemberData(nameof(Cultures))]
    public void Result_of_a_record(string culture, string naming) => Law(Results(Orders), culture, naming);

    [Theory, MemberData(nameof(Cultures))]
    public void Result_of_decimal(string culture, string naming) => Law(Results(Gen.Decimal), culture, naming);

    [Theory, MemberData(nameof(Cultures))]
    public void Result_of_Unit(string culture, string naming) =>
        Law(Gen.Bool.SelectMany(ok => ok ? Gen.Const(Result.Ok()) : AnyError.Select(Result.Fail<Unit>)), culture, naming);

    [Theory, MemberData(nameof(Cultures))]
    public void Result_with_a_hierarchy_error(string culture, string naming) =>
        Law(Gen.Bool.SelectMany(ok => ok ? Text.Select(Result.Ok<string, PaymentError>) : Payment(2).Select(Result.Fail<string, PaymentError>)), culture, naming);

    [Theory, MemberData(nameof(Cultures))]
    public void Result_with_Error_as_TError(string culture, string naming) =>
        Law(Gen.Bool.SelectMany(ok => ok ? Gen.Int.Select(Result.Ok<int, Error>) : AnyError.Select(Result.Fail<int, Error>)), culture, naming);

    [Theory, MemberData(nameof(Cultures))]
    public void Result_with_an_error_that_isnt_an_Error(string culture, string naming) =>
        Law(Gen.Bool.SelectMany(ok => ok ? Gen.Int.Select(Result.Ok<int, string>) : Text.Select(Result.Fail<int, string>)), culture, naming);

    [Theory, MemberData(nameof(Cultures))]
    public void Validation_of_a_record(string culture, string naming) =>
        Law(Gen.Bool.SelectMany(ok => ok ? Orders.Select(Validation.Ok) : AnyError.Array[1, 4].Select(e => Validation.Fail<Order>(e))), culture, naming);

    [Theory, MemberData(nameof(Cultures))]
    public void Validation_with_a_hierarchy_error(string culture, string naming) =>
        Law(Gen.Bool.SelectMany(ok => ok ? Gen.Int.Select(Validation.Ok<int, PaymentError>) : Payment(1).Array[1, 4].Select(e => Validation.Fail<int, PaymentError>(e))), culture, naming);

    [Theory, MemberData(nameof(Cultures))]
    public void An_Error_on_its_own(string culture, string naming) => Law(AnyError, culture, naming);

    [Theory]
    [InlineData("")]
    [InlineData("tr-TR")]
    public void Without_the_factory_the_converters_alone_round_trip(string culture) => Wire.InCulture(culture, () =>
    {
        // The AOT path: no AddResultful (it needs dynamic code), only the converters and the registered cases.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new ResultJsonConverter<Order>(), new ValidationJsonConverter<int, PaymentError>(), new ResultJsonConverter<Unit>() },
        }.AddErrorCases<Error>(Wire.AllLeaves).AddErrorCases<PaymentError>(PaymentError.Cases);
        Results(Orders).Sample(r => Assert.Equal(r, JsonSerializer.Deserialize<Result<Order>>(JsonSerializer.Serialize(r, options), options)), iter: 300);
        Payment(1).Array[1, 3].Select(e => Validation.Fail<int, PaymentError>(e))
            .Sample(v => Assert.Equal(v, JsonSerializer.Deserialize<Validation<int, PaymentError>>(JsonSerializer.Serialize(v, options), options)), iter: 100);
        Assert.Equal("""{"ok":true,"value":{}}""", JsonSerializer.Serialize(Result.Ok(), options));
    });
}
