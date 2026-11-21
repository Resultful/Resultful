using System.Reflection;
using HotChocolate;
using HotChocolate.Configuration;
using HotChocolate.Features;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace Resultful.HotChocolate;

/// <summary>
/// Makes a field that returns a result behave as one returning HotChocolate's <c>FieldResult</c>: the field's type
/// becomes the success type, each declared case is added as an error type (as <c>[Error&lt;T&gt;]</c> would), and a
/// middleware next to the resolver unwraps success to the value and a declared failure to its error object. The query
/// and mutation conventions then build the union or the payload's <c>errors</c> from those error types.
/// </summary>
/// <remarks>
/// Only typed resolvers are seen: a resolver's declared return type decides, so an untyped code-first
/// <c>Resolve(ctx =&gt; (object)result)</c> or a schema-first resolver isn't rewritten.
/// </remarks>
internal sealed class ResultfulTypeInterceptor : TypeInterceptor
{
    private const string MiddlewareKey = "Resultful.Result";
    private const string DetailsField = "details";

    // Middleware that reshape a list field's value, which a union member can't be.
    private static readonly string[] DataMiddleware = ["Paging", "Projection", "Filtering", "Sorting"];

    private readonly Dictionary<ObjectFieldConfiguration, Pending> _pending = [];
    private readonly Dictionary<MemberInfo, Pending> _pendingByMember = [];
    private readonly Dictionary<string, Type> _errorNames = new(StringComparer.Ordinal);
    private ObjectTypeConfiguration? _mutation;

    public override void OnBeforeRegisterDependencies(ITypeDiscoveryContext discoveryContext, TypeSystemConfiguration configuration)
    {
        if (configuration is not ObjectTypeConfiguration type) return;
        var context = discoveryContext.DescriptorContext;
        var options = context.Features.GetOrSet<ResultfulGraphQLOptions>();

        if (type.RuntimeType == typeof(ResultfulValidationError))
        {
            type.Name = options.ValidationErrorTypeName;
            CheckName(discoveryContext, options.ValidationErrorTypeName, type.RuntimeType);
        }
        else if (type.RuntimeType != typeof(Error) && typeof(Error).IsAssignableFrom(type.RuntimeType))
        {
            ShowErrorFields(discoveryContext, type, options);
            CheckName(discoveryContext, string.IsNullOrEmpty(type.Name) ? context.Naming.GetTypeName(type.RuntimeType, TypeKind.Object) : type.Name, type.RuntimeType);
        }

        foreach (var field in type.Fields)
        {
            if (field.IsIntrospectionField) continue;
            var coordinate = $"{type.Name}.{field.Name}";
            if (ResultField.TryCreate(field.ResultType) is not { } result)
            {
                if (ResultField.ElementOfList(field.ResultType) is { } element && ResultField.TryCreate(element) is not null)
                {
                    Report(discoveryContext, $"The field {coordinate} returns a list of results. A GraphQL union can't be a list item's own outcome: return one result whose value is the list, or put each result on an object type's field.");
                }

                continue;
            }

            if (field.Type is not ExtendedTypeReference typeRef || typeRef.Type.Type != ResultField.Unwrap(field.ResultType!))
            {
                Report(discoveryContext, $"The field {coordinate} returns a result but declares its own GraphQL type. Remove the explicit type: the success type and the error cases decide the field's type.");
                continue;
            }

            if (field.MiddlewareConfigurations.FirstOrDefault(m => m.Key is { } key && DataMiddleware.Any(d => key.Contains(d, StringComparison.Ordinal))) is { } data)
            {
                Report(discoveryContext, $"The field {coordinate} returns a result and uses {data.Key}. Paging, projection, filtering and sorting can't wrap a result: put them on a field that returns the list itself.");
                continue;
            }

            var isUnit = result.ValueType == typeof(Unit);
            var (success, nullable) = isUnit ? (TypeReference.Parse("Boolean"), true) : SuccessType(context, typeRef, result);
            field.Type = success;
            if (DeclaredCases.For(result, options, out var problem) is not { } cases)
            {
                Report(discoveryContext, $"The field {coordinate} returns a result, but {problem}");
                continue;
            }

            // Types named by [Error<T>] on the same field are already error types: a failure of one is returned as it.
            var attributed = ErrorAttributeTypes(field.Member);
            foreach (var schemaType in cases.SchemaTypes)
            {
                if (!attributed.Contains(schemaType)) field.AddErrorType(context, schemaType);
            }

            cases = cases.WithLeaves(attributed);
            field.MiddlewareConfigurations.Add(new FieldMiddlewareConfiguration(Unwrap(result.Reader, cases, options.ValidationKey), false, MiddlewareKey));
            var pending = new Pending(coordinate, success, nullable, isUnit);
            _pending[field] = pending;
            if (field.Member is { } member) _pendingByMember[member] = pending;
        }
    }

