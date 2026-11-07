// Scaffolded from petstore.yaml by matchmark scaffold. This file is yours: edit it freely, it is never
// regenerated. The contract it implements (the types, unions and handler interfaces) stays generated.
#nullable enable

using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using PetStore.Api;

namespace PetStore.Scaffolded;

public static class PetStoreConventions
{
    /// <summary>
    /// JSON as the spec reads it (numbers are numbers; a oneOf discriminator may come anywhere), for both MVC and the
    /// OpenAPI document. Requests that can't be bound get the spec's validation problem, worded as the generated
    /// endpoints word it (PetStoreValidation.BindingError); the spec's constraints are checked by the generated validator, not
    /// by MVC's DataAnnotations.
    /// </summary>
    public static IServiceCollection AddPetStoreConventions(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.SerializerOptions.AllowOutOfOrderMetadataProperties = true;
            options.SerializerOptions.TypeInfoResolver = global::System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.WithAddedModifier(options.SerializerOptions.TypeInfoResolver ?? new global::System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), PetStoreValidation.RefuseNulls);
        });
        services.Configure<JsonOptions>(options =>
        {
            options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.JsonSerializerOptions.AllowOutOfOrderMetadataProperties = true;
            options.JsonSerializerOptions.TypeInfoResolver = global::System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.WithAddedModifier(options.JsonSerializerOptions.TypeInfoResolver ?? new global::System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), PetStoreValidation.RefuseNulls);
        });
        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
        {
            // Only binding failures reach here (no validators run): answer the first, preferring the JSON path it happened at.
            var (key, entry) = context.ModelState.Where(e => e.Value?.Errors.Count > 0).OrderBy(e => e.Key.StartsWith('$') ? 0 : 1).First();
            var failure = entry!.Errors[0];
            var (name, message) = PetStoreValidation.BindingError(key, failure.ErrorMessage.Length > 0 ? failure.ErrorMessage : failure.Exception?.Message ?? string.Empty);
            return Problem(context.HttpContext, name, message);
        });

        services.Configure<MvcOptions>(options =>
        {
            // The generated validator checks the spec's constraints; MVC's DataAnnotations would check them again, worded differently.
            options.ModelValidatorProviders.Clear();

            // A body missing its oneOf discriminator fails with NotSupportedException, which MVC's JSON input doesn't catch.
            options.Filters.Add(new MissingDiscriminator());

            // MVC's JSON input also accepts application/*+json and text/json; the spec documents application/json only.
            foreach (var json in options.InputFormatters.OfType<SystemTextJsonInputFormatter>())
            {
                json.SupportedMediaTypes.Remove("application/*+json");
                json.SupportedMediaTypes.Remove("text/json");
            }
        });
        return services;
    }

    /// <summary>The spec's validation problem for one value, as MVC writes problems.</summary>
    private static BadRequestObjectResult Problem(Microsoft.AspNetCore.Http.HttpContext context, string name, string message)
    {
        var errors = new ModelStateDictionary();
        errors.AddModelError(name, message);
        return new BadRequestObjectResult(context.RequestServices.GetRequiredService<ProblemDetailsFactory>().CreateValidationProblemDetails(context, errors));
    }

    /// <summary>A body missing its oneOf discriminator: a validation problem, not a 500.</summary>
    private sealed class MissingDiscriminator : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is NotSupportedException { Source: "System.Text.Json" } e && e.Message.Contains("type discriminator", StringComparison.Ordinal))
            {
                var (name, message) = PetStoreValidation.BindingError(Regex.Match(e.Message, @"Path: (\$\S*)").Groups[1].Value, e.Message);
                context.Result = Problem(context.HttpContext, name, message);
                context.ExceptionHandled = true;
            }
        }
    }
}
