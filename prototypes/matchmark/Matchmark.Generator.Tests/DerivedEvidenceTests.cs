using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// Derived evidence (Diffra's approach to generated assets): for each sample, what the generator decides and writes,
/// rendered for review, never committed. CI runs this on the pull request and on its base, and diffs the two:
/// <c>{name}.model.json</c> (the model: operations, parameters, outcomes), <c>{name}.api.txt</c> (the generated public
/// surface, signatures only: a change here needs review) and <c>{name}.g.cs</c> (every generated source: a change only here
/// is in method bodies). Written to MATCHMARK_DERIVED, or the test's work directory.
/// </summary>
[TestFixture]
public sealed class DerivedEvidenceTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Directory(), ".."));

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Each sample with an OpenAPI spec, with its overlay if it has one, and the Pet Store.</summary>
    public static IEnumerable<string> Samples()
        => System.IO.Directory.GetDirectories(Path.Combine(Root, "samples")).Where(d => File.Exists(Path.Combine(d, "openapi.yaml")))
            .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).Append("petstore");

    internal static (string Spec, string[] Overlays) Paths(string sample)
    {
        if (sample == "petstore")
        {
            return (Path.Combine(Root, "Matchmark.Sample.Contract", "petstore.yaml"), []);
        }

        var folder = Path.Combine(Root, "samples", sample);
        return (Path.Combine(folder, "openapi.yaml"), System.IO.Directory.GetFiles(folder, "*.overlay.yaml").Order(StringComparer.Ordinal).ToArray());
    }

    private static string Output()
    {
        var directory = Environment.GetEnvironmentVariable("MATCHMARK_DERIVED") ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "derived");
        System.IO.Directory.CreateDirectory(directory);
        return directory;
    }

    [TestCaseSource(nameof(Samples))]
    public void Each_sample_s_model_surface_and_source_are_derived(string sample)
    {
        var (spec, overlays) = Paths(sample);
        if (sample == "split")
        {
            // Split across files: derived from its bundle, as the generator reads it from its parts.
            var bundled = Path.Combine(TestContext.CurrentContext.WorkDirectory, "split.bundled.json");
            File.WriteAllText(bundled, Matchmark.Cli.BundleCommand.Bundle(spec).ToJsonString());
            spec = bundled;
        }

        var api = char.ToUpperInvariant(sample[0]) + sample[1..];
        var model = ApiModel.Read(File.ReadAllText(spec), spec, api, api + ".Api", overlays.Select(o => (File.ReadAllText(o), o)))!;
        var (assembly, _, sources) = GeneratedServer.BuildWithSources(api, spec, overlays, string.Empty, withRules: true);

        var directory = Output();
        File.WriteAllText(Path.Combine(directory, $"{sample}.model.json"), JsonSerializer.Serialize(new { model.Operations, model.Diagnostics }, Indented));
        var surface = Surface(assembly);
        File.WriteAllText(Path.Combine(directory, $"{sample}.api.txt"), surface);
        File.WriteAllText(Path.Combine(directory, $"{sample}.g.cs"), string.Concat(sources.Select(s => $"// ===== {s.Hint}\n{s.Text}\n")));

        // What was examined, so an empty derivation can't pass.
        model.Operations.Should().NotBeEmpty();
        surface.Split('\n').Should().HaveCountGreaterThan(10);
        sources.Should().NotBeEmpty();
    }

    /// <summary>The generated public surface, signatures only, in a stable order: types, then each type's members.</summary>
    public static string Surface(Assembly assembly)
    {
        var lines = new StringBuilder();
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var kind = type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct"
                : type.IsAbstract && type.IsSealed ? "static class" : typeof(Delegate).IsAssignableFrom(type) ? "delegate" : "class";
            lines.Append(kind).Append(' ').Append(Name(type));
            if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(Enum) && !typeof(Delegate).IsAssignableFrom(type))
            {
                lines.Append(" : ").Append(Name(baseType));
            }

            lines.Append('\n');
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.Name.StartsWith('<') && m is not MethodInfo { IsSpecialName: true } && !(type.IsEnum && m.Name == "value__"))
                .Select(Member).Order(StringComparer.Ordinal);
            foreach (var member in members)
            {
                lines.Append("  ").Append(member).Append('\n');
            }
        }

        return lines.ToString();
    }

    private static string Member(MemberInfo member) => member switch
    {
        ConstructorInfo c => $"ctor({Parameters(c.GetParameters())})",
        MethodInfo m => $"{(m.IsStatic ? "static " : string.Empty)}{Name(m.ReturnType)} {m.Name}{(m.IsGenericMethod ? $"<{string.Join(", ", m.GetGenericArguments().Select(Name))}>" : string.Empty)}({Parameters(m.GetParameters())})",
        PropertyInfo p => $"{(p.GetMethod?.IsStatic == true ? "static " : string.Empty)}{Name(p.PropertyType)} {p.Name} {{{(p.CanRead ? " get;" : string.Empty)}{(p.SetMethod is { IsPublic: true } set ? (set.ReturnParameter.GetRequiredCustomModifiers().Any(t => t.Name == "IsExternalInit") ? " init;" : " set;") : string.Empty)} }}",
        FieldInfo f => $"{(f.IsStatic ? "static " : string.Empty)}{(f.IsLiteral ? "const " : f.IsInitOnly ? "readonly " : string.Empty)}{Name(f.FieldType)} {f.Name}",
        EventInfo e => $"event {Name(e.EventHandlerType!)} {e.Name}",
        Type nested => $"nested {Name(nested)}",
        _ => member.Name,
    };

    private static string Parameters(ParameterInfo[] parameters) => string.Join(", ", parameters.Select(p => $"{Name(p.ParameterType)} {p.Name}"));

    private static string Name(Type type)
    {
        if (type.IsByRef)
        {
            return "ref " + Name(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            return Name(type.GetElementType()!) + "[]";
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
        return $"{definition[..definition.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
    }

    [Test]
    public void The_surface_is_deterministic_and_names_signatures_only()
    {
        var (spec, overlays) = Paths("petstore");
        var first = Surface(GeneratedServer.Build("PetStore", spec, overlays, string.Empty, withRules: true).Assembly);
        var second = Surface(GeneratedServer.Build("PetStore", spec, overlays, string.Empty, withRules: true).Assembly);
        second.Should().Be(first, "generated code is commit-deterministic: the same input renders the same surface");
        first.Should().Contain("class PetStore.Api.Pet").And.Contain("static class PetStore.Api.PetStoreEndpoints").And.NotContain("=>", "bodies aren't part of the surface");
    }

    private static string Directory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
