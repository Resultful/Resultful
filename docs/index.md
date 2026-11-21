---
okf_version: 0.2
type: index
title: "Resultful documentation"
description: "Human-first documentation hub for Resultful, with direct backdoors to architecture decisions and agent specs."
status: stable
tags: [resultful, index]
generated:
  by: agent:antigravity
  at: 2026-11-21
---

# Resultful Documentation

Typed, zero-allocation error handling for .NET — structured for fast human onboarding with deep specification backdoors for maintainers and AI agents.

## Start here

| Guide | What you'll learn |
|---|---|
| [Quick Start](guides/quickstart.md) | Model domain errors, compose fail-fast (`Result`) vs. accumulating (`Validation`), and map cleanly at HTTP/GraphQL boundaries |
| [Comparing Resultful](guides/comparing-resultful.md) | Honest trade-offs vs. exceptions, `OneOf`, `ErrorOr`, `FluentResults`, and `LanguageExt` — plus when *not* to use Resultful |
| [Using Resultful with AI Agents](guides/using-with-ai-agents.md) | Copyable `AGENTS.md` / `CLAUDE.md` rules so coding agents write idiomatic Resultful in your app |

## Explanation

| Page | Focus |
|---|---|
| [Vision](vision.md) | Why Resultful exists, what it is and is not, and who it is for |
| [Architecture](architecture.md) | The 4-layer package family, zero-dependency core, and satellite rules |
| [Roadmap](roadmap.md) | Milestones to 0.3, 1.0, and beyond |

## Agent & maintainer backdoors

Deep specifications are intentionally kept out of the primary getting-started flow (`docs/config.json`) while remaining readable raw in git and indexable for agents:

| Backdoor | Contents |
|---|---|
| [AGENTS.md](../AGENTS.md) | Repo build (`Cake.Sdk`), testing, PublicAPI, and PR guardrails |
| [Decisions (`0001`–`0021`)](decisions/index.md) | Dated architectural decisions with rationale and trade-offs |
| [Explorations](explorations/seamless-with-dotnet.md) | Deep design notes (operators, unions, generator internals) |
| [Log](log.md) | Chronological documentation change log |
| [Release Notes](../RELEASE_NOTES.md) | Temporal release stream (separate from evergreen guides) |