    public override void OnAfterResolveRootType(
        ITypeCompletionContext completionContext,
        ObjectTypeConfiguration configuration,
        OperationType operationType)
    {
        if (operationType != OperationType.Mutation) return;
        _mutation = configuration;

        // Without the mutation conventions nothing would show a mutation's declared cases: say so while building.
        if (completionContext.DescriptorContext.Features.TryGet<MutationConventionOptions>(out _)) return;
        foreach (var field in configuration.Fields)
        {
            if (Find(field) is { } pending)
            {
                completionContext.ReportError(SchemaErrorBuilder.New()
                    .SetMessage($"The mutation {configuration.Name}.{field.Name} returns a result, whose error cases are shown by the mutation conventions: call AddMutationConventions().")
                    .Build());
            }
        }
    }

    // A query field is a union of the success type and the error types, after the query conventions ran: check that
    // the success type can be a member, and keep a nullable success nullable.
    public override void OnBeforeCompleteType(ITypeCompletionContext completionContext, TypeSystemConfiguration configuration)
    {
        if (configuration is not ObjectTypeConfiguration type || ReferenceEquals(type, _mutation) || (_mutation is not null && type.Name == _mutation.Name)) return;
        foreach (var field in type.Fields)
        {
            if (Find(field) is not { } pending) continue;
            if (pending.IsUnit)
            {
                Report(completionContext, $"The field {pending.Coordinate} returns Result<Unit, …>, which has no value to show. A query field needs a value; a mutation with the mutation conventions answers a nullable Boolean.");
                continue;
            }

            if (!completionContext.TryGetType<IType>(pending.Success, out var success)) continue;
            var named = success is NonNullType nonNull ? nonNull.NullableType : success;
            if (named is not ObjectType)
            {
                Report(completionContext, $"The field {pending.Coordinate} returns a result whose success type is {success.Print()}, but a GraphQL union holds only object types. Wrap the value in an object type, such as a payload with a field for it.");
                continue;
            }

            if (pending.Nullable && field.Type is SchemaTypeReference { Type: NonNullType { NullableType: { } union } })
            {
                field.Type = TypeReference.Create(union);
            }
        }
    }

    private Pending? Find(ObjectFieldConfiguration field) =>
        _pending.TryGetValue(field, out var pending) || (field.Member is { } member && _pendingByMember.TryGetValue(member, out pending)) ? pending : null;

    // The success type keeps HotChocolate's nullability and list shape, read from the result's type arguments as its
    // FieldResult conventions do; a shape has no type arguments, so its success type is read from the CLR type.
    private static (TypeReference Type, bool Nullable) SuccessType(IDescriptorContext context, ExtendedTypeReference typeRef, ResultField result)
    {
        if (typeRef.Type.IsGeneric && typeRef.Type.TypeArguments.Count > 0 && ResultField.IsCoreResult(typeRef.Type.Type))
        {
            var success = typeRef.Type.TypeArguments[0];
            return (typeRef.WithType(success), success.IsNullable);
        }

        return (context.TypeInspector.GetOutputTypeRef(result.ValueType), false);
    }

