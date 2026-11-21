using System.Runtime.CompilerServices;
using HotChocolate.Execution.Configuration;
using HotChocolate.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Resultful.HotChocolate;

/// <summary>Sets up a GraphQL server for resolvers that return Resultful results.</summary>
public static class ResultfulRequestExecutorBuilderExtensions
{
    /// <summary>
    /// Lets resolvers return <see cref="Result{T, TError}"/>, <see cref="Result{T}"/>,
    /// <see cref="Validation{T, TError}"/>, <see cref="Validation{T}"/>, a generated shape, or a <c>Task</c> or
    /// <c>ValueTask</c> of one, and shows their error cases as GraphQL error types, as HotChocolate's
    /// <c>FieldResult</c> does. Turns on the query conventions; a mutation that returns a result needs the mutation
    /// conventions (<c>AddMutationConventions()</c>). Calling it again adds to the options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A query field's type becomes a union of the success type and its error types, <c>union UserByIdResult = User |
    /// UserNotFound</c>; a mutation's payload gets <c>errors: [UpdateUserError!]</c>. Each error type implements
    /// <c>interface Error { message: String! }</c> and shows <c>message</c> only, unless
    /// <see cref="ResultfulGraphQLOptions.ExposeLeafFields"/> or <see cref="ResultfulGraphQLOptions.ExposeFieldsOf{TLeaf}"/>
    /// shows the case's own properties. A nested error union is shown as its leaves.
    /// </para>
    /// <para>
    /// A failure that isn't a declared case goes in the top-level <c>errors</c> with <c>extensions.code</c> set to its
    /// type name. Its message is the error's <see cref="Error.ToString"/> in the Development environment and
    /// <c>"An error occurred while processing your request."</c> everywhere else; it is logged at Error with
    /// <see cref="Error.ToString"/>. The field is then null, and null propagates as for any field error, as with
    /// <c>FieldResult</c>.
    /// </para>
    /// <para>
    /// Only typed resolvers are seen: the declared return type decides. An untyped code-first
    /// <c>Resolve(ctx =&gt; (object)result)</c> or a schema-first resolver isn't rewritten.
    /// </para>
    /// <para>
    /// Named <c>AddResultfulErrors</c>, not <c>AddResultful</c>, so it isn't mistaken for <c>Resultful.AspNetCore</c>'s
    /// <c>AddResultful</c> or <c>Resultful.Text.Json</c>'s, which isn't native-AOT safe. Each resolver's result type is
    /// read when the schema is built, as HotChocolate's conventions read <c>FieldResult</c>; the cases are read from
    /// <see cref="ResultfulGraphQLOptions.AddErrorCases{TError}"/>, with no reflection.
    /// </para>
    /// </remarks>
    /// <param name="builder">The GraphQL server's builder.</param>
    /// <param name="configure">Registers error cases and sets the options; may be null.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static IRequestExecutorBuilder AddResultfulErrors(
        this IRequestExecutorBuilder builder,
        Action<ResultfulGraphQLOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureSchema(schema =>
        {
            var options = schema.Features.GetOrSet<ResultfulGraphQLOptions>();
            configure?.Invoke(options);
        });

        // The conventions and the interceptor are added once however often this runs; the filter once per schema, so
        // an undeclared failure is logged once.
        builder.AddQueryConventions().TryAddTypeInterceptor<ResultfulTypeInterceptor>();
        var schemas = Registered.GetOrCreateValue(builder.Services);
        lock (schemas)
        {
            if (schemas.Add(builder.Name)) builder.AddErrorFilter(static services => new ResultfulErrorFilter(services));
        }

        return builder;
    }

    private static readonly ConditionalWeakTable<IServiceCollection, HashSet<string>> Registered = [];
}
