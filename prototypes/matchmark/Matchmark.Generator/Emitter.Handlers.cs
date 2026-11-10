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
    /// <summary>The emitter's handlers (decision Q10: the model and its outputs, split by output).</summary>
    internal sealed partial class Emitter
    {
        // ---------------------------------------------------------------- handlers

        private string EmitHandlers(List<Operation> operations)
        {
            var w = new CodeWriter(_namespace);
            w.Usings(OutcomeUsings("System.Threading", "System.Threading.Tasks"));

            foreach (var group in operations.GroupBy(o => o.Tag))
            {
                w.Line($"/// <summary>Implement this for the <c>{group.Key}</c> operations. Return values carry the spec's responses; HTTP is handled by the generated endpoints.</summary>");
                w.Open($"public interface {HandlerInterface(group.Key)}");
                foreach (var operation in group)
                {
                    var args = operation.Parameters.Select(p => $"{p.Type} {p.Identifier}").ToList();
                    if (operation.Body is not null)
                    {
                        args.Add($"{operation.Body.Type} body");
                    }

                    args.Add("CancellationToken cancellationToken");
                    w.Line($"/// <summary><c>{operation.Method} {operation.Path}</c></summary>");
                    w.Line($"{operation.HandlerReturnType} {operation.Name}({string.Join(", ", args)});");
                    w.Line();
                }

                w.TrimBlankLine();
                w.Close();
                w.Line();
                EmitValidatingHandlers(w, group.Key, group.ToList());
            }

            if (operations.SelectMany(o => o.ResponseHeaders).Distinct(StringComparer.OrdinalIgnoreCase).ToList() is { Count: > 0 } headers)
            {
                w.Line("/// <summary>");
                w.Line($"/// The response headers the spec documents for <c>{_apiName}</c>, for a handler to set: the generated endpoint opens a scope");
                w.Line("/// around each call and writes what was set onto the response. Outside one (a test calling a handler), setting does nothing.");
                w.Line("/// </summary>");
                w.Open($"public static class {_apiName}Headers");
                w.Line("private static readonly global::System.Threading.AsyncLocal<global::System.Collections.Generic.Dictionary<string, string>?> Current = new();");
                w.Line();
                w.Line("/// <summary>Opens a scope: what handlers set in it, until it's disposed.</summary>");
                w.Line("public static Scope Begin() => new(Current.Value);");
                w.Line();
                w.Line("/// <summary>Sets a response header, by name.</summary>");
                w.Open("public static void Set(string name, string value)");
                w.Open("if (Current.Value is { } values)");
                w.Line("values[name] = value;");
                w.Close();
                w.Close();
                foreach (var header in headers)
                {
                    w.Line();
                    w.Line($"/// <summary>Sets the <c>{header}</c> response header.</summary>");
                    w.Line($"public static void Set{Naming.Pascal(header)}(string value) => Set({Naming.Literal(header)}, value);");
                }

                w.Line();
                w.Open("public sealed class Scope : global::System.IDisposable");
                w.Line("private readonly global::System.Collections.Generic.Dictionary<string, string>? _previous;");
                w.Line("private readonly global::System.Collections.Generic.Dictionary<string, string> _values = new(global::System.StringComparer.OrdinalIgnoreCase);");
                w.Line();
                w.Open("internal Scope(global::System.Collections.Generic.Dictionary<string, string>? previous)");
                w.Line("_previous = previous;");
                w.Line("Current.Value = _values;");
                w.Close();
                w.Line();
                w.Line("/// <summary>The headers set in this scope.</summary>");
                w.Line("public global::System.Collections.Generic.IReadOnlyDictionary<string, string> Values => _values;");
                w.Line();
                w.Line("public void Dispose() => Current.Value = _previous;");
                w.Close();
                w.Close();
                w.Line();
            }

            return w.ToString();
        }

        private static string HandlerInterface(string tag) => $"I{Naming.Pascal(tag)}Handlers";

        /// <summary>
        /// A decorator applying the spec's request validation in front of any implementation of a handler interface, so an
        /// in-process implementation answers an invalid body the way the generated endpoint does. Operations that document
        /// no validation response pass straight through.
        /// </summary>
        private void EmitValidatingHandlers(CodeWriter w, string tag, List<Operation> operations)
        {
            var name = $"Validating{Naming.Pascal(tag)}Handlers";
            var iface = HandlerInterface(tag);
            w.Line($"/// <summary>Applies the spec's request validation in front of any <see cref=\"{iface}\"/>, as the generated endpoints do.</summary>");
            w.Open($"public sealed class {name} : {iface}");
            w.Line($"private readonly {iface} _inner;");
            w.Line();
            w.Line($"public {name}({iface} inner) => _inner = inner;");
            foreach (var operation in operations)
            {
                var args = operation.Parameters.Select(p => $"{p.Type} {p.Identifier}").ToList();
                var call = operation.Parameters.Select(p => p.Identifier).ToList();
                if (operation.Body is not null)
                {
                    args.Add($"{operation.Body.Type} body");
                    call.Add("body");
                }

                args.Add("CancellationToken cancellationToken");
                call.Add("cancellationToken");
                var forward = $"_inner.{operation.Name}({string.Join(", ", call)})";
                var validation = operation.Body?.Validated == true ? operation.Errors.FirstOrDefault(e => e.IsValidation) : null;

                w.Line();
                if (validation is null)
                {
                    w.Line($"public {operation.HandlerReturnType} {operation.Name}({string.Join(", ", args)})");
                    w.Line($"    => {forward};");
                    continue;
                }

                var hasValue = operation.SuccessIsUnion || operation.Successes[0].BodyType is not null;
                var problem = ValidationProblem(validation.BodyType!, "errors");
                var error = $"{operation.ErrorType}.{validation.CaseName}({problem})";
                var failed = hasValue
                    ? $"{Outcomes}.Result.Fail<{operation.SuccessType}, {operation.ErrorType}>({error})"
                    : $"{Outcomes}.Result.Fail<{operation.ErrorType}>({error})";
                w.Line($"public {operation.HandlerReturnType} {operation.Name}({string.Join(", ", args)})");
                w.Line($"    => {ValidationClass}.Validate(body) is {{ }} errors");
                w.Line($"        ? Task.FromResult({failed})");
                w.Line($"        : {forward};");
            }

            w.Close();
            w.Line();
        }
    }
}
