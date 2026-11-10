using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Matchmark.Generator
{
    /// <summary>The spec as an <see cref="ApiShape"/>: the parse step's output, made from the same decisions as every output.</summary>
    internal sealed partial class Emitter
    {
        public ApiShape Shape(string path)
        {
            CollectSchemas();
            var operations = CollectOperations().Select(o => new OperationShape(
                o.Id,
                o.Method,
                o.Path,
                o.Tag,
                o.Parameters.Select(p => new ParameterShape(
                    p.Attribute.Substring(4), p.Name, p.Type.TrimEnd('?'), !p.Type.EndsWith("?", StringComparison.Ordinal),
                    (p.Schema is null ? Array.Empty<(string, string)>() : Constraints.Of(p.Schema)).Select(c => new ConstraintShape(c.Item1, c.Item2)).ToEquatable())).ToEquatable(),
                o.Body?.Type,
                o.Body?.Schema is { } body ? SchemaShapeOf(body) : null,
                o.Successes.Concat(o.Errors).Select(r => new ResponseShape(r.Status, r.Schema is { } s ? SchemaShapeOf(s) : null)).ToEquatable()));

            // The framework's problem types stand for their schemas whether or not the spec defines them as components.
            var wellKnown = WellKnownSchemas.Keys.Where(id => !_schemas.ContainsKey(id)).Select(id => new ComponentShape(
                id, Generated(id), true, ComponentShape.ObjectKind, EquatableArray<string>.Empty, null, EquatableArray<UnionMemberShape>.Empty, EquatableArray<PropertyShape>.Empty, null));
            var components = _schemas.Select(s => ComponentShapeOf(s.Key, s.Value)).Concat(wellKnown).OrderBy(c => c.Id, StringComparer.Ordinal);
            return new ApiShape(_apiName, _namespace, path, operations.ToEquatable(), components.ToEquatable());
        }

        private static SchemaShape SchemaShapeOf(IOpenApiSchema schema)
        {
            if (RefId(schema) is { } id)
            {
                return new SchemaShape(SchemaShape.RefKind, Ref: id);
            }

            if (Has(schema, JsonSchemaType.Array) && schema.Items is { } items)
            {
                return new SchemaShape(SchemaShape.ArrayKind, Items: SchemaShapeOf(items));
            }

            return Canonical(schema) is { } primitive ? new SchemaShape(SchemaShape.PrimitiveKind, Primitive: primitive) : new SchemaShape(SchemaShape.OtherKind);
        }

        private ComponentShape ComponentShapeOf(string id, IOpenApiSchema schema)
        {
            var wellKnown = WellKnownSchemas.ContainsKey(id);
            var kind = IsEnum(schema) ? ComponentShape.EnumKind : IsUnion(schema) ? ComponentShape.UnionKind : ComponentShape.ObjectKind;
            var values = kind == ComponentShape.EnumKind
                ? schema.Enum!.Select(v => v is JsonValue value && value.TryGetValue<string>(out var text) ? text : v?.ToJsonString() ?? "null")
                : Enumerable.Empty<string>();
            var members = kind == ComponentShape.UnionKind
                ? _unionMembers.Where(m => m.Value.Base == id).Select(m => new UnionMemberShape(m.Key, m.Value.Value))
                : Enumerable.Empty<UnionMemberShape>();
            var properties = (schema.Properties ?? new Dictionary<string, IOpenApiSchema>()).Select(p => new PropertyShape(
                p.Key,
                SchemaShapeOf(p.Value),
                (RefId(p.Value) is null ? Constraints.Of(p.Value) : Array.Empty<(string, string)>()).Select(c => new ConstraintShape(c.Item1, c.Item2)).ToEquatable(),
                schema.Required?.Contains(p.Key) == true,
                Has(p.Value, JsonSchemaType.Null)));
            return new ComponentShape(
                id,
                Generated(id),
                wellKnown,
                kind,
                values.ToEquatable(),
                kind == ComponentShape.UnionKind ? schema.Discriminator?.PropertyName : null,
                members.ToEquatable(),
                properties.ToEquatable(),
                _unionMembers.TryGetValue(id, out var union) ? union.Property : null);
        }
    }
}
