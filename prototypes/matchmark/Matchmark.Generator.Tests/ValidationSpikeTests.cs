#pragma warning disable ASP0029 // Microsoft.Extensions.Validation is experimental in .NET 10.
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Validation;
using NUnit.Framework;

namespace Matchmark.Generator.Tests;

/// <summary>
/// The spike for decision Q45 (docs/reference/validation.md#the-spike): what the generator would emit, written by hand.
/// A model with DataAnnotations, a resolver describing it in the spec's JSON names, and a minimal API endpoint, with no
/// help from Microsoft's validations generator. It records what .NET's layer does with them.
/// </summary>
[TestFixture]
public sealed class ValidationSpikeTests
{
    public sealed record Details([property: JsonPropertyName("livesLeft"), System.ComponentModel.DataAnnotations.Range(0, 9)] int LivesLeft);

    public sealed record NewPet(
        [property: JsonPropertyName("name"), Required, MaxLength(10)] string Name,
        [property: JsonPropertyName("details"), Required] Details Details) : IValidatableObject
    {
        // A rule across fields, as x-matchmark-rules would generate it.
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Name == "Garfield" && Details.LivesLeft != 9)
            {
                yield return new ValidationResult("Garfield keeps all his lives.", ["livesLeft"]);
            }
        }
    }

    /// <summary>What Matchmark would generate: every model, by its JSON names.</summary>
    private sealed class Resolver : IValidatableInfoResolver
    {
        public bool TryGetValidatableTypeInfo(Type type, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IValidatableInfo? info)
        {
            info = type == typeof(NewPet) ? new TypeInfo(typeof(NewPet),
                    [new Property(typeof(NewPet), typeof(string), "name", new RequiredAttribute(), new MaxLengthAttribute(10)),
                     new Property(typeof(NewPet), typeof(Details), "details", new RequiredAttribute())])
                : type == typeof(Details) ? new TypeInfo(typeof(Details), [new Property(typeof(Details), typeof(int), "livesLeft", new System.ComponentModel.DataAnnotations.RangeAttribute(0, 9))])
                : null;
            return info is not null;
        }

        public bool TryGetValidatableParameterInfo(ParameterInfo parameterInfo, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IValidatableInfo? info)
        {
            info = parameterInfo.ParameterType == typeof(NewPet) ? new Parameter(typeof(NewPet), parameterInfo.Name!) : null;
            return info is not null;
        }
    }

    private sealed class TypeInfo(Type type, IReadOnlyList<ValidatablePropertyInfo> members) : ValidatableTypeInfo(type, members)
    {
        protected override ValidationAttribute[] GetValidationAttributes() => [];
    }

    /// <summary>A property: its C# name (the layer finds it by reflection) and its JSON name, which it's shown as.</summary>
    private sealed class Property(Type declaring, Type type, string name, params ValidationAttribute[] attributes)
        : ValidatablePropertyInfo(declaring, type, char.ToUpperInvariant(name[0]) + name[1..], name)
    {
        protected override ValidationAttribute[] GetValidationAttributes() => attributes;
    }

    /// <summary>
    /// A body parameter. .NET keys errors by C# paths (<c>Details.LivesLeft</c>), so after it validates, each key is
    /// translated to the spec's JSON path (<c>details.livesLeft</c>) through the models' JSON names.
    /// </summary>
#pragma warning disable CS9107 // the type is the base's too; it's kept here to translate keys.
    private sealed class Parameter(Type type, string name) : ValidatableParameterInfo(type, name, name)
#pragma warning restore CS9107
    {
        private Type Root { get; } = type;

        protected override ValidationAttribute[] GetValidationAttributes() => [];

        public override async Task ValidateAsync(object? value, ValidateContext context, CancellationToken cancellationToken)
        {
            var errors = context.ValidationErrors;
            context.ValidationErrors = [];
            try
            {
                await base.ValidateAsync(value, context, cancellationToken);
            }
            finally
            {
                foreach (var (key, messages) in context.ValidationErrors ?? [])
                {
                    var json = Json(Root, key);
                    errors ??= [];
                    errors[json] = errors.TryGetValue(json, out var had) ? [.. had, .. messages] : messages;
                }

                context.ValidationErrors = errors;
            }
        }

        /// <summary>A C# path as the spec's JSON path: each segment the property's JSON name, walking from the parameter's type.</summary>
        private static string Json(Type root, string path)
        {
            var type = root;
            var segments = new List<string>();
            foreach (var segment in path.Split('.'))
            {
                var property = type?.GetProperty(segment);
                segments.Add(property?.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? segment);
                type = property?.PropertyType;
            }

            return string.Join('.', segments);
        }
    }

    private static async Task<(int Status, JsonObject? Errors)> Post(object body, bool resolver)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddValidation(options =>
        {
            if (resolver)
            {
                options.Resolvers.Insert(0, new Resolver());
            }
        });
        await using var app = builder.Build();
        app.MapPost("/pets", (NewPet pet) => Results.Created($"/pets/1", pet));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single() + "/") };
        var response = await client.PostAsJsonAsync("pets", body);
        var text = await response.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine($"{(int)response.StatusCode} {text}");
        return ((int)response.StatusCode, text.Length > 0 ? JsonNode.Parse(text)?["errors"] as JsonObject : null);
    }

    [Test]
    public async Task Without_the_resolver_Microsoft_s_generator_validates_a_model_it_sees_under_C_sharp_names()
    {
        // This test project's own MapPost and model are in the compilation Microsoft's validations generator runs over, so
        // it finds them; it keys errors by C# paths, not the spec's. A model Matchmark generates is in no compilation that
        // generator sees (source generators don't see each other's output), so it gets neither: hence the resolver.
        var (status, errors) = await Post(new { name = "A name far too long", details = new { livesLeft = 12 } }, resolver: false);
        status.Should().Be(400);
        errors!.Select(e => e.Key).Should().BeEquivalentTo("Name", "Details.LivesLeft");
    }

    [Test]
    public async Task A_generated_resolver_validates_nested_values_under_the_spec_s_names()
    {
        var (status, errors) = await Post(new { name = "A name far too long", details = new { livesLeft = 12 } }, resolver: true);
        status.Should().Be(400);
        errors!.Select(e => e.Key).Should().BeEquivalentTo("name", "details.livesLeft");
    }

    [Test]
    public async Task Rules_across_fields_run_as_IValidatableObject_once_the_fields_are_valid()
    {
        var (status, errors) = await Post(new { name = "Garfield", details = new { livesLeft = 3 } }, resolver: true);
        status.Should().Be(400);
        errors!.Select(e => e.Key).Should().ContainSingle().Which.Should().Be("livesLeft", "the key is the member name the rule gives, not a path from the root");
    }
}
