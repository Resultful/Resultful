// Scaffolded from petstore.yaml by matchmark scaffold. This file is yours: edit it freely, it is never
// regenerated. The contract it implements (the types, unions and handler interfaces) stays generated.
#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PetStore.Api;

namespace PetStore.Scaffolded;

public static class PetStoreOpenApi
{
    /// <summary>The spec's oneOf unions: base schema, discriminator property, and each member with its discriminator value.</summary>
    private static readonly (string Union, string Discriminator, (string Member, string Value)[] Members)[] Unions =
    [
        ("PetDetails", "kind", [("Dog", "dog"), ("Cat", "cat")]),
    ];

    private static readonly string[] StringEnums = ["PetStatus"];

    /// <summary>
    /// Describes the generated types as the spec does: schemas named after their types, and oneOf unions, their members and
    /// string enums written in the spec's form rather than ASP.NET Core's.
    /// </summary>
    public static OpenApiOptions AddPetStoreSchemas(this OpenApiOptions options)
    {
        options.CreateSchemaReferenceId = type => type.Type.Namespace == "PetStore.Api"
            ? type.Type.Name
            : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
        options.AddDocumentTransformer((document, _, _) =>
        {
            Describe(document);
            return Task.CompletedTask;
        });
        return options;
    }

    private static void Describe(OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas;
        if (schemas is null)
        {
            return;
        }

        foreach (var (union, discriminator, members) in Unions)
        {
            if (!schemas.TryGetValue(union, out var found) || found is not OpenApiSchema schema || schema.Discriminator?.Mapping is not { } mapping)
            {
                continue;
            }

            // ASP.NET Core writes the union as anyOf over members it names '{Union}{Member}'; the spec writes oneOf over the members' own names.
            foreach (var (member, value) in members)
            {
                if (mapping.TryGetValue(value, out var served) && served.Reference.Id is { } servedId && servedId != member && schemas.Remove(servedId, out var memberSchema))
                {
                    schemas[member] = memberSchema;
                }

                mapping[value] = new OpenApiSchemaReference(member, document);

                // The discriminator is an ordinary required string property of each member, not a single-value enum.
                if (schemas.TryGetValue(member, out var m) && m is OpenApiSchema memberObject)
                {
                    memberObject.Type ??= JsonSchemaType.Object;
                    memberObject.Properties ??= new System.Collections.Generic.Dictionary<string, IOpenApiSchema>();
                    memberObject.Properties[discriminator] = new OpenApiSchema { Type = JsonSchemaType.String };
                    memberObject.Required ??= new System.Collections.Generic.HashSet<string>();
                    memberObject.Required.Add(discriminator);
                }
            }

            schema.OneOf = members.Select(m => (IOpenApiSchema)new OpenApiSchemaReference(m.Member, document)).ToList();
            schema.AnyOf = null;
            schema.Type = null;
            schema.Required = null;
            schema.Properties = null;
        }

        foreach (var name in StringEnums)
        {
            if (schemas.TryGetValue(name, out var e) && e is OpenApiSchema enumSchema)
            {
                enumSchema.Type = JsonSchemaType.String;
            }
        }
    }
}
