using System.Net;
using System.Net.Sockets;
using Resultful.Shop;
using Xunit;

namespace Resultful.Compatibility.Tests;

// The shop sample as a consumer would build it: every Resultful package from the packed folder, outside the
// repository. JIT and native AOT print the same scenario lines, the native binary serves the same problems, and each
// analyzer and the exhaustive switch fail a build that breaks its rule.
public sealed class ShopLegTests(PackedPackages packages)
{
    private const string Sdk = "11.0.100";

    private static readonly string[] Expected =
        [.. new[] { "Open", "Closed", "Shaped" }.SelectMany(style => Scenarios.Expected.Select(line => $"{style} {line}"))];

    [Fact]
    public async Task The_shop_builds_from_the_packages_and_runs_the_same_natively()
    {
        using var leg = Leg.Create(packages, Sdk, Path.Combine("samples", "Resultful.Shop"));
        string[] props = [$"-p:ResultfulVersion={packages.Version}", $"-p:RestorePackagesPath={leg.Packages}"];

        var build = leg.Dotnet(["build", "-c", "Release", .. props]);
        Assert.True(build.ExitCode == 0, build.Output);
        Assert.Contains(" 0 Warning(s)", build.Output, StringComparison.Ordinal);
        var jit = leg.Dotnet(Path.Combine(leg.Root, "bin", "Release", "net11.0", "Resultful.Shop.dll"), "scenarios");
        Assert.True(jit.ExitCode == 0, jit.Output);
        Assert.Equal(Expected, Lines(jit.Output));

        // Trimming and AOT analysis run on the whole app here: no coded warning (IL2xxx, IL3xxx or any other). The
        // native linker's own notes (macOS ld prints "warning: (arm64) skipping ...") have no code and are not checked.
        var native = Path.Combine(leg.Root, "native");
        var publish = leg.Dotnet(["publish", "-c", "Release", "-p:PublishAot=true", "-o", native, .. props]);
        Assert.True(publish.ExitCode == 0, publish.Output);
        Assert.DoesNotMatch(@"warning [A-Z]+\d{4}", publish.Output);
        var shop = Path.Combine(native, OperatingSystem.IsWindows() ? "Resultful.Shop.exe" : "Resultful.Shop");
        var aot = Process.RunFile(leg.Root, shop, "scenarios");
        Assert.True(aot.ExitCode == 0, aot.Output);
        Assert.Equal(Expected, Lines(aot.Output));

        await ServesProblems(shop);

        foreach (var (rule, diagnostic) in new[] { ("Default", "RF0002"), ("Drop", "RF0001"), ("Value", "RF0003"), ("Arm", "CS8509"), ("Inner", "CS8509") })
        {
            var broken = leg.Dotnet(["build", "-c", "Release", $"-p:ShopBreak={rule}", .. props]);
            Assert.True(broken.ExitCode != 0, $"ShopBreak={rule} built:\n{broken.Output}");
            Assert.Contains(diagnostic, broken.Output, StringComparison.Ordinal);
        }
    }

    // The native binary on a free loopback port: a domain error, a validation problem and a success, as JSON.
    private static async Task ServesProblems(string shop)
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        using var server = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(shop, ["--urls", $"http://127.0.0.1:{port}"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            HttpResponseMessage? missing = null;
            for (var attempt = 0; missing is null; attempt++)
            {
                try
                {
                    missing = await client.GetAsync(new Uri("/quote/SKU-99999?qty=4", UriKind.Relative), TestContext.Current.CancellationToken);
                }
                catch (HttpRequestException) when (attempt < 50 && !server.HasExited)
                {
                    await Task.Delay(200, TestContext.Current.CancellationToken);
                }
            }

            using (missing)
            {
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
                Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
                Assert.Contains("\"detail\":\"SKU-99999 is not in the catalog\"", await missing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
            }

            using var order = new StringContent("""{"sku":"SKU-99999","qty":"x","note":"fine"}""", System.Text.Encoding.UTF8, "application/json");
            using var invalid = await client.PostAsync(new Uri("/orders", UriKind.Relative), order, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var problem = await invalid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("\"InvalidField\":[\"qty fails format\"]", problem, StringComparison.Ordinal);
            Assert.Contains("\"SkuNotFound\":[\"SKU-99999 is not in the catalog\"]", problem, StringComparison.Ordinal);

            using var suspended = await client.GetAsync(new Uri("/customers/C-2", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, suspended.StatusCode);
            Assert.Contains("\"detail\":\"customer C-2 is suspended\"", await suspended.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

            using var ok = await client.GetAsync(new Uri("/closed/quote/SKU-00001?qty=4", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("10.00", await ok.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static string[] Lines(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
