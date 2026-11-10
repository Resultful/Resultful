using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>The emitter's validation (decision Q10: the model and its outputs, split by output).</summary>
    internal sealed partial class Emitter
    {
        // ---------------------------------------------------------------- validation

        /// <summary>
        /// The translation of a binding failure (ASP.NET Core's key and message) into the spec's terms, shared by the
        /// generated endpoints and the scaffolded controllers. Found necessary by the regression suite generated from the spec.
        /// </summary>
        /// <summary>Forms (application/x-www-form-urlencoded), read into and written from the generated types, nested values in brackets.</summary>
        private const string FormMethods = @"/// <summary>
/// A form (application/x-www-form-urlencoded) as the type: nested values in brackets, as Stripe sends them (card[number],
/// expand[], items[0][price]); numbers and true or false read from their text. Throws JsonException, at the value's
/// path, for a form that doesn't fit the schema.
/// </summary>
public static T ReadForm<T>(IEnumerable<KeyValuePair<string, string>> fields)
{
    var root = new global::System.Text.Json.Nodes.JsonObject();
    foreach (var field in fields)
    {
        var keys = FormKeys(field.Key);
        var node = root;
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].Length == 0 ? node.Count.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : keys[i];
            if (i == keys.Count - 1)
            {
                node[key] = field.Value;
            }
            else
            {
                if (node[key] is not global::System.Text.Json.Nodes.JsonObject next)
                {
                    next = new global::System.Text.Json.Nodes.JsonObject();
                    node[key] = next;
                }

                node = next;
            }
        }
    }

    return global::System.Text.Json.JsonSerializer.Deserialize<T>(FormArrays(root), FormRead)
        ?? throw new global::System.Text.Json.JsonException(""The form is empty."");
}

/// <summary>A deep-object query parameter's value (name[a][b]=v) as JSON, or the text of name=v; null when it isn't there.</summary>
public static global::System.Text.Json.Nodes.JsonNode? QueryNode(IEnumerable<KeyValuePair<string, string>> fields, string name)
{
    var own = fields.Where(f => f.Key == name || f.Key.StartsWith(name + ""["", System.StringComparison.Ordinal)).ToList();
    if (own.Count == 0)
    {
        return null;
    }

    if (own.All(f => f.Key == name))
    {
        return global::System.Text.Json.Nodes.JsonValue.Create(own[0].Value);
    }

    var root = new global::System.Text.Json.Nodes.JsonObject();
    foreach (var field in own.Where(f => f.Key != name))
    {
        var keys = FormKeys(""x"" + field.Key.Substring(name.Length));
        var node = root;
        for (var i = 1; i < keys.Count; i++)
        {
            var key = keys[i].Length == 0 ? node.Count.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : keys[i];
            if (i == keys.Count - 1)
            {
                node[key] = field.Value;
            }
            else
            {
                if (node[key] is not global::System.Text.Json.Nodes.JsonObject next)
                {
                    next = new global::System.Text.Json.Nodes.JsonObject();
                    node[key] = next;
                }

                node = next;
            }
        }
    }

    return FormArrays(root);
}

/// <summary>The whole query as JSON (OpenAPI 3.2's querystring), nested values in brackets; null when it's empty.</summary>
public static global::System.Text.Json.Nodes.JsonNode? QueryAll(IEnumerable<KeyValuePair<string, string>> fields)
{
    var all = fields.ToList();
    if (all.Count == 0)
    {
        return null;
    }

    var root = new global::System.Text.Json.Nodes.JsonObject();
    foreach (var field in all)
    {
        var keys = FormKeys(field.Key);
        var node = root;
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].Length == 0 ? node.Count.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : keys[i];
            if (i == keys.Count - 1)
            {
                node[key] = field.Value;
            }
            else
            {
                if (node[key] is not global::System.Text.Json.Nodes.JsonObject next)
                {
                    next = new global::System.Text.Json.Nodes.JsonObject();
                    node[key] = next;
                }

                node = next;
            }
        }
    }

    return FormArrays(root);
}

