---
okf_version: 0.2
type: guide
title: "Quick Start"
description: "Build your first vertical slice with Resultful: typed domain errors, Result vs. Validation, and boundary mapping."
status: stable
tags: [resultful, guide, quickstart]
generated:
  by: agent:antigravity
  at: 2026-11-21
---

# Quick Start

Install the dependency-free core and (recommended) the Roslyn analyzers:

```bash
dotnet add package Resultful
dotnet add package Resultful.Analyzers
```

## 1. Model failures as data

Define a closed hierarchy of domain errors inheriting from `Error`. Keep user-safe text in `Message` and internal diagnostics in `WithContext(...)`:

```csharp
[ClosedHierarchy]
public abstract partial record OrderError(string Message) : Error(Message)
{
    public sealed record NotFound(int OrderId) : OrderError($"Order {OrderId} was not found.");
    public sealed record Locked(int OrderId, string Owner) : OrderError($"Order {OrderId} is locked.");
}
```

## 2. Fail fast (`Result`) vs. collect everything (`Validation`)

Use **`Validation<T, TError>`** at the input edge to gather every validation error in one pass, then switch to **`Result<T, TError>`** inside the domain to short-circuit on the first failure:

```csharp
Validation<OrderRequest> validated = ValidateCustomer(cmd)
    .Combine(ValidateItems(cmd), (customer, items) => new OrderRequest(customer, items));

Result<Order, OrderError> order = validated
    .AsResult()
    .Bind(ReserveInventory)
    .Bind(ChargeCard)
    .WithContext($"checkout {cmd.OrderId}");
```

<!-- widget: resultful-railway -->
> **Mental model:** On `Result.Bind`, the first error switches to the failure track and skips remaining steps while preserving `WithContext` breadcrumbs. On `Validation.Combine`, every branch runs and all errors accumulate into `ValidationFailed`.

## 3. Handle every case at the boundary

Map domain errors once at your HTTP, GraphQL, or CLI boundary — leaf properties and internal `Context` are never leaked by default:

```csharp
app.MapPost("/orders", (OrderCommand cmd) => Checkout(cmd).ToHttpResult());
```

<!-- widget: resultful-error-boundary -->

## Next steps

- [Comparing Resultful](comparing-resultful.md)
- [Using Resultful with AI Agents](using-with-ai-agents.md)
- [Architecture](../architecture.md)
