---
okf_version: 0.2
type: index
title: "Resultful documentation"
description: "The Resultful knowledge bundle: vision, roadmap, decisions and log."
status: stable
tags: [resultful, index]
generated:
  by: claude-opus-5.5
  at: 2026-11-20
---

# Resultful documentation

This folder is an [Open Knowledge Format](https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/main/okf/SPEC.md)
(OKF 0.2) bundle: Markdown files with YAML frontmatter, linked into a graph that people and agents read the same
way. Every file has a `type` and says who wrote it (`generated.by`); a decision also says who made it
(`decided.by`). Agent-written content that a person has checked carries `verified`.

## Start here

| | |
|---|---|
| [Vision](vision.md) | Why Resultful exists, what it is and is not, who it is for |
| [Roadmap](roadmap.md) | Long-term milestones to 1.0 and beyond, and the assertions track |
| [Architecture](architecture.md) | The package family across both repositories, dependency rules, layout |
| [Decisions](decisions/index.md) | Every decision, dated, with why |
| [Log](log.md) | What changed in these documents, by date |
| [Seamless with .NET](explorations/seamless-with-dotnet.md) | Exploration: operators, unions, nullable flow and framework integration |
| [One error or many](explorations/one-error-or-many.md) | Exploration: `Result` and `Validation`, and an `Error` type that earns its place |
| [Error handling: prior art](references/error-handling-prior-art.md) | Reference: how other .NET libraries and languages shape and compose errors, and what Resultful took |

The library's usage guide is the [README](../README.md); release history is in
[RELEASE_NOTES.md](../RELEASE_NOTES.md).