/// <summary>JSON as the type, numbers and true or false read from their text.</summary>
public static T FromNode<T>(global::System.Text.Json.Nodes.JsonNode node)
    => global::System.Text.Json.JsonSerializer.Deserialize<T>(node, FormRead) ?? throw new global::System.Text.Json.JsonException(""The value is null."");

/// <summary>A value as a deep-object query parameter's fields: name[a][b]=v, or name=v for a plain value.</summary>
public static IEnumerable<KeyValuePair<string, string>> QueryFields<T>(string name, T value)
    => FormFields(value).Select(f => new KeyValuePair<string, string>(
        f.Key.Length == 0 ? name : name + ""["" + (f.Key.IndexOf('[') is var open and >= 0 ? f.Key.Substring(0, open) + ""]"" + f.Key.Substring(open) : f.Key + ""]""), f.Value));

/// <summary>The type as form fields, nested values in brackets; absent values left out.</summary>
public static IEnumerable<KeyValuePair<string, string>> FormFields<T>(T value)
    => FormFlatten(global::System.Text.Json.JsonSerializer.SerializeToNode(value, FormWrite), null);

private static readonly global::System.Text.Json.JsonSerializerOptions FormRead = new(global::System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new FormBoolean() } };

private static readonly global::System.Text.Json.JsonSerializerOptions FormWrite = new(global::System.Text.Json.JsonSerializerDefaults.Web)
{
    DefaultIgnoreCondition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
};

private static List<string> FormKeys(string key)
{
    var open = key.IndexOf('[');
    var keys = new List<string> { open < 0 ? key : key.Substring(0, open) };
    while (open >= 0 && key.IndexOf(']', open) is var close and > 0)
    {
        keys.Add(key.Substring(open + 1, close - open - 1));
        open = key.IndexOf('[', close);
    }

    return keys;
}

/// <summary>An object whose keys are 0, 1, 2... is an array.</summary>
private static global::System.Text.Json.Nodes.JsonNode? FormArrays(global::System.Text.Json.Nodes.JsonNode? node)
{
    if (node is not global::System.Text.Json.Nodes.JsonObject obj)
    {
        return node?.DeepClone();
    }

    var index = 0;
    if (obj.Count > 0 && obj.All(p => p.Key == (index++).ToString(global::System.Globalization.CultureInfo.InvariantCulture)))
    {
        return new global::System.Text.Json.Nodes.JsonArray(obj.Select(p => FormArrays(p.Value)).ToArray());
    }

    var copy = new global::System.Text.Json.Nodes.JsonObject();
    foreach (var pair in obj)
    {
        copy[pair.Key] = FormArrays(pair.Value);
    }

    return copy;
}

private static IEnumerable<KeyValuePair<string, string>> FormFlatten(global::System.Text.Json.Nodes.JsonNode? node, string? prefix)
{
    switch (node)
    {
        case global::System.Text.Json.Nodes.JsonObject obj:
            foreach (var pair in obj)
            {
                foreach (var field in FormFlatten(pair.Value, prefix is null ? pair.Key : prefix + ""["" + pair.Key + ""]""))
                {
                    yield return field;
                }
            }

            break;
        case global::System.Text.Json.Nodes.JsonArray array:
            for (var i = 0; i < array.Count; i++)
            {
                foreach (var field in FormFlatten(array[i], prefix + ""["" + i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + ""]""))
                {
                    yield return field;
                }
            }

            break;
        case global::System.Text.Json.Nodes.JsonValue value:
            yield return new KeyValuePair<string, string>(prefix ?? string.Empty, value.GetValueKind() switch
            {
                global::System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
                global::System.Text.Json.JsonValueKind.True => ""true"",
                global::System.Text.Json.JsonValueKind.False => ""false"",
                _ => value.ToJsonString(),
            });
            break;
    }
}

