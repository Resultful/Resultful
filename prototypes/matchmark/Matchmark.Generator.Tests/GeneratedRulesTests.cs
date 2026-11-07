using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The invariants an overlay declares, enforced by the generated server and checked by the engine: the Bookings sample's
/// generated server, compiled here with a small in-memory handler, run on a real port, and held to the suite generated
/// from the same spec and overlay. Without Matchmark.Rules the generator says the rules aren't enforced (MM025), and the
/// suite finds every request that breaks one accepted.
/// </summary>
[TestFixture]
public sealed class GeneratedRulesTests
{
    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "bookings"));

    private const string Handlers = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Hosting;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.Extensions.DependencyInjection;
        using Resultful;

        namespace Bookings.Api;

        public sealed class Handlers : IBookingsHandlers
        {
            private readonly ConcurrentDictionary<long, Booking> _store = new();
            private long _next;

            public Task<Result<IReadOnlyList<Booking>, ListBookingsError>> ListBookings(int? roomId, int? deskId, int? minMinutes, int? maxMinutes, CancellationToken cancellationToken)
            {
                IReadOnlyList<Booking> found = _store.Values
                    .Where(b => (roomId is null || b.RoomId == roomId) && (deskId is null || b.DeskId == deskId)
                                && (minMinutes is null || (b.EndsAt - b.StartsAt).TotalMinutes >= minMinutes)
                                && (maxMinutes is null || (b.EndsAt - b.StartsAt).TotalMinutes <= maxMinutes))
                    .OrderBy(b => b.Id).ToList();
                return Task.FromResult(Result.Ok<IReadOnlyList<Booking>, ListBookingsError>(found));
            }

            public Task<Result<Booking, CreateBookingError>> CreateBooking(NewBooking body, CancellationToken cancellationToken)
            {
                var id = Interlocked.Increment(ref _next);
                var booking = new Booking { Id = id, StartsAt = body.StartsAt, EndsAt = body.EndsAt, RoomId = body.RoomId, DeskId = body.DeskId, Note = body.Note, Status = "booked" };
                _store[id] = booking;
                return Task.FromResult(Result.Ok<Booking, CreateBookingError>(booking));
            }

            public Task<Result<Booking, GetBookingError>> GetBooking(long bookingId, CancellationToken cancellationToken)
                => Task.FromResult(_store.TryGetValue(bookingId, out var booking)
                    ? Result.Ok<Booking, GetBookingError>(booking)
                    : Result.Fail<Booking, GetBookingError>(GetBookingError.NotFound(new ProblemDetails { Title = "No such booking" })));

            public Task<VoidResult<CancelBookingError>> CancelBooking(long bookingId, CancellationToken cancellationToken)
                => Task.FromResult(_store.TryRemove(bookingId, out _)
                    ? Result.Ok<CancelBookingError>()
                    : Result.Fail<CancelBookingError>(CancelBookingError.NotFound(new ProblemDetails { Title = "No such booking" })));
        }

        public static class Server
        {
            public static async Task<WebApplication> StartAsync()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddBookings();
                builder.Services.AddProblemDetails();
                builder.Services.AddSingleton<IBookingsHandlers, Handlers>();
                var app = builder.Build();
                app.MapBookings();
                await app.StartAsync();
                return app;
            }
        }
        """;

    [Test]
    public async Task The_generated_server_enforces_what_the_overlay_declares_and_the_suite_agrees()
    {
        var (assembly, diagnostics) = Build(withRules: true);
        diagnostics.Should().BeEmpty();

        var report = await Run(assembly);

        report.Failures.Should().BeEmpty(report.ToString());
        report.Cases.Where(c => c.Case.Kind == CaseKind.Invalid).Select(c => c.Case.Name).Should().Contain([
            "listBookings: breaks the dependency ZeroOrOne(roomId, deskId);",
            "listBookings: breaks the dependency minMinutes <= maxMinutes;",
        ]).And.Contain(n => n.Contains("breaking the rule !has(self.roomId) || !has(self.deskId)", StringComparison.Ordinal));
    }

    [Test]
    public async Task Without_Matchmark_Rules_the_rules_are_not_enforced_and_the_suite_says_so()
    {
        var (assembly, diagnostics) = Build(withRules: false);
        diagnostics.Select(d => d.Id).Should().Equal("MM025");

        var report = await Run(assembly);

        report.Failures.Where(f => f.Check == Checks.InvalidRejected).Select(f => f.Subject).Should().Contain([
            "listBookings: breaks the dependency ZeroOrOne(roomId, deskId);",
            "listBookings: breaks the dependency minMinutes <= maxMinutes;",
        ]).And.Contain(s => s.Contains("breaking the rule", StringComparison.Ordinal));
    }

    private static RegressionSuite Suite() => RegressionSuite.FromJson(SpecJson.Normalize(
        File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml", out _,
        [(File.ReadAllText(Path.Combine(Samples, "bookings.overlay.yaml")), "bookings.overlay.yaml")]));

    private static async Task<RegressionReport> Run(Assembly assembly)
    {
        var start = assembly.GetType("Bookings.Api.Server")!.GetMethod("StartAsync")!;
        var app = await (Task<WebApplication>)start.Invoke(null, null)!;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single() + "/") };
            var report = await Suite().RunAsync(client, new RegressionOptions());
            File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bookings-{TestContext.CurrentContext.Test.MethodName}.txt"), report.ToString());
            return report;
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static (Assembly Assembly, IReadOnlyList<Diagnostic> Diagnostics) Build(bool withRules)
        => GeneratedServer.Build("Bookings", Path.Combine(Samples, "openapi.yaml"), [Path.Combine(Samples, "bookings.overlay.yaml")], Handlers, withRules);

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