    private static HashSet<Type> ErrorAttributeTypes(MemberInfo? member)
    {
        var types = new HashSet<Type>();
        if (member is null) return types;
        foreach (var attribute in member.GetCustomAttributes<ErrorAttribute>(inherit: true))
        {
            if (typeof(Error).IsAssignableFrom(attribute.ErrorType) && !attribute.ErrorType.IsAbstract) types.Add(attribute.ErrorType);
        }

        return types;
    }

    private void CheckName(ITypeDiscoveryContext context, string name, Type runtimeType)
    {
        if (name == "Error")
        {
            Report(context, $"The error type {runtimeType.FullName} is named Error, which the conventions' Error interface already uses. Rename it with [GraphQLName].");
        }
        else if (_errorNames.TryGetValue(name, out var other) && other != runtimeType)
        {
            var hint = other == typeof(ResultfulValidationError) || runtimeType == typeof(ResultfulValidationError)
                ? "Rename the app's error with [GraphQLName], or set ValidationErrorTypeName."
                : "Rename one with [GraphQLName].";
            Report(context, $"The error types {other.FullName} and {runtimeType.FullName} are both named {name} in GraphQL. {hint}");
        }
        else
        {
            _errorNames[name] = runtimeType;
        }
    }

    // A leaf shows `message` only, unless the app exposes its own fields. Context, Cause, Inner() and Causes() always
    // stay on the server; IncludeDetails adds `details`: Error.ToString(), never a leaf's own properties.
    private static void ShowErrorFields(ITypeDiscoveryContext context, ObjectTypeConfiguration type, ResultfulGraphQLOptions options)
    {
        var expose = options.ExposesFieldsOf(type.RuntimeType);
        for (var i = type.Fields.Count - 1; i >= 0; i--)
        {
            if (type.Fields[i].Member is not { } member) continue;
            if (IsErrorMember(member))
            {
                if (member.Name != nameof(Error.Message)) type.Fields.RemoveAt(i);
            }
            else if (!expose)
            {
                type.Fields.RemoveAt(i);
            }
            else if (AbstractError(member) is { } shown)
            {
                Report(context, $"The error type {type.RuntimeType.Name} shows {member.Name}, typed as {shown.Name}, which GraphQL can't show as a type. Hide it with [GraphQLIgnore], or don't expose {type.RuntimeType.Name}'s fields.");
            }
        }

        if (options.IncludeDetails && !type.Fields.Any(f => f.Name == DetailsField))
        {
            type.Fields.Add(new ObjectFieldConfiguration(
                DetailsField,
                "The error's type, message, context and cause chain, as Error.ToString() writes them.",
                TypeReference.Parse("String!"),
                pureResolver: static c => c.Parent<Error>().ToString()));
        }
    }

    // Declared on Error, or an override of one of its members (a leaf's own Inner()).
    private static bool IsErrorMember(MemberInfo member)
    {
        if (member.DeclaringType == typeof(Error)) return true;
        var method = member switch
        {
            MethodInfo m => m,
            PropertyInfo p => p.GetMethod,
            _ => null,
        };
        return method?.GetBaseDefinition().DeclaringType == typeof(Error);
    }

    // The abstract error type a member shows, itself or as a list's item: GraphQL would need an object type for it.
    private static Type? AbstractError(MemberInfo member)
    {
        var type = member switch
        {
            PropertyInfo p => p.PropertyType,
            MethodInfo m => m.ReturnType,
            _ => null,
        };
        if (type is null) return null;
        var item = ResultField.ElementOfList(type) ?? type;
        return typeof(Error).IsAssignableFrom(item) && item.IsAbstract ? item : null;
    }

    private static void Report(ITypeSystemObjectContext context, string message) =>
        context.ReportError(SchemaErrorBuilder.New().SetMessage(message).Build());

    private static FieldMiddleware Unwrap(ResultReader reader, DeclaredCases cases, Func<Error, string> key) =>
        next => async context =>
        {
            await next(context).ConfigureAwait(false);
            if (context.Result is null) return;
            context.Result = reader.TryRead(context.Result, out var value, out var failure)
                ? value is Unit ? true : value
                : new FieldError(cases.ToGraphQL(failure, key));
        };

    private sealed record Pending(string Coordinate, TypeReference Success, bool Nullable, bool IsUnit);
}