/// <summary>true or false, from a form's text.</summary>
private sealed class FormBoolean : global::System.Text.Json.Serialization.JsonConverter<bool>
{
    public override bool Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)
        => reader.TokenType switch
        {
            global::System.Text.Json.JsonTokenType.True => true,
            global::System.Text.Json.JsonTokenType.False => false,
            global::System.Text.Json.JsonTokenType.String when reader.GetString() is ""true"" => true,
            global::System.Text.Json.JsonTokenType.String when reader.GetString() is ""false"" => false,
            _ => throw new global::System.Text.Json.JsonException(""Expected true or false.""),
        };

    public override void Write(global::System.Text.Json.Utf8JsonWriter writer, bool value, global::System.Text.Json.JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);
}";

        private const string BindingErrorMethod = @"/// <summary>
/// A request that couldn't be bound, in the spec's terms: which value was wrong (<c>name</c>, <c>details.goodBoy</c>,
/// <c>petId</c>, or <c>body</c> for the whole body) and what was wrong with it. <paramref name=""key""/> is where ASP.NET Core
/// put the failure (a JSON path such as <c>$.name</c>, or a parameter name) and <paramref name=""message""/> its message.
/// The generated endpoints and the scaffolded controllers both answer through this, so they answer alike.
/// </summary>
public static (string Key, string Message) BindingError(string? key, string message)
{
    var at = key is null || key.Length == 0 || key == ""$"" ? string.Empty : key.StartsWith(""$."", System.StringComparison.Ordinal) ? key.Substring(2) : key.TrimStart('$');
    at = ParameterNames.TryGetValue(at, out var named) ? named : at;
    var missing = Regex.Match(message, ""missing required properties including: '([^']+)'"");
    if (missing.Success)
    {
        return (at.Length == 0 ? missing.Groups[1].Value : at + ""."" + missing.Groups[1].Value, $""'{missing.Groups[1].Value}' is required."");
    }

    var unmapped = Regex.Match(message, ""JSON property '([^']+)' could not be mapped"");
    if (unmapped.Success)
    {
        return (at.Length == 0 || at.EndsWith(unmapped.Groups[1].Value, System.StringComparison.Ordinal) ? (at.Length == 0 ? unmapped.Groups[1].Value : at) : at + ""."" + unmapped.Groups[1].Value,
            $""'{unmapped.Groups[1].Value}' isn't a property this object has."");
    }

    var body = at.Length == 0 || at == ""body"" ? ""body"" : at;
    var discriminator = Regex.Match(message, ""discriminator id '([^']*)'"");
    if (discriminator.Success)
    {
        return (body, $""'{discriminator.Groups[1].Value}' is not a known kind."");
    }

    if (message.Contains(""must specify a type discriminator""))
    {
        return (body, ""The type discriminator is missing."");
    }

    var value = Regex.Match(message, @""(?:value '([^']*)' is not valid|from \x22([^\x22]*)\x22)"");
    if (value.Success)
    {
        return (body, $""'{(value.Groups[1].Success ? value.Groups[1].Value : value.Groups[2].Value)}' is not a valid value."");
    }

    if (message.Contains(""request body is required"") || message.Contains(""was not provided from body""))
    {
        return (""body"", ""A request body is required."");
    }

    var type = Regex.Match(message, @""converted to ([\w.]+)\."");
    var expected = type.Groups[1].Value switch
    {
        ""System.String"" => ""a string"",
        ""System.Boolean"" => ""true or false"",
        ""System.Int16"" or ""System.Int32"" or ""System.Int64"" => ""an integer"",
        ""System.Single"" or ""System.Double"" or ""System.Decimal"" => ""a number"",
        ""System.DateTime"" or ""System.DateTimeOffset"" => ""a date-time"",
        ""System.Guid"" => ""a uuid"",
        _ => null,
    };
    return (body, expected is null ? ""The body is not valid JSON for this operation."" : $""Expected {expected}."");
}";

        private string EmitValidation(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings("System.Collections.Generic", "System.Linq", "System.Text.RegularExpressions");

            w.Line("/// <summary>");
            w.Line("/// Validation generated from the schema constraints (required, minLength, maxLength, pattern, minimum, maximum).");
            w.Line("/// The endpoints run it on request bodies; the client runs it on response bodies.");
            w.Line("/// </summary>");
            w.Open($"public static class {ValidationClass}");
            w.Block(BindingErrorMethod);
            w.Line();

            foreach (var bodyType in _schemas.Where(s => !IsEnum(s.Value)).Select(s => Naming.Pascal(s.Key)))
            {
                w.Line($"/// <summary>Returns the errors keyed by JSON path, or <c>null</c> when <paramref name=\"value\"/> is valid.</summary>");
                w.Open($"public static Dictionary<string, string[]>? Validate({bodyType} value)");
                w.Line("var errors = new Errors();");
                w.Line("Check(value, string.Empty, errors);");
                w.Line("return errors.Result();");
                w.Close();
                w.Line();
            }

            var patterns = new List<string>();
            foreach (var pair in _schemas.Where(s => !IsEnum(s.Value)))
            {
                var name = Naming.Pascal(pair.Key);
                var schema = pair.Value;
                w.Open($"private static void Check({name} value, string path, Errors errors)");

                if (IsUnion(schema))
                {
                    w.Open("switch (value)");
                    foreach (var member in _unionMembers.Where(m => m.Value.Base == pair.Key))
                    {
                        w.Line($"case {Naming.Pascal(member.Key)} member: Check(member, path, errors); break;");
                    }

                    w.Close();
                    w.Close();
                    w.Line();
                    continue;
                }

                _unionMembers.TryGetValue(pair.Key, out var union);
                foreach (var property in schema.Properties!)
                {
                    if (union.Property == property.Key || TypeOf(property.Value, pair.Key) is not { } type)
                    {
                        continue;
                    }

                    var member = "value." + Member(name, schema, property.Key);
                    var jsonPath = $"path + {Naming.Literal(property.Key)}";
                    var required = schema.Required?.Contains(property.Key) == true;
                    var prop = property.Value;
                    var local = "v" + Member(name, schema, property.Key);

                    if (required && !Has(prop, JsonSchemaType.Null) && !IsValueType(type) && !IsEnumRef(prop))
                    {
                        w.Line($"if ({member} is null) errors.Add({jsonPath}, \"The field is required.\");");
                    }

                    var checks = new List<string>();
                    if (prop.MinLength is { } min)
                    {
                        checks.Add($"if ({local}.Length < {min.Invariant()}) errors.Add({jsonPath}, \"Must be at least {min.Invariant()} characters.\");");
                    }

                    if (prop.MaxLength is { } max)
                    {
                        checks.Add($"if ({local}.Length > {max.Invariant()}) errors.Add({jsonPath}, \"Must be at most {max.Invariant()} characters.\");");
                    }

                    if (prop.Pattern is { } pattern)
                    {
                        var index = patterns.Count;
                        patterns.Add(pattern);
                        checks.Add($"if (!Pattern{index.Invariant()}.IsMatch({local})) errors.Add({jsonPath}, {Naming.Literal("Must match " + pattern + ".")});");
                    }

                    if (type == "string" && FormatCheck(prop.Format, local) is { } format)
                    {
                        checks.Add($"if ({format.Condition}) errors.Add({jsonPath}, {Naming.Literal(format.Message)});");
                    }

                    // An enum written inline on a string property stays a string: its values are checked here.
                    if (type.TrimEnd('?') == "string" && !IsEnumRef(prop) && prop.Enum is { Count: > 0 } values
                        && values.Select(v => v is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<string>(out var text) ? text : null).ToList() is var texts && texts.All(t => t is not null))
                    {
                        checks.Add($"if (!({local} is {string.Join(" or ", texts.Select(t => Naming.Literal(t!)))})) errors.Add({jsonPath}, {Naming.Literal("Must be one of " + string.Join(", ", texts) + ".")});");
                    }

                    if (prop.Minimum is { } minimum)
                    {
                        checks.Add($"if ({local} < {Number(minimum)}) errors.Add({jsonPath}, \"Must be at least {Number(minimum)}.\");");
                    }

                    if (prop.Maximum is { } maximum)
                    {
                        checks.Add($"if ({local} > {Number(maximum)}) errors.Add({jsonPath}, \"Must be at most {Number(maximum)}.\");");
                    }

                    // 3.1's numeric exclusive bounds (3.0's boolean form is read into these too).
                    if (prop.ExclusiveMinimum is { } above)
                    {
                        checks.Add($"if ({local} <= {Number(above)}) errors.Add({jsonPath}, \"Must be more than {Number(above)}.\");");
                    }

                    if (prop.ExclusiveMaximum is { } below)
                    {
                        checks.Add($"if ({local} >= {Number(below)}) errors.Add({jsonPath}, \"Must be less than {Number(below)}.\");");
                    }

                    if (RefId(prop) is { } refId && _schemas.ContainsKey(refId) && !IsEnumRef(prop))
                    {
                        checks.Add($"Check({local}, {jsonPath} + \".\", errors);");
                    }

                    if (Has(prop, JsonSchemaType.Array) && type.StartsWith("global::System.Collections.Generic.IReadOnlyList<", StringComparison.Ordinal))
                    {
                        // An array: its count, and each item as its own schema says.
                        if (prop.MinItems is { } minItems)
                        {
                            checks.Add($"if ({local}.Count < {minItems.Invariant()}) errors.Add({jsonPath}, \"Must have at least {minItems.Invariant()} items.\");");
                        }

                        if (prop.MaxItems is { } maxItems)
                        {
                            checks.Add($"if ({local}.Count > {maxItems.Invariant()}) errors.Add({jsonPath}, \"Must have at most {maxItems.Invariant()} items.\");");
                        }

                        if (prop.Items is { } items && ItemChecks(items, $"{jsonPath} + \"[\" + __i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\"", patterns) is { Count: > 0 } itemChecks)
                        {
                            checks.Add($"for (var __i = 0; __i < {local}.Count; __i++) {{ if ({local}[__i] is {{ }} __item) {{ {string.Join(" ", itemChecks)} }} }}");
                        }
                    }

                    if (checks.Count == 0)
                    {
                        continue;
                    }

                    // `is { } x` narrows both reference and nullable value types; a non-nullable value is always present.
                    var nullable = !required || Has(prop, JsonSchemaType.Null);
                    w.Open(IsValueType(type) && !nullable ? $"var {local} = {member};" : $"if ({member} is {{ }} {local})", braces: !(IsValueType(type) && !nullable));
                    foreach (var check in checks)
                    {
                        w.Line(check);
                    }

                    w.Close(braces: !(IsValueType(type) && !nullable));
                }

                foreach (var (rule, message) in Rules(schema))
                {
                    w.Line($"Rule(value, path, {Naming.Literal(rule)}, {Naming.Literal(message ?? $"Must satisfy {rule}.")}, errors);");
                }

                w.Close();
                w.Line();
            }

            if (operations.Any(o => o.Body is { Form: true } || o.Parameters.Any(p => p.Deep)))
            {
                w.Block(FormMethods);
                w.Line();
            }

            var renamed = operations.SelectMany(o => o.Parameters).Where(p => p.Identifier.TrimStart('@') != p.Name)
                .GroupBy(p => p.Identifier.TrimStart('@')).Select(g => g.First()).ToList();
            w.Line("/// <summary>The spec's name of each parameter whose C# name differs (xSignature is X-Signature), for binding errors.</summary>");
            w.Line($"private static readonly Dictionary<string, string> ParameterNames = new() {{ {string.Join(", ", renamed.Select(p => $"[{Naming.Literal(p.Identifier.TrimStart('@'))}] = {Naming.Literal(p.Name)}"))} }};");
            w.Line();
            w.Line("/// <summary>A text body against its schema (an empty one is missing when it's required): the errors, keyed body, or null.</summary>");
            w.Open("public static Dictionary<string, string[]>? CheckText(string text, int minLength, int? maxLength, string? pattern)");
            w.Line("var error = text.Length == 0 && minLength > 0 ? \"A request body is required.\"");
            w.Line("    : text.Length < minLength ? $\"Must be at least {minLength} characters.\"");
            w.Line("    : maxLength is { } max && text.Length > max ? $\"Must be at most {max} characters.\"");
            w.Line("    : pattern is not null && !Regex.IsMatch(text, pattern) ? $\"Must match {pattern}.\"");
            w.Line("    : null;");
            w.Line("return error is null ? null : new Dictionary<string, string[]> { [\"body\"] = new[] { error } };");
            w.Close();
            w.Line();
            w.Line("/// <summary>An enum from its text, by its name in the spec; false for anything else, a number included.</summary>");
            w.Open("public static bool TryEnum<T>(string text, out T value) where T : struct, global::System.Enum");
            w.Open("try");
            w.Line("value = global::System.Text.Json.JsonSerializer.Deserialize<T>(global::System.Text.Json.JsonSerializer.Serialize(text));");
            w.Line("return true;");
            w.Close();
            w.Open("catch (global::System.Text.Json.JsonException)");
            w.Line("value = default;");
            w.Line("return false;");
            w.Close();
            w.Close();
            w.Line();

            var refused = _schemas.Where(s => !IsEnum(s.Value) && s.Value.OneOf is not { Count: > 0 } && s.Value.Properties is { Count: > 0 })
                .Select(s => (Type: Naming.Pascal(s.Key), Names: s.Value.Properties!
                    .Where(p => s.Value.Required?.Contains(p.Key) != true && !Has(p.Value, JsonSchemaType.Null)
                                && !(_unionMembers.TryGetValue(s.Key, out var u) && u.Property == p.Key))
                    .Select(p => p.Key).ToList()))
                .Where(t => t.Names.Count > 0).ToList();
            w.Line("/// <summary>");
            w.Line("/// A JSON resolver modifier: refuses null, when read, for the optional properties the schema doesn't let be null, so that");
            w.Line("/// leaving one out and sending it as null aren't confused. The server's JSON options add it.");
            w.Line("/// </summary>");
            w.Open("public static void RefuseNulls(global::System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo)");
            w.Open("if (!NotNull.TryGetValue(typeInfo.Type, out var names))");
            w.Line("return;");
            w.Close();
            w.Line();
            w.Open("foreach (var property in typeInfo.Properties)");
            w.Open("if (property.Set is { } set && global::System.Array.IndexOf(names, property.Name) >= 0)");
            w.Line("var name = property.Name;");
            w.Line("property.Set = (target, value) => set(target, value ?? throw new global::System.Text.Json.JsonException($\"'{name}' can't be null; leave it out instead.\"));");
            w.Close();
            w.Close();
            w.Close();
            w.Line();
            w.Line($"private static readonly Dictionary<global::System.Type, string[]> NotNull = new() {{ {string.Join(", ", refused.Select(t => $"[typeof({t.Type})] = new[] {{ {string.Join(", ", t.Names.Select(Naming.Literal))} }}"))} }};");
            w.Line();

            if (_hasRules)
            {
                // An absent optional value is absent to CEL's has(), not a null.
                w.Line("private static readonly global::System.Text.Json.JsonSerializerOptions RulesJson = new(global::System.Text.Json.JsonSerializerDefaults.Web) { DefaultIgnoreCondition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };");
                w.Line();
                w.Line("/// <summary>The rules a model breaks, as .NET's validation layer takes them (IValidatableObject): a result per broken rule.</summary>");
                w.Open("public static global::System.Collections.Generic.IEnumerable<global::System.ComponentModel.DataAnnotations.ValidationResult> Broken<T>(T value, params (string Rule, string Message)[] rules)");
                w.Line("var node = global::System.Text.Json.JsonSerializer.SerializeToNode(value, RulesJson);");
                w.Open("foreach (var rule in rules)");
                w.Open("if (global::Matchmark.Rules.Cel.Check(rule.Rule, node) is { Holds: false, Error: null })");
                w.Line("yield return new global::System.ComponentModel.DataAnnotations.ValidationResult(rule.Message);");
                w.Close();
                w.Close();
                w.Close();
                w.Line();
                w.Line("/// <summary>An x-matchmark-rules rule (CEL over the value as JSON); a value it can't be evaluated on is left to the handler.</summary>");
                w.Open("private static void Rule<T>(T value, string path, string rule, string message, Errors errors)");
                w.Open("if (global::Matchmark.Rules.Cel.Check(rule, global::System.Text.Json.JsonSerializer.SerializeToNode(value, RulesJson)) is { Holds: false, Error: null })");
                w.Line("errors.Add(path.Length == 0 ? \"$\" : path.TrimEnd('.'), message);");
                w.Close();
                w.Close();
                w.Line();
                foreach (var operation in operations.Where(o => o.Dependencies.Count > 0))
                {
                    var field = $"{operation.Name}Dependencies";
                    w.Line($"private static readonly global::Matchmark.Rules.Idl[] {field} = new[] {{ {string.Join(", ", operation.Dependencies.Select(d => $"global::Matchmark.Rules.Idl.Parse({Naming.Literal(d)})"))} }};");
                    w.Line();
                    w.Line($"/// <summary>The x-dependencies of {operation.Id} its parameters break, keyed by the first parameter each names; null when none.</summary>");
                    w.Open($"public static Dictionary<string, string[]>? Broken{operation.Name}Dependencies(Dictionary<string, string?> parameters)");
                    w.Line("var given = new Dictionary<string, string>();");
                    w.Open("foreach (var parameter in parameters)");
                    w.Line("if (parameter.Value is { } text) given[parameter.Key] = text;");
                    w.Close();
                    w.Line();
                    w.Line("var errors = new Errors();");
                    w.Open($"foreach (var dependency in {field})");
                    w.Open("if (!dependency.Holds(given))");
                    w.Line("errors.Add(dependency.Parameters.Count > 0 ? dependency.Parameters[0] : \"$\", $\"Must satisfy {dependency.Source}\");");
                    w.Close();
                    w.Close();
                    w.Line();
                    w.Line("return errors.Result();");
                    w.Close();
                    w.Line();
                }

                w.Line("/// <summary>A parameter as the text it travels as, for its dependencies.</summary>");
                w.Line("public static string? Text(object? value) => value switch { null => null, bool b => b ? \"true\" : \"false\", global::System.IFormattable f => f.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture), _ => value.ToString() };");
                w.Line();
            }

            for (var i = 0; i < patterns.Count; i++)
            {
                w.Line($"private static readonly Regex Pattern{i.Invariant()} = new({Naming.Literal(patterns[i])}, RegexOptions.CultureInvariant);");
                w.Line();
            }

            w.Open("private sealed class Errors");
            w.Line("private Dictionary<string, List<string>>? _errors;");
            w.Line();
            w.Open("public void Add(string path, string message)");
            w.Line("_errors ??= new();");
            w.Line("if (!_errors.TryGetValue(path, out var list))");
            w.Line("{");
            w.Line("    _errors[path] = list = new();");
            w.Line("}");
            w.Line();
            w.Line("list.Add(message);");
            w.Close();
            w.Line();
            w.Line("public Dictionary<string, string[]>? Result()");
            w.Line("    => _errors is null ? null : System.Linq.Enumerable.ToDictionary(_errors, e => e.Key, e => e.Value.ToArray());");
            w.Close();

            w.Close();
            return w.ToString();
        }

        private static string Number(string value)
            => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d.ToString(CultureInfo.InvariantCulture)
                : value;
    }
}
