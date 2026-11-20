---
type: decision
title: "Operators are opt-in sugar over named methods"
description: "C# 14 extension operators live in a Resultful.Operators namespace, and every operator has a named equivalent."
status: proposed
tags: [resultful, decision, language]
decided:
  by: ""
  at: 2026-11-20
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# 9. Operators are opt-in sugar over named methods

Resultful adds C# 14 extension operators in a `Resultful.Operators` namespace:

- `+ - *` (and possibly `/`) combine successes with the value's own operator and accumulate errors.
- `|` falls back, and `||` falls back lazily.
- `>>` binds, one type-changing overload only.
- `&` combines into a tuple.
- The same operators work on `Task<Result<T>>`.

Each operator is a one-line call to a named method (`Plus`, `Bind`, `Map`, `OrElse`, `ValueOr`), and the named
method is the API: it is documented, tested and tracked in `PublicAPI`.

Why:
- Operators make the railway read like arithmetic and control flow, which is the most visible step towards
  feeling native.
- Some C# developers will read `if (result)` or `>>` as hidden behaviour, so the default namespace stays
  named methods and teams opt in with one `using`.
- Older compilers and other .NET languages can't see extension operators, so the named methods have to exist
  anyway.

Consequences:
- Generic-math operators exist only on NET7+, so the public API files become per-target.
- `&&` isn't offered, because `&` is the tuple combine.
- The evidence is in [Seamless with .NET](../explorations/seamless-with-dotnet.md).
