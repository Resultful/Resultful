using HotChocolate;

namespace Resultful.HotChocolate;

/// <summary>
/// The GraphQL <c>ValidationError</c> type: a validation failure, with the failing fields and their messages. A
/// <see cref="ValidationFailed{TError}"/>, or the errors of a failed <see cref="Validation{T, TError}"/>, becomes one.
/// </summary>
/// <remarks>
/// Like every error type, it implements the conventions' <c>Error</c> interface: <c>message</c> is
/// <c>"Validation failed."</c>. Each error is listed under the field <see cref="ResultfulGraphQLOptions.ValidationKey"/>
/// gives it, with its <see cref="Error.Message"/>; nothing else of the errors is shown.
/// </remarks>
[GraphQLName("ValidationError")]
[GraphQLDescription("A validation failure: the failing fields and their messages.")]
public sealed class ResultfulValidationError
{
    internal const string ValidationMessage = "Validation failed.";

    private ResultfulValidationError(IReadOnlyList<ResultfulValidationField> fields)
    {
        Fields = fields;
    }

    /// <summary>Always <c>"Validation failed."</c>, the message of a <see cref="ValidationFailed{TError}"/>.</summary>
    [GraphQLDescription("Always \"Validation failed.\"")]
    public string Message { get; } = ValidationMessage;

    /// <summary>The failing fields, in the order their first error appears. Never empty.</summary>
    [GraphQLDescription("The failing fields, in the order their first error appears.")]
    public IReadOnlyList<ResultfulValidationField> Fields { get; }

    /// <summary>Groups <paramref name="errors"/> by <paramref name="key"/>, keeping their order.</summary>
    internal static ResultfulValidationError From(IEnumerable<Error> errors, Func<Error, string> key)
    {
        var fields = new List<ResultfulValidationField>();
        var byKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var error in errors)
        {
            var name = key(error) ?? "";
            if (!byKey.TryGetValue(name, out var messages))
            {
                messages = [];
                byKey[name] = messages;
                fields.Add(new ResultfulValidationField(name, messages));
            }

            messages.Add(error.Message);
        }

        return new ResultfulValidationError(fields);
    }

    /// <summary>The default key, an error's type name, as <see cref="ResultfulGraphQLOptions.ValidationKey"/> starts.</summary>
    internal static string TypeName(Error error) => error.GetType().Name;

    /// <summary><c>true</c> for a closed <see cref="ValidationFailed{TError}"/>.</summary>
    internal static bool IsValidationFailed(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValidationFailed<>);
}

/// <summary>The GraphQL <c>ValidationFieldError</c> type: one failing field of a <see cref="ResultfulValidationError"/>.</summary>
[GraphQLName("ValidationFieldError")]
[GraphQLDescription("One failing field and the messages of its errors.")]
public sealed class ResultfulValidationField
{
    internal ResultfulValidationField(string field, IReadOnlyList<string> messages)
    {
        Field = field;
        Messages = messages;
    }

    /// <summary>The field's key, from <see cref="ResultfulGraphQLOptions.ValidationKey"/>.</summary>
    [GraphQLDescription("The field's key.")]
    public string Field { get; }

    /// <summary>The <see cref="Error.Message"/> of each error listed under the field, in order. Never empty.</summary>
    [GraphQLDescription("The message of each error listed under the field, in order.")]
    public IReadOnlyList<string> Messages { get; }
}
