using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Matchmark.Testing;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Money, with the Ledger sample: decimal strings, an Idempotency-Key so a retry never pays twice, settlement as a 202 and
/// a transfer to poll, and its overlay's rules, postconditions and transition. The generated server held to its suite.
/// </summary>
[TestFixture]
public sealed class LedgerTests
{
    private static readonly string Samples = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "samples", "ledger"));

    internal const string Source = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Globalization;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Hosting;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.Extensions.DependencyInjection;
        using Resultful;

        namespace Ledger.Api;

        public sealed class Handlers : ILedgerHandlers
        {
            private readonly object _gate = new();
            private readonly Dictionary<long, Account> _accounts = new();
            private readonly Dictionary<long, Transfer> _transfers = new();
            private readonly Dictionary<string, (NewTransfer Body, Transfer Transfer)> _keys = new();
            private long _next;

            private static decimal Amount(string money) => decimal.Parse(money, CultureInfo.InvariantCulture);

            private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

            public Task<Result<Account, OpenAccountError>> OpenAccount(NewAccount body, CancellationToken cancellationToken)
            {
                lock (_gate)
                {
                    var account = new Account { Id = ++_next, Owner = body.Owner, Balance = body.Opening };
                    _accounts[account.Id] = account;
                    return Task.FromResult(Result.Ok<Account, OpenAccountError>(account));
                }
            }

            public Task<Result<Account, GetAccountError>> GetAccount(long accountId, CancellationToken cancellationToken)
            {
                lock (_gate)
                {
                    return Task.FromResult(_accounts.TryGetValue(accountId, out var account)
                        ? Result.Ok<Account, GetAccountError>(account)
                        : Result.Fail<Account, GetAccountError>(GetAccountError.NotFound(new ProblemDetails { Title = "No such account" })));
                }
            }

            public Task<Result<Transfer, TransferError>> Transfer(string idempotencyKey, NewTransfer body, CancellationToken cancellationToken)
            {
                lock (_gate)
                {
                    // The same key and transfer: the same answer, and no second payment. Another transfer: 422.
                    if (_keys.TryGetValue(idempotencyKey, out var seen))
                    {
                        if (seen.Body != body)
                        {
                            return Task.FromResult(Result.Fail<Transfer, TransferError>(TransferError.UnprocessableEntity(new ProblemDetails { Title = "That key was used with another transfer" })));
                        }

                        LedgerHeaders.SetLocation($"/transfers/{seen.Transfer.Id}");
                        return Task.FromResult(Result.Ok<Transfer, TransferError>(seen.Transfer));
                    }

                    if (!_accounts.ContainsKey(body.From) || !_accounts.ContainsKey(body.To))
                    {
                        var missing = !_accounts.ContainsKey(body.From) ? "from" : "to";
                        var problem = new HttpValidationProblemDetails(new Dictionary<string, string[]> { [missing] = ["No such account."] }) { Status = 400 };
                        return Task.FromResult(Result.Fail<Transfer, TransferError>(TransferError.BadRequest(problem)));
                    }

                    if (Amount(_accounts[body.From].Balance) < Amount(body.Amount))
                    {
                        return Task.FromResult(Result.Fail<Transfer, TransferError>(TransferError.Conflict(new ProblemDetails { Title = "The source can't cover it" })));
                    }

                    var transfer = new Transfer { Id = ++_next, From = body.From, To = body.To, Amount = body.Amount, Status = "pending" };
                    _transfers[transfer.Id] = transfer;
                    _keys[idempotencyKey] = (body, transfer);
                    LedgerHeaders.SetLocation($"/transfers/{transfer.Id}");
                    return Task.FromResult(Result.Ok<Transfer, TransferError>(transfer));
                }
            }

            public Task<Result<Transfer, GetTransferError>> GetTransfer(long transferId, CancellationToken cancellationToken)
            {
                lock (_gate)
                {
                    if (!_transfers.TryGetValue(transferId, out var transfer))
                    {
                        return Task.FromResult(Result.Fail<Transfer, GetTransferError>(GetTransferError.NotFound(new ProblemDetails { Title = "No such transfer" })));
                    }

                    // It settles when it's next looked at: the money moves once.
                    if (transfer.Status == "pending")
                    {
                        var source = _accounts[transfer.From];
                        var target = _accounts[transfer.To];
                        _accounts[source.Id] = source with { Balance = Money(Amount(source.Balance) - Amount(transfer.Amount)) };
                        _accounts[target.Id] = target with { Balance = Money(Amount(target.Balance) + Amount(transfer.Amount)) };
                        _transfers[transferId] = transfer = transfer with { Status = "settled" };
                    }

                    return Task.FromResult(Result.Ok<Transfer, GetTransferError>(transfer));
                }
            }
        }

        public static class Server
        {
            public static async Task<WebApplication> StartAsync()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddLedger();
                builder.Services.AddProblemDetails();
                builder.Services.AddSingleton<ILedgerHandlers, Handlers>();
                var app = builder.Build();
                app.MapLedger();
                await app.StartAsync();
                return app;
            }
        }
        """;

    [Test]
    public async Task Money_moves_once_settles_once_and_keeps_to_the_overlay()
    {
        var (assembly, diagnostics) = GeneratedServer.Build("Ledger", Path.Combine(Samples, "openapi.yaml"), [Path.Combine(Samples, "ledger.overlay.yaml")], Source, withRules: true);
        diagnostics.Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).Should().BeEmpty();
        var app = await (Task<WebApplication>)assembly.GetType("Ledger.Api.Server")!.GetMethod("StartAsync")!.Invoke(null, null)!;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single() + "/") };
            var spec = SpecJson.Normalize(File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml", out _,
                [(File.ReadAllText(Path.Combine(Samples, "ledger.overlay.yaml")), "ledger.overlay.yaml")]);

            // Two accounts first, so the transfer's example (from 1 to 2) has somewhere to go.
            await client.PostAsync("accounts", new StringContent("""{"owner":"Ada","opening":"500.00"}""", System.Text.Encoding.UTF8, "application/json"));
            await client.PostAsync("accounts", new StringContent("""{"owner":"Bo","opening":"0.00"}""", System.Text.Encoding.UTF8, "application/json"));
            var report = await RegressionSuite.FromJson(spec).RunAsync(client, new RegressionOptions { MaxRetryWait = TimeSpan.Zero });

            File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "ledger.txt"), report.ToString());
            report.Failures.Should().BeEmpty(report.ToString());
            report.Cases.Should().Contain(c => c.Case.Operation.Id == "transfer" && c.Exchange.Status == 202 && c.Exchange.Headers.ContainsKey("Location"));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    /// <summary>The conserved total is proven on the real Ledger: settlement that debits without crediting is caught.</summary>
    [Test]
    public async Task Settlement_that_loses_money_breaks_the_conserved_total()
    {
        const string Credit = "_accounts[target.Id] = target with { Balance = Money(Amount(target.Balance) + Amount(transfer.Amount)) };";
        Source.Should().Contain(Credit);
        var (assembly, _) = GeneratedServer.Build("Ledger", Path.Combine(Samples, "openapi.yaml"), [Path.Combine(Samples, "ledger.overlay.yaml")],
            Source.Replace(Credit, string.Empty, StringComparison.Ordinal), withRules: true);
        var app = await (Task<WebApplication>)assembly.GetType("Ledger.Api.Server")!.GetMethod("StartAsync")!.Invoke(null, null)!;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single() + "/") };
            var spec = SpecJson.Normalize(File.ReadAllText(Path.Combine(Samples, "openapi.yaml")), "openapi.yaml", out _,
                [(File.ReadAllText(Path.Combine(Samples, "ledger.overlay.yaml")), "ledger.overlay.yaml")]);
            await client.PostAsync("accounts", new StringContent("""{"owner":"Ada","opening":"500.00"}""", System.Text.Encoding.UTF8, "application/json"));
            await client.PostAsync("accounts", new StringContent("""{"owner":"Bo","opening":"0.00"}""", System.Text.Encoding.UTF8, "application/json"));
            var report = await RegressionSuite.FromJson(spec).RunAsync(client, new RegressionOptions { MaxRetryWait = TimeSpan.Zero });

            report.Failures.Should().Contain(f => f.Check == Checks.TotalConserved && f.Message.Contains("changed the total of Account.balance", StringComparison.Ordinal) && f.Subject.Contains("getTransfer", StringComparison.Ordinal), report.ToString());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
