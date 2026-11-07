using System.Diagnostics;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using NUnit.Framework;
using Resultful.OpenApi.Scaffold;

namespace Resultful.OpenApi.Generator.Tests;

/// <summary>
/// The scaffolder: the server side of a spec written out as ASP.NET source a team owns. The running behaviour of the
/// scaffolded Pet Store is tested in Resultful.OpenApi.Sample.Tests; these test the scaffolder itself.
/// </summary>
[TestFixture]
public sealed class ScaffoldTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(ThisDirectory(), ".."));
    private static readonly string Spec = Path.Combine(Root, "Resultful.OpenApi.Sample.Contract", "petstore.yaml");
    private static readonly string Committed = Path.Combine(Root, "Resultful.OpenApi.Sample.Scaffolded");

    [Test]
    public void The_committed_scaffold_is_exactly_what_the_scaffolder_writes()
    {
        var files = Scaffold(controllers: true);

        files.Keys.Order(StringComparer.Ordinal).Should().Equal(
            "Controllers/PetsController.cs", "PetStoreConventions.cs", "PetStoreOpenApi.cs", "PetsHandlers.cs", "Program.cs");
        foreach (var (path, content) in files)
        {
            File.ReadAllText(Path.Combine(Committed, path)).Should().Be(content, $"{path} is the scaffolder's untouched output");
        }
    }

    [Test]
    public void Re_running_keeps_files_that_exist_unless_forced()
    {
        var directory = Directory.CreateTempSubdirectory("scaffold-").FullName;
        try
        {
            var files = Scaffold(controllers: true);
            ScaffoldWriter.WriteTo(files, directory, force: false).Should().OnlyContain(r => r.Written);

            File.WriteAllText(Path.Combine(directory, "PetsHandlers.cs"), "// the team's own implementation");
            File.Delete(Path.Combine(directory, "Program.cs"));

            var again = ScaffoldWriter.WriteTo(files, directory, force: false);
            again.Where(r => r.Written).Select(r => r.Path).Should().Equal("Program.cs");
            File.ReadAllText(Path.Combine(directory, "PetsHandlers.cs")).Should().Be("// the team's own implementation");

            ScaffoldWriter.WriteTo(files, directory, force: true).Should().OnlyContain(r => r.Written);
            File.ReadAllText(Path.Combine(directory, "PetsHandlers.cs")).Should().Be(files["PetsHandlers.cs"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void The_minimal_API_style_writes_endpoints_in_place_of_controllers()
    {
        var files = Scaffold(controllers: false);

        files.Keys.Order(StringComparer.Ordinal).Should().Equal("PetStoreEndpoints.cs", "PetStoreOpenApi.cs", "PetsHandlers.cs", "Program.cs");
        files["Program.cs"].Should().Contain("app.MapPetStore();").And.NotContain("AddControllers");
        files["PetStoreEndpoints.cs"].Should().Contain("app.MapMethods(\"/pets/{petId}\"").And.NotContain("auto-generated");
    }

    [Test]
    [Category("Slow")]
    public async Task The_minimal_API_style_builds()
    {
        var directory = Directory.CreateTempSubdirectory("scaffold-minimal-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Scaffolded.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{Path.Combine(Root, "Resultful.OpenApi.Sample.Contract", "Resultful.OpenApi.Sample.Contract.csproj")}" />
                    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.14" />
                  </ItemGroup>
                </Project>
                """);
            ScaffoldWriter.WriteTo(Scaffold(controllers: false), directory, force: false);

            using var build = Process.Start(new ProcessStartInfo(
                "dotnet", ["build", directory, "-nologo", "-v", "quiet", "-nodeReuse:false", "--disable-build-servers"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var stdout = build.StandardOutput.ReadToEndAsync();
            var stderr = build.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            try
            {
                await build.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                build.Kill(entireProcessTree: true);
                throw;
            }

            build.ExitCode.Should().Be(0, await stdout + await stderr);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IReadOnlyDictionary<string, string> Scaffold(bool controllers)
    {
        var files = Scaffolder.Scaffold(File.ReadAllText(Spec), "petstore.yaml", "PetStore.Api", "PetStore.Scaffolded", "PetStore", controllers, out var warnings);
        warnings.Should().BeEmpty();
        return files;
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
