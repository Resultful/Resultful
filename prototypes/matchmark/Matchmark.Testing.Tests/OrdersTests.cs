using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Matchmark.Generator;
using NUnit.Framework;

namespace Matchmark.Testing.Tests;

/// <summary>
/// The behaviour the Orders sample declares in standard HTTP, checked by the engine: idempotency keys, conditional
/// changes, paging, work that finishes later, rate limits and deprecation, plus its Arazzo checkout. Against an Orders
/// API in memory that does all of it right, and then wrong in one way at a time.
/// </summary>
[TestFixture]
public sealed class OrdersTests
{
    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "orders"));

    private static RegressionSuite Suite() => RegressionSuite.FromJson(
        SpecJson.Normalize(File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml"),
        SpecJson.Arazzo(File.ReadAllText(Path.Combine(Samples, "orders.arazzo.yaml")), "orders.arazzo.yaml"));

    private static readonly string[] Behaviour =
    [
        Checks.IdempotentReplay, Checks.IdempotencyKeyReuse, Checks.ETagChanges, Checks.StaleIfMatchRefused, Checks.PagesComplete,
        Checks.OperationCompletes, Checks.RetryAfterGiven, Checks.DeprecationAnnounced, Checks.WorkflowCriteria,
    ];

    [Test]
    public async Task An_API_that_does_it_all_right_passes_everything()
    {
        var report = await Run(fault: null);

        report.Failures.Should().BeEmpty(report.ToString());
        report.Workflows.Select(w => $"{w.Name}: {w.Skipped ?? "ran"}").Should().Contain([
            "Orders checkout: checkout: ran",
            "paging listOrders: ran",
            "createOrder 201, then getOrder, updateOrder, payOrder: ran",
        ]);
        report.Workflows.Single(w => w.Name == "Orders checkout: checkout").Exchanges.Select(e => $"{e.Request.Method} {e.Status}").Should().Equal(
            "POST 201", "POST 201", "PATCH 200", "PATCH 412", "POST 202", "GET 200", "GET 200", "GET 200", "GET 200", "GET 200");
    }

    [TestCase("does the work again when a request is retried with its key", Checks.IdempotentReplay)]
    [TestCase("applies a key reused with a different body", Checks.IdempotencyKeyReuse)]
    [TestCase("keeps the same ETag after a change", Checks.ETagChanges)]
    [TestCase("ignores If-Match", Checks.StaleIfMatchRefused)]
    [TestCase("repeats a page", Checks.PagesComplete)]
    [TestCase("loses the last page", Checks.PagesComplete)]
    [TestCase("never finishes a payment", Checks.OperationCompletes)]
    [TestCase("limits requests without saying when to retry", Checks.RetryAfterGiven)]
    [TestCase("forgets to announce the deprecation", Checks.DeprecationAnnounced)]
    public async Task Each_behaviour_catches_its_regression(string fault, string check)
    {
        var report = await Run(fault);

        report.Failures.Select(f => f.Check).Where(Behaviour.Contains).Distinct().Should().Contain(check, report.ToString());
    }

    private static async Task<RegressionReport> Run(string? fault)
    {
        using var client = new HttpClient(new OrdersApi(fault)) { BaseAddress = new Uri("http://orders/") };
        var options = new RegressionOptions { MaxRetryWait = TimeSpan.Zero, MaxPolls = 5 };
        var report = await Suite().RunAsync(client, options);
        File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"orders-{fault ?? "right"}.txt".Replace(' ', '-')), report.ToString());
        return report;
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    /// <summary>The Orders API, in memory: right by default, or wrong in the one way <paramref name="fault"/> says.</summary>
    private sealed class OrdersApi(string? fault) : HttpMessageHandler
    {
        private readonly List<(Guid Id, JsonObject Order, int Version)> _orders = [];
        private readonly Dictionary<string, (string Body, int Status, string Response, string Id)> _keys = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, (Guid Order, int Polls)> _operations = [];
        private int _requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests++;
            var path = request.RequestUri!.AbsolutePath;
            var segments = path.Trim('/').Split('/');
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            // Rate limited now and then: the first list of all, with a Retry-After (or, broken, without one).
            if (path == "/orders" && request.Method == HttpMethod.Get && _requests == 1)
            {
                var limited = Problem(HttpStatusCode.TooManyRequests, "Slow down");
                if (fault != "limits requests without saying when to retry")
                {
                    limited.Headers.Add("Retry-After", "0");
                }

                return limited;
            }

            return (request.Method.Method, segments) switch
            {
                ("GET", ["orders"]) => List(request),
                ("POST", ["orders"]) => Create(request, body),
                ("GET", ["orders", var id]) => Find(id) is { } o ? Order(HttpStatusCode.OK, o) : NotFound(),
                ("PATCH", ["orders", var id]) => Update(request, id, body),
                ("POST", ["orders", var id, "payments"]) => Pay(id),
                ("GET", ["orders", var id, "receipt"]) => Receipt(id),
                ("GET", ["operations", var id]) => Poll(id),
                _ => NotFound(),
            };
        }

        private HttpResponseMessage List(HttpRequestMessage request)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var limit = 20;
            if (query["limit"] is { } l && (!int.TryParse(l, out limit) || limit is < 1 or > 100))
            {
                return Invalid("limit", "1 to 100.");
            }

            var start = 0;
            if (query["cursor"] is { } c && (!c.StartsWith('c') || !int.TryParse(c[1..], out start) || start > _orders.Count))
            {
                return Invalid("cursor", "Not a cursor this API gave.");
            }

            var end = Math.Min(start + limit, _orders.Count);
            var page = new JsonArray(_orders.Skip(start).Take(end - start).Select(o => (JsonNode)View(o)).ToArray());
            var nextStart = fault == "repeats a page" ? start : end;
            var next = end < _orders.Count && !(fault == "loses the last page" && end + limit >= _orders.Count) ? $"c{nextStart}" : null;
            var response = Json(HttpStatusCode.OK, new JsonObject { ["items"] = page, ["next"] = next });
            response.Headers.Add("RateLimit", "\"default\";r=99;t=60");
            return response;
        }

        private HttpResponseMessage Create(HttpRequestMessage request, string? body)
        {
            var key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.First() : null;
            if (key is null or { Length: 0 or > 255 })
            {
                return Invalid("Idempotency-Key", "A key of 1 to 255 characters is required.");
            }

            if (_keys.TryGetValue(key, out var seen) && fault != "does the work again when a request is retried with its key")
            {
                return seen.Body == body || fault == "applies a key reused with a different body"
                    ? Order(HttpStatusCode.Created, _orders.First(o => o.Id.ToString() == seen.Id))
                    : Problem((HttpStatusCode)422, "That key was used with a different body");
            }

            if (Validate(request, body, isNew: true) is { } invalid)
            {
                return invalid;
            }

            var order = JsonNode.Parse(body!)!.AsObject();
            var items = order["items"]!.AsArray();
            var record = (Id: Guid.NewGuid(), Order: new JsonObject
            {
                ["status"] = "pending",
                ["items"] = items.DeepClone(),
                ["total"] = items.Sum(i => i!["quantity"]!.GetValue<int>() * 2.5m),
                ["note"] = order["note"]?.DeepClone(),
            }, Version: 1);
            _orders.Add(record);
            _keys[key] = (body!, 201, string.Empty, record.Id.ToString());
            return Order(HttpStatusCode.Created, record);
        }

        private HttpResponseMessage Update(HttpRequestMessage request, string id, string? body)
        {
            if (Find(id) is not { } record)
            {
                return NotFound();
            }

            if (!request.Headers.TryGetValues("If-Match", out var match))
            {
                return Problem((HttpStatusCode)428, "Send If-Match");
            }

            if (match.First() != ETag(record) && fault != "ignores If-Match")
            {
                return Problem(HttpStatusCode.PreconditionFailed, "The order changed since that ETag");
            }

            if (Validate(request, body, isNew: false) is { } invalid)
            {
                return invalid;
            }

            var patch = JsonNode.Parse(body!)!.AsObject();
            if (patch.ContainsKey("note"))
            {
                record.Order["note"] = patch["note"]?.DeepClone();
            }

            var index = _orders.FindIndex(o => o.Id == record.Id);
            _orders[index] = record with { Version = fault == "keeps the same ETag after a change" ? record.Version : record.Version + 1 };
            return Order(HttpStatusCode.OK, _orders[index]);
        }

        private HttpResponseMessage Pay(string id)
        {
            if (Find(id) is not { } record)
            {
                return NotFound();
            }

            var operation = Guid.NewGuid();
            _operations[operation] = (record.Id, 0);
            var accepted = Json(HttpStatusCode.Accepted, new JsonObject { ["id"] = operation.ToString(), ["status"] = "running" });
            accepted.Headers.Location = new Uri($"/operations/{operation}", UriKind.Relative);
            return accepted;
        }

        private HttpResponseMessage Poll(string id)
        {
            if (!Guid.TryParse(id, out var operation) || !_operations.TryGetValue(operation, out var state))
            {
                return NotFound();
            }

            _operations[operation] = state with { Polls = state.Polls + 1 };
            var done = state.Polls >= 2 && fault != "never finishes a payment";
            if (done)
            {
                var index = _orders.FindIndex(o => o.Id == state.Order);
                _orders[index].Order["status"] = "paid";
                _orders[index] = _orders[index] with { Version = _orders[index].Version + 1 };
            }

            var response = Json(HttpStatusCode.OK, new JsonObject { ["id"] = id, ["status"] = done ? "succeeded" : "running" });
            if (!done)
            {
                response.Headers.Add("Retry-After", "0");
            }

            return response;
        }

        private HttpResponseMessage Receipt(string id)
        {
            if (Find(id) is not { } record)
            {
                return NotFound();
            }

            var receipt = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"Order {record.Id}: {record.Order["total"]}", Encoding.UTF8, "text/plain") };
            if (fault != "forgets to announce the deprecation")
            {
                receipt.Headers.Add("Deprecation", "@1767225600");
                receipt.Headers.Add("Sunset", "Thu, 31 Dec 2026 23:59:59 GMT");
            }

            return receipt;
        }

        /// <summary>The body's schema, checked: what's wrong is named at its path, as the spec's validation problem.</summary>
        private static HttpResponseMessage? Validate(HttpRequestMessage request, string? body, bool isNew)
        {
            var type = request.Content?.Headers.ContentType?.MediaType;
            if (type != (isNew ? "application/json" : "application/merge-patch+json"))
            {
                return Problem(HttpStatusCode.UnsupportedMediaType, "Unsupported media type");
            }

            JsonNode? json;
            try
            {
                json = JsonNode.Parse(body ?? string.Empty);
            }
            catch (JsonException)
            {
                return Invalid("body", "Not JSON.");
            }

            if (json is not JsonObject order)
            {
                return Invalid("body", "An object is required.");
            }

            if (order.ContainsKey("note") && order["note"] is not null && (order["note"]!.GetValueKind() != JsonValueKind.String || order["note"]!.GetValue<string>().Length > 500))
            {
                return Invalid("note", "Up to 500 characters, or null.");
            }

            if (!isNew)
            {
                return null;
            }

            if (order["items"] is not JsonArray items || items.Count is < 1 or > 20)
            {
                return Invalid("items", "1 to 20 lines.");
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not JsonObject line)
                {
                    return Invalid($"items[{i}]", "A line is an object.");
                }

                if (line["sku"] is not JsonValue sku || sku.GetValueKind() != JsonValueKind.String || !Regex.IsMatch(sku.GetValue<string>(), "^[A-Z]{3}-[0-9]{3}$"))
                {
                    return Invalid($"items[{i}].sku", "Like TEA-001.");
                }

                if (line["quantity"] is not JsonValue quantity || quantity.GetValueKind() != JsonValueKind.Number
                    || !decimal.TryParse(quantity.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture, out var q) || q % 1 != 0 || q is < 1 or > 99)
                {
                    return Invalid($"items[{i}].quantity", "A whole number from 1 to 99.");
                }
            }

            return null;
        }

        private (Guid Id, JsonObject Order, int Version)? Find(string id)
            => Guid.TryParse(id, out var guid) && _orders.FindIndex(o => o.Id == guid) is var i and >= 0 ? _orders[i] : null;

        private static string ETag((Guid Id, JsonObject Order, int Version) record) => $"\"v{record.Version}\"";

        private static JsonObject View((Guid Id, JsonObject Order, int Version) record)
        {
            var view = new JsonObject { ["id"] = record.Id.ToString() };
            foreach (var (key, value) in record.Order)
            {
                view[key] = value?.DeepClone();
            }

            return view;
        }

        private static HttpResponseMessage Order(HttpStatusCode status, (Guid Id, JsonObject Order, int Version) record)
        {
            var response = Json(status, View(record));
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(ETag(record));
            if (status == HttpStatusCode.Created)
            {
                response.Headers.Location = new Uri($"/orders/{record.Id}", UriKind.Relative);
            }

            return response;
        }

        private static HttpResponseMessage NotFound() => Problem(HttpStatusCode.NotFound, "No such order or operation");

        private static HttpResponseMessage Invalid(string key, string message)
            => Json(HttpStatusCode.BadRequest, new JsonObject { ["title"] = "Invalid", ["status"] = 400, ["errors"] = new JsonObject { [key] = new JsonArray(message) } }, "application/problem+json");

        private static HttpResponseMessage Problem(HttpStatusCode status, string title)
            => Json(status, new JsonObject { ["title"] = title, ["status"] = (int)status }, "application/problem+json");

        private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body, string type = "application/json")
            => new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, type) };
    }
}
