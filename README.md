# OneOf.ROP
Utility classes for Railway Oriented Programming using OneOf a Discriminated Union library.

Inspired by this article on [F# for fun and profit](https://fsharpforfunandprofit.com/posts/recipe-part2/)
Designed to be consumed for C#.

## Types

There are 5 fundamental types in this library

1. `Result<T, TError>` - Union of `T` and `TError`
2. `Result<T>` - Union of `T` and `IEnumerable<string>`
3. `VoidResult<TError>` - Union of `Unit` and `TError`
4. `VoidResult` - Union of `Unit` and `IEnumerable<string>`
5. `Option<T>` - Union of `T` and `None`

## Building

There are implicit converters to each of the result types from their Union element, everything other than Result<T> which does not have a successful value implicit converter because it would interfere with any Results that could also be interpreted as an error such as `string`, `IEnumerable<string>`.

## Operations

I am going to write the majority of the examples against `Result<T, TError>` as it is the most generic type, but the idea is applicable to all types where.
1. `Result<T>` = `Result<T, IEnumerable<string>>`
2. `VoidResult<TError>` = `Result<Unit, TError>`
3. `VoidResult` = `Result<Unit, IEnumerable<string>>`
4. `Option<T>` = `Result<T, None>`

### Map

Takes a `Result<T, TError>` and a function which accepts the success type and returns a type of your choosing `Func<T, TResult>`.
This then returns a `Result<TResult, TError>`and the function is only invoked if the input result was a success value.

### Bind

Takes a `Result<T, TError>` and a function which accepts the success type and returns a new `Result<TResult, TError>` of your choosing.
This then returns a `Result<TResult, TError>` and the function is only invoked if the input result was a success value.

### Tee

Takes a `Result<T, Error>` and an action which accepts the success type but does not return anything.
This returns the same input but has allowed an arbitrary piece of code to run against the successful value.

### Plus

Allows addition of two or more `Result<T, TError>` values, functions are passed in to add T, TError together.
If you add two results together of two different types for example `T1` and `T2` you can provide a `Func<T1, T2, TResult>` which will make the plus function return a `Result<TResult, TError>` if both values where successful.
If you do not want to provide a plus function upfront for your success values then the result will be a tuple of the result types `Result<(T1, T2), TError>` up to 8 values added together, You could then use a map to provide your addition function. Which might prove more readable for most people.

### Fold

This is a way to take an `IEnumerable<Result<T, TError>>` and return either `Result<T, TError>` or `Result<TResult, TError>` based on what you want for the result.
You always provide an accumulator function which should be `Func<TResult, T, TResult>` or `Func<T, T, T>`

## IPlus

When adding two or more items together of the same type. You have the option to pass in a addition function for both `TError` type and `T`, but as an alternative to this you can add the `IPlus<>` Interface to your type.
This is useful for creating custom Error types adding `IPlus<TError>` to the new type. This will reduce the amount of boilerplate when adding together multiple Results with the same error type.
Or anywhere that accepts `Func<T, T, T>` is typically written to remove that argument whenever IPlus is implemented for that type.

## Async

There are Async operations available which can either accept the input as a Task and/or the `Func` given returns a `Task`. These should be available for all standard operations of the library such as `Map`, `Bind`, `Tee`.
This has not been implemented for Plus or Fold as the Task could actually be in a variety of locations on the arguments. Everything desired could be achieved using other methods in the library or using methods in the TPL.
Also some special ones unique for Async.
