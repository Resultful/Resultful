using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Resultful.Generators.Tests;

public sealed class BudgetAndIlTests
{
    private sealed record FormulaBudget(int MemberBase, int MemberPerLeaf, int ParamBase, int ParamPerLeaf);

    [Fact]
    public void Emitted_API_and_IL_sizes_stay_within_budgets_with_zero_unexpected_boxing()
    {
        var budgetPath = Path.Combine(GeneratorTestHarness.RepoRootDir, "src", "Resultful.Generators", "api", "emitted-api-budgets.txt");
        Assert.True(File.Exists(budgetPath), $"Budget file not found at {budgetPath}");
        var (budgets, ilForwardMax, ilDispatchBase, ilDispatchPerLeaf) = ParseBudgetFile(File.ReadAllLines(budgetPath));

        var fixtures = new (string Name, string Source, string Category, string TypeFullName, int LeafCount)[]
        {
            ("FlatNarrow", FixtureCatalog.FlatHierarchy, IsNet11 ? "narrow_net11" : "narrow_net10", "Fixture.Flat.PlaceOrderResult", 2),
            ("DeepNarrow", FixtureCatalog.DeepFourLevelHierarchy, IsNet11 ? "narrow_net11" : "narrow_net10", "Fixture.Deep.CheckoutResult", 4),
            ("Valueless", FixtureCatalog.ValuelessShape, IsNet11 ? "valueless_net11" : "valueless_net10", "Fixture.Void.CancelOrderResult", 2),
            ("ManyErrors", FixtureCatalog.ManyErrorsShape, "many_errors", "Fixture.Many.RegisterResult", 2),
            ("ClosedBase", FixtureCatalog.FlatHierarchy, "closed_hierarchy", "Fixture.Flat.OrderError", 2),
        };

        var checkedShapes = 0;

        foreach (var (name, src, category, typeFullName, leafCount) in fixtures)
        {
            var comp = GeneratorTestHarness.CreateCompilation([(name + ".cs", src)], assemblyName: "Budget_" + name);
            var output = GeneratorTestHarness.Run(comp, out _);
            Assert.DoesNotContain(output.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);

            var typeSymbol = output.OutputCompilation.GetTypeByMetadataName(typeFullName);
            Assert.NotNull(typeSymbol);

            // Count generated public members and max parameters
            var generatedTrees = output.RunResult.GeneratedTrees.ToHashSet();
            var generatedPublicMembers = typeSymbol!.GetMembers()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public &&
                            !m.IsImplicitlyDeclared &&
                            m.DeclaringSyntaxReferences.Any(r => generatedTrees.Contains(r.SyntaxTree)) &&
                            m is not IMethodSymbol { AssociatedSymbol: not null })
                .ToList();

            var memberCount = generatedPublicMembers.Count;
            var maxParams = generatedPublicMembers
                .OfType<IMethodSymbol>()
                .Select(m => m.Parameters.Length)
                .DefaultIfEmpty(0)
                .Max();

            var budget = budgets[category];
            var maxMembers = budget.MemberBase + (budget.MemberPerLeaf * leafCount);
            var maxParamBudget = budget.ParamBase + (budget.ParamPerLeaf * leafCount);

            Assert.True(
                memberCount <= maxMembers,
                $"{name} ({category}, L={leafCount}): public members {memberCount} > budget {maxMembers}");
            Assert.True(
                maxParams <= maxParamBudget,
                $"{name} ({category}, L={leafCount}): max parameters {maxParams} > budget {maxParamBudget}");

            // Inspect compiled IL bytes and box opcodes via System.Reflection.Metadata
            var peBytes = GeneratorTestHarness.EmitAssemblyBytes(output.OutputCompilation);
            VerifyIlForType(peBytes, typeFullName, generatedPublicMembers, leafCount, ilForwardMax, ilDispatchBase, ilDispatchPerLeaf);
            checkedShapes++;
        }

        // Fail closed positive control
        Assert.True(checkedShapes > 0, "Budget gate checked 0 shapes.");
    }

    private static bool IsNet11 =>
#if NET11_0_OR_GREATER
        true;
#else
        false;
#endif

    private static void VerifyIlForType(
        byte[] peBytes,
        string typeFullName,
        List<ISymbol> generatedMembers,
        int leafCount,
        int ilForwardMax,
        int ilDispatchBase,
        int ilDispatchPerLeaf)
    {
        var generatedNames = new HashSet<string>(
            generatedMembers.Select(m => m is IPropertySymbol p ? "get_" + p.Name : m.Name),
            StringComparer.Ordinal);

        using var peReader = new PEReader(new MemoryStream(peBytes));
        var md = peReader.GetMetadataReader();

        var inspectedMethods = 0;
        foreach (var typeHandle in md.TypeDefinitions)
        {
            var typeDef = md.GetTypeDefinition(typeHandle);
            var ns = md.GetString(typeDef.Namespace);
            var name = md.GetString(typeDef.Name);
            var full = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            if (!string.Equals(full, typeFullName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var methodDef = md.GetMethodDefinition(methodHandle);
                var methodName = md.GetString(methodDef.Name);
                if (!generatedNames.Contains(methodName) || methodDef.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                var body = peReader.GetMethodBody(methodDef.RelativeVirtualAddress);
                var il = body.GetILBytes()!;
                var isDispatch = methodName is "Match" or "Switch" or "TryMap";
                var limit = isDispatch ? ilDispatchBase + (ilDispatchPerLeaf * leafCount) : ilForwardMax;

                // Value property (IUnion.Value) intentionally boxes value-type T to object?
                if (methodName != "get_Value")
                {
                    Assert.True(
                        il.Length <= limit,
                        $"{typeFullName}.{methodName}: IL size {il.Length} bytes > budget {limit} bytes");
                    Assert.False(
                        ContainsBoxOpcode(il),
                        $"{typeFullName}.{methodName} contains a 'box' IL instruction.");
                }

                inspectedMethods++;
            }
        }

        Assert.True(inspectedMethods > 0, $"No compiled IL methods inspected for {typeFullName}.");
    }

    private static bool ContainsBoxOpcode(byte[] il)
    {
        // Minimal IL scanner checking for opcode 0x8C (box) at instruction boundaries
        var i = 0;
        while (i < il.Length)
        {
            var op = il[i++];
            if (op == 0x8C)
            {
                return true;
            }

            if (op == 0xFE && i < il.Length)
            {
                var op2 = il[i++];
                i += TwoByteOperandSize(op2);
            }
            else if (op == 0x45) // switch
            {
                var count = BitConverter.ToInt32(il, i);
                i += 4 + (count * 4);
            }
            else
            {
                i += SingleByteOperandSize(op);
            }
        }

        return false;
    }

    private static int SingleByteOperandSize(byte op) => op switch
    {
        <= 0x0D => 0,
        <= 0x13 => 1,
        <= 0x1E => 0,
        0x1F => 1,
        0x20 => 4,
        0x21 => 8,
        0x22 => 4,
        0x23 => 8,
        0x25 or 0x26 or 0x2A => 0,
        0x27 or 0x28 or 0x29 => 4,
        >= 0x2B and <= 0x37 => 1,
        >= 0x38 and <= 0x44 => 4,
        >= 0x46 and <= 0x6E => 0,
        >= 0x6F and <= 0x75 => 4,
        0x76 => 0,
        >= 0x79 and <= 0x81 => 4,
        >= 0x82 and <= 0x8B => 0,
        0x8C or 0x8D => 4,
        0x8E => 0,
        0x8F => 4,
        >= 0x90 and <= 0xA2 => 0,
        >= 0xA3 and <= 0xA5 => 4,
        >= 0xB3 and <= 0xC6 => 0,
        0xD0 => 4,
        >= 0xD1 and <= 0xDD => 0,
        0xDE => 1,
        _ => 0,
    };

    private static int TwoByteOperandSize(byte op2) => op2 switch
    {
        0x06 or 0x07 or 0x15 or 0x16 or 0x1C => 4,
        >= 0x09 and <= 0x0E => 2,
        0x12 => 1,
        _ => 0,
    };

    private static (Dictionary<string, FormulaBudget> Budgets, int IlForwardMax, int IlDispatchBase, int IlDispatchPerLeaf) ParseBudgetFile(string[] lines)
    {
        var map = new Dictionary<string, FormulaBudget>(StringComparer.Ordinal);
        int ilFwd = 32, ilDispBase = 32, ilDispPerLeaf = 24;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("IL_FORWARDING_MAX=", StringComparison.Ordinal))
            {
                ilFwd = int.Parse(line["IL_FORWARDING_MAX=".Length..], CultureInfo.InvariantCulture);
                continue;
            }

            if (line.StartsWith("IL_DISPATCH_BASE=", StringComparison.Ordinal))
            {
                ilDispBase = int.Parse(line["IL_DISPATCH_BASE=".Length..], CultureInfo.InvariantCulture);
                continue;
            }

            if (line.StartsWith("IL_DISPATCH_PER_LEAF=", StringComparison.Ordinal))
            {
                ilDispPerLeaf = int.Parse(line["IL_DISPATCH_PER_LEAF=".Length..], CultureInfo.InvariantCulture);
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cat = parts[0];
            var (mb, ml) = ParseFormula(parts[1]["MEMBERS=".Length..]);
            var (pb, pl) = ParseFormula(parts[2]["PARAMETERS=".Length..]);
            map[cat] = new FormulaBudget(mb, ml, pb, pl);
        }

        return (map, ilFwd, ilDispBase, ilDispPerLeaf);
    }

    private static (int BaseVal, int PerLeaf) ParseFormula(string expr)
    {
        var plus = expr.IndexOf('+');
        var b = int.Parse(expr[..plus], CultureInfo.InvariantCulture);
        var l = int.Parse(expr[(plus + 1)..^1], CultureInfo.InvariantCulture);
        return (b, l);
    }
}
