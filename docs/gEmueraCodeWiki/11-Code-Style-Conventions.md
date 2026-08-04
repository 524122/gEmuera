# Code Style Conventions

## Purpose

This repository has two maintained C# code boundaries. The conventions below make
new work consistent without rewriting upstream-compatible code or changing the
legacy runtime path.

The executable policy lives in the repository-root `.editorconfig`; this document
explains when each policy applies. Root-level `Scripts/*.cs` files are intentionally
not indentation-enforced until their focused migration work is scheduled, preventing
a repository-wide formatting rewrite from obscuring functional changes.

## Scope

| Area | Style | Naming boundary |
| --- | --- | --- |
| `Scripts/Emuera/**` | Tabs and existing block namespaces | Preserve `MinorShift.Emuera.*` names and upstream identifiers. |
| `Scripts/uEmuera/**` | Tabs and existing compatibility style | Preserve the `uEmuera.*` shim boundary. |
| `Scripts/GodotHost/**`, `Scripts/Diagnostics/**`, `Scripts/M0/**` | Four spaces for new or touched code | Use their declared `gEmuera.*` namespaces, PascalCase exposed symbols, and `_camelCase` non-public fields. |
| Root `Scripts/*.cs` host scripts | Existing tabs until focused migration work is scheduled | Preserve local formatting; use modern names for new symbols. |
| `src/Core/**` | Four spaces and file-scoped namespaces where practical | Use `GEmuera.Core.*`; it must remain free of Godot references. |
| `tools/core-contracts/**` | Four spaces and file-scoped namespaces where practical | Follow Core naming and dependency boundaries. |
| `addons/**` | Vendor formatting | Do not perform repository-wide formatting or naming changes. |

## Naming Rules

- Classes, structs, enums, interfaces, methods, properties, events, and delegates
  exposed outside their declaring type use PascalCase. Interfaces start with `I`.
- Non-public fields in modern code use `_camelCase`; locals and parameters use
  `camelCase`; generic parameters use `TPascalCase`.
- New asynchronous methods end in `Async` and accept `CancellationToken` when they
  can be cancelled by their caller.
- Use names that encode the owned domain rather than generic `Manager` classes.
  Keep Godot types in host or bridge code and Core types in `src/Core`.

## File and Code Organization

- Keep one primary type per new file and name the file after that type.
- Order members as constants/static fields, instance fields, properties,
  constructors, public methods, private methods, then nested types.
- Keep `using` directives outside namespaces and order `System` imports first.
- Update the relevant Code Wiki page and regenerate `10-Source-Index.md` whenever
  C# files or primary type locations change.

## Compatibility Guardrails

- Do not rename or move public legacy APIs, scene-bound class names, or
  `MinorShift.Emuera.*` and `uEmuera.*` namespaces as a cleanup task.
- Do not reformat `Scripts/Emuera`, `Scripts/uEmuera`, or `addons` en masse.
  Those areas retain their styles for upstream and vendor synchronization.
- Do not remove legacy runners, renderers, Parser/VM paths, fixtures, or evidence
  documents without the gate decision required by `docs/NewFrameworkDesign/`.
- Formatting and naming analyzer diagnostics remain suggestions, so they guide new
  work without changing current build gates.
