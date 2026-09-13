# Contributing to ZeroSpace (Unity Client)

Thank you for contributing to ZeroSpace. This document explains the project's conventions, workflows, and expectations for code contributions to the Unity client.

## Purpose
ZeroSpace follows strict separation of concerns: Network -> Core -> UI/Animation. All gameplay logic and authoritative state live in the Backend. The Unity client is purely a presentation layer that connects to the Backend via WebSocket, receives JSON events, and updates UI/animations accordingly.

## How to contribute
- Fork the repository and create feature branches from `develop` using the naming pattern: `feature/<short-description>`.
- For bug fixes use `fix/<short-description>`. For experiments use `wip/<short-description>`.
- Open a Pull Request against `develop` and include a concise description and related issue number.

## Coding standards
- See `.editorconfig` at repository root for exact formatting rules. Key points:
  - Use spaces, 4-space indent.
  - CRLF line endings.
  - UTF-8 charset.
  - Max line length 120.
- C# style:
  - PascalCase for types and public members.
  - camelCase for private fields and parameters. Private backing fields start with an underscore (e.g., `_currentHealth`).
  - Explicit access modifiers on all members.
  - Prefer expression-bodied members where appropriate.

## Architecture and layering rules
- Network layer (`WebSocketManager.cs`) handles raw WebSocket connection and emits raw JSON strings only.
- Core (`GameManager.cs`) is responsible for parsing JSON, validating data, and dispatching typed events/models to UI and Animation layers.
- UI (`PlayerUI.cs`, `RoundManager.cs`) and Animation (`AnimationController.cs`) consume typed events from `GameManager` and must not perform network or JSON parsing.
- Do not duplicate game logic in client; the client must never be authoritative.

## Pull Request checklist
- [ ] Code compiles and runs in Unity 2020+ editor used by project.
- [ ] Follow `.editorconfig` and this CONTRIBUTING.md rules.
- [ ] Include unit tests or integration test steps when applicable.
- [ ] Update relevant documentation if public APIs or behaviors change.

## Commit message style
Use imperative, short subject line and optional body, e.g.:
```
Fix: correct HP interpolation in PlayerUI

Use smooth damping to avoid jitter when backend sends frequent updates.
```

## Running and formatting
- Use Visual Studio 2022 settings. Ensure you run the formatter and apply `.editorconfig` rules before committing.
- Visual Studio commands referenced in this document are formatted like `__CommandName__` when called out.

## License and code of conduct
Contributors must follow the repository license and code of conduct in the root of the project.

---
If you need to adjust any of these rules, propose changes via pull request and discuss in the PR.