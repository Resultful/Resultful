using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OneOf;
using Xunit;

namespace Resultful.Consumption;

/// <summary>
/// Verifies that every public member declared in src/Resultful/PublicAPI.Shipped.txt and
/// PublicAPI.Unshipped.txt is genuinely invoked, accessed, or converted by the consumption tests in this project.
/// Only symbols the compiler bound count: an overload that failed to bind is not covered.
/// </summary>
public sealed partial class PublicApiCoverageTests
{
    [Fact]
    public void Every_public_member_is_exercised_by_consumption_tests()
    {
        var apiDir = Path.Combine(AssemblyPaths.RepoRootDir, "src", "Resultful");
        var publicMembers = new List<(string RawLine, string Key)>();
        foreach (var file in new[] { "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt" })
        {
            var path = Path.Combine(apiDir, file);
            Assert.True(File.Exists(path), $"{file} not found at {path}");
            publicMembers.AddRange(ParseApiMembers(File.ReadAllLines(path)));
        }

        Assert.NotEmpty(publicMembers);

        var exercisedKeys = CollectExercisedResultfulMembers();

        var missing = publicMembers
            .Where(m => !exercisedKeys.Contains(m.Key))
            .Select(m => $"{m.RawLine}  [key: {m.Key}]")
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Found {missing.Count} public API member(s) with no consumption test:\n" +
            string.Join("\n", missing.Take(40)) +
            (missing.Count > 40 ? $"\n... and {missing.Count - 40} more" : string.Empty));
    }

    private static HashSet<string> CollectExercisedResultfulMembers()
    {
        var testDir = AssemblyPaths.ConsumptionTestsDir;
        var csFiles = Directory.EnumerateFiles(testDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).Equals("PublicApiCoverageTests.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var trees = csFiles
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parseOptions, path: f))
            .ToList();
        trees.Add(CSharpSyntaxTree.ParseText(
            """
            global using global::System;
            global using global::System.Collections.Generic;
            global using global::System.IO;
            global using global::System.Linq;
            global using global::System.Threading;
            global using global::System.Threading.Tasks;
            """,
            parseOptions,
            path: "ImplicitUsings.g.cs"));

        var refs = GetMetadataReferences();
        var compilation = CSharpCompilation.Create(
            "Resultful.Consumption.CoverageScan",
            trees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // The scan must compile cleanly. With errors, symbols fail to bind and the coverage result means nothing.
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        Assert.True(
            errors.Count == 0,
            $"The coverage scan compilation has {errors.Count} error(s):\n" + string.Join("\n", errors.Take(20)));

        var resultfulAssembly = compilation.GetTypeByMetadataName("Resultful.Result")?.ContainingAssembly
            ?? throw new InvalidOperationException("Could not resolve Resultful assembly in compilation.");

        var exercised = new HashSet<string>(StringComparer.Ordinal);

        void Record(ISymbol? symbol)
        {
            if (symbol is null)
            {
                return;
            }

            if (symbol is IMethodSymbol method)
            {
                var unreduced = method.ReducedFrom ?? method;
                var orig = unreduced.OriginalDefinition;
                if (SymbolEqualityComparer.Default.Equals(orig.ContainingAssembly, resultfulAssembly))
                {
                    exercised.Add(FormatMethodKey(orig));
                }
            }
            else if (symbol is IPropertySymbol prop)
            {
                var orig = prop.OriginalDefinition;
                if (SymbolEqualityComparer.Default.Equals(orig.ContainingAssembly, resultfulAssembly) && orig.GetMethod is not null)
                {
                    exercised.Add(FormatMethodKey(orig.GetMethod));
                }
            }
        }

        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                var info = model.GetSymbolInfo(node);
                Record(info.Symbol);

                if (node is ExpressionSyntax expr)
                {
                    var conv = model.GetConversion(expr);
                    Record(conv.MethodSymbol);
                }

                if (node is QueryClauseSyntax clause)
                {
                    var qInfo = model.GetQueryClauseInfo(clause);
                    Record(qInfo.CastInfo.Symbol);
                    Record(qInfo.OperationInfo.Symbol);
                }
                else if (node is SelectOrGroupClauseSyntax selectClause)
                {
                    Record(model.GetSymbolInfo(selectClause).Symbol);
                }
            }
        }

        return exercised;
    }

    private static List<MetadataReference> GetMetadataReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var paths = new HashSet<string>(trustedAssemblies, StringComparer.OrdinalIgnoreCase)
        {
            typeof(Result).Assembly.Location,
            typeof(OneOfBase<,>).Assembly.Location,
            typeof(FactAttribute).Assembly.Location,
            typeof(CsCheck.Gen).Assembly.Location,
        };

        return paths
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }

    private static List<(string RawLine, string Key)> ParseApiMembers(string[] lines)
    {
        var result = new List<(string, string)>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)
                || line.StartsWith('#')
                || line.StartsWith("*REMOVED*", StringComparison.Ordinal)
                || !line.Contains("->", StringComparison.Ordinal))
            {
                continue;
            }

            result.Add((line, NormalizeShippedLine(line)));
        }
        return result;
    }

    private static string NormalizeShippedLine(string line)
    {
        var isStatic = false;
        var rest = line;
        if (rest.StartsWith("static ", StringComparison.Ordinal))
        {
            isStatic = true;
            rest = rest["static ".Length..];
        }

        var arrowIdx = rest.LastIndexOf(" -> ", StringComparison.Ordinal);
        var lhs = rest[..arrowIdx].Trim();
        var ret = rest[(arrowIdx + 4)..].Trim();

        // Property getter: Resultful.Option<T>.None.get
        if (lhs.EndsWith(".get", StringComparison.Ordinal))
        {
            var withoutGet = lhs[..^4];
            var dotIdx = withoutGet.LastIndexOf('.');
            var owner = withoutGet[..dotIdx];
            var propName = withoutGet[(dotIdx + 1)..];
            var typeMap = BuildTypeParamMap(owner, string.Empty);
            return $"{(isStatic ? "S:" : "I:")}{NormalizeType(owner, typeMap)}.get_{propName}`0()->{NormalizeType(ret, typeMap)}";
        }

        // Implicit operator: Resultful.Result<T>.implicit operator Resultful.Result<T>(string! value)
        var implicitIdx = lhs.IndexOf(".implicit operator ", StringComparison.Ordinal);
        if (implicitIdx >= 0)
        {
            var owner = lhs[..implicitIdx];
            var parenStart = lhs.IndexOf('(', implicitIdx);
            var paramList = lhs[(parenStart + 1)..^1];
            var typeMap = BuildTypeParamMap(owner, string.Empty);
            var paramTypes = SplitTopLevel(paramList)
                .Select(p => NormalizeParam(p, typeMap));
            return $"S:{NormalizeType(owner, typeMap)}.op_Implicit`0({string.Join(",", paramTypes)})->{NormalizeType(ret, typeMap)}";
        }

        // Regular method or constructor: Owner.Name<TArgs>(params)
        var openParen = lhs.IndexOf('(');
        var beforeParen = lhs[..openParen];
        var paramsStr = lhs[(openParen + 1)..^1];

        var lastDot = FindLastDotOutsideAngles(beforeParen);
        var ownerStr = beforeParen[..lastDot];
        var memberStr = beforeParen[(lastDot + 1)..];

        var methodTypeParams = string.Empty;
        var methodName = memberStr;
        var angleIdx = memberStr.IndexOf('<');
        if (angleIdx >= 0)
        {
            methodName = memberStr[..angleIdx];
            methodTypeParams = memberStr[(angleIdx + 1)..^1];
        }

        var ownerSimpleName = StripGenericArgs(ownerStr.Split('.').Last());
        if (!isStatic && methodName == ownerSimpleName)
        {
            methodName = ".ctor";
        }

        var methodArity = string.IsNullOrEmpty(methodTypeParams) ? 0 : SplitTopLevel(methodTypeParams).Count;
        var map = BuildTypeParamMap(ownerStr, methodTypeParams);
        var normParams = string.IsNullOrWhiteSpace(paramsStr)
            ? Array.Empty<string>()
            : SplitTopLevel(paramsStr).Select(p => NormalizeParam(p, map)).ToArray();

        return $"{(isStatic ? "S:" : "I:")}{NormalizeType(ownerStr, map)}.{methodName}`{methodArity}({string.Join(",", normParams)})->{NormalizeType(ret, map)}";
    }

    private static string FormatMethodKey(IMethodSymbol method)
    {
        var typeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var owner = method.ContainingType;
        for (var i = 0; i < owner.TypeParameters.Length; i++)
        {
            typeMap[owner.TypeParameters[i].Name] = $"!{i}";
        }
        for (var i = 0; i < method.TypeParameters.Length; i++)
        {
            typeMap[method.TypeParameters[i].Name] = $"!!{i}";
        }

        var prefix = method.IsStatic ? "S:" : "I:";
        var ownerStr = FormatSymbolType(owner, typeMap);
        var normParams = method.Parameters.Select(p => FormatSymbolType(p.Type, typeMap));
        var retStr = FormatSymbolType(method.ReturnType, typeMap);
        return $"{prefix}{ownerStr}.{method.Name}`{method.TypeParameters.Length}({string.Join(",", normParams)})->{retStr}";
    }

    private static string FormatSymbolType(ITypeSymbol type, Dictionary<string, string> typeMap)
    {
        if (type is ITypeParameterSymbol tp)
        {
            return typeMap.TryGetValue(tp.Name, out var mapped) ? mapped : tp.Name;
        }
        if (type is IArrayTypeSymbol arr)
        {
            return $"{FormatSymbolType(arr.ElementType, typeMap)}[]";
        }
        if (type is INamedTypeSymbol named)
        {
            if (named.IsTupleType)
            {
                return "(" + string.Join(",", named.TupleElements.Select(e => FormatSymbolType(e.Type, typeMap))) + ")";
            }

            if (named.SpecialType == SpecialType.System_Void) return "void";
            if (named.SpecialType == SpecialType.System_String) return "string";
            if (named.SpecialType == SpecialType.System_Int32) return "int";
            if (named.SpecialType == SpecialType.System_Boolean) return "bool";
            if (named.SpecialType == SpecialType.System_Object) return "object";

            var ns = named.ContainingNamespace.IsGlobalNamespace ? string.Empty : named.ContainingNamespace.ToDisplayString() + ".";
            var baseName = ns + named.Name;
            if (named.TypeArguments.Length > 0)
            {
                return $"{baseName}<{string.Join(",", named.TypeArguments.Select(a => FormatSymbolType(a, typeMap)))}>";
            }
            return baseName;
        }
        return type.ToDisplayString();
    }

    private static Dictionary<string, string> BuildTypeParamMap(string owner, string methodTypeParams)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var ownerAngle = owner.IndexOf('<');
        if (ownerAngle >= 0)
        {
            var ownerArgs = SplitTopLevel(owner[(ownerAngle + 1)..^1]);
            for (var i = 0; i < ownerArgs.Count; i++)
            {
                map[ownerArgs[i].Trim()] = $"!{i}";
            }
        }
        if (!string.IsNullOrEmpty(methodTypeParams))
        {
            var mArgs = SplitTopLevel(methodTypeParams);
            for (var i = 0; i < mArgs.Count; i++)
            {
                map[mArgs[i].Trim()] = $"!!{i}";
            }
        }
        return map;
    }

    private static string NormalizeParam(string param, Dictionary<string, string> typeMap)
    {
        var p = param.Trim();
        if (p.StartsWith("this ", StringComparison.Ordinal)) p = p[5..].Trim();
        if (p.StartsWith("params ", StringComparison.Ordinal)) p = p[7..].Trim();

        // Strip trailing parameter name (last space outside <> and ())
        var lastSpace = FindLastSpaceOutsideBrackets(p);
        if (lastSpace > 0)
        {
            p = p[..lastSpace].Trim();
        }

        return NormalizeType(p, typeMap);
    }

    private static string NormalizeType(string type, Dictionary<string, string> typeMap)
    {
        var t = type.Trim().Replace("!", string.Empty, StringComparison.Ordinal).Replace("?", string.Empty, StringComparison.Ordinal);

        if (t.EndsWith("[]", StringComparison.Ordinal))
        {
            return $"{NormalizeType(t[..^2], typeMap)}[]";
        }

        if (t.StartsWith('(') && t.EndsWith(')'))
        {
            var elems = SplitTopLevel(t[1..^1]).Select(e => NormalizeType(e, typeMap));
            return "(" + string.Join(",", elems) + ")";
        }

        var angleStart = t.IndexOf('<');
        if (angleStart >= 0)
        {
            var head = t[..angleStart].Trim();
            var args = SplitTopLevel(t[(angleStart + 1)..^1]).Select(a => NormalizeType(a, typeMap));
            return $"{head}<{string.Join(",", args)}>";
        }

        return typeMap.TryGetValue(t, out var mapped) ? mapped : t;
    }

    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (ch is '<' or '(') depth++;
            else if (ch is '>' or ')') depth--;

            if (ch == ',' && depth == 0)
            {
                parts.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }
        if (sb.Length > 0)
        {
            parts.Add(sb.ToString().Trim());
        }
        return parts;
    }

    private static int FindLastDotOutsideAngles(string s)
    {
        var depth = 0;
        for (var i = s.Length - 1; i >= 0; i--)
        {
            if (s[i] == '>') depth++;
            else if (s[i] == '<') depth--;
            else if (s[i] == '.' && depth == 0) return i;
        }
        return -1;
    }

    private static int FindLastSpaceOutsideBrackets(string s)
    {
        var depth = 0;
        for (var i = s.Length - 1; i >= 0; i--)
        {
            if (s[i] is '>' or ')') depth++;
            else if (s[i] is '<' or '(') depth--;
            else if (s[i] == ' ' && depth == 0) return i;
        }
        return -1;
    }

    private static string StripGenericArgs(string s)
    {
        var idx = s.IndexOf('<');
        return idx >= 0 ? s[..idx] : s;
    }
}
