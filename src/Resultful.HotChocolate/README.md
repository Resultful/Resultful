# Resultful.HotChocolate

HotChocolate 16 for [Resultful](https://github.com/Resultful/Resultful): a resolver returns a result, and its error
cases become GraphQL types that clients query, with no exceptions thrown. It follows HotChocolate's own query and
mutation conventions for `FieldResult<T, E1..E5>`, without the five-error limit.

```csharp
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddMutationConventions()
    .AddResultfulErrors(o => o
        .AddErrorCases<UserError>(UserError.Cases)
        .ExposeFieldsOf<UserSuspended>());

public sealed class Query
{
    public Task<Result<User, UserError>> UserById(int id, Users users) => users.FindAsync(id);
}
```

```graphql
type Query { userById(id: Int!): UserByIdResult! }
union UserByIdResult = User | UserNotFound | UserSuspended
type UserNotFound implements Error { message: String! }
type UserSuspended implements Error { reason: String! message: String! }
interface Error { message: String! }
```

## What a resolver may return

`Result<T, TError>`, `Result<T>`, `Validation<T, TError>`, `Validation<T>`, a generated `[ResultShape]`, or a
`Task`/`ValueTask` of one. `AddResultfulErrors()` turns on `AddQueryConventions()`: a query field becomes `T | cases`.
A mutation's payload gets `errors: [RenameUserError!]`; call `AddMutationConventions()` (the schema build says so if
it's missing). A mutation returning `Result<Unit, TError>` answers a nullable `Boolean` (`true` on success), since
HotChocolate's conventions need a value; rename the payload field with `[UseMutationConvention(PayloadFieldName = …)]`.

A GraphQL union holds only object types, so a query field's success type must be one: a list, a scalar, an enum or
`Unit` fails the schema build with the reason (a mutation's payload field takes any type). `Result<User?, TError>`
makes the field's union nullable. Paging, projection, filtering and sorting can't wrap a result, nor can an explicit
`[GraphQLType]`, and a list of results isn't a field type: each fails the schema build. Results are detected from
typed resolvers only: an untyped code-first `Resolve(ctx => (object)result)` or a schema-first resolver isn't
rewritten.

## Error types

- The cases of a closed hierarchy or an error union come from the generator's `Cases`, registered with
  `AddErrorCases<TError>`: no reflection finds them. A concrete error type is its own case; a missing registration
  fails the schema build with the call to add. A nested union or hierarchy is shown as its leaves. Types named by
  `[Error<T>]` on the same field are declared too.
- Each error type shows `message` (`Error.Message`) only, by default. A leaf's own properties may hold personal data,
  so they stay on the server, as in `Error.ToString()` and `Resultful.AspNetCore`'s problems. Show them per leaf
  with `ExposeFieldsOf<TLeaf>()`, or for every leaf with `ExposeLeafFields`; `[GraphQLIgnore]` still hides one.
  `Context` and the cause chain never become fields; `IncludeDetails` adds `details` (`Error.ToString()`).
- A validation failure is `ValidationError { message fields { field messages } }`, keyed by `ValidationKey` (the
  error's type name by default), wherever the error can be one: `Result<T>`, a validation, or a union case.

## Names

GraphQL type names are global. Two leaves with the same name in different hierarchies (`Orders.NotFound`,
`Users.NotFound`), a leaf named `Error` (the conventions' interface) or `ValidationError` fail the schema build,
naming both CLR types. Rename a leaf with `[GraphQLName("OrderNotFound")]`, or move this package's validation type
with `ValidationErrorTypeName`.

## Failures that aren't declared

They go in the top-level `errors` with `extensions.code` set to the error's type name, and are logged at Error with
`Error.ToString()`. The message is that same text in Development and a fixed sentence everywhere else, as an HTTP
500 is. The field is then null, and null propagates as for any field error, as with `FieldResult`.

## FieldResult

`ToFieldResult<T, TError, TLeaf1..TLeaf5>()` converts a result for resolvers that already return `FieldResult`. It
has no options: a validation failure is always keyed by type name.

## Native AOT

Not checked for native AOT: each resolver's result type is read with reflection when the schema is built, as
HotChocolate's conventions read `FieldResult`. Only the cases are read without reflection.

Every `Resultful.*` package shares one version: use the same version of `Resultful` and `Resultful.HotChocolate`.
