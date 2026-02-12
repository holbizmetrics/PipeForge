# Graveyard — PipeForge

> No dead ends yet. Campaign is fresh.

## Entries

(none)

## PipeForge-Specific Failure Signatures to Watch For

These are failure patterns likely to appear in a DevOps tooling campaign. Not triggered yet — preloaded from DERIVE analysis as early warning.

### Software Engineering Signatures

| Signature | Pattern | Watch For |
|---|---|---|
| `PLATFORM-ASSUMPTION` | Code assumes one OS. Breaks on others. | FileSystemWatcher behaves differently on Windows/Linux/macOS. Path separators. Process killing. |
| `ABSTRACTION-LEAK` | Internal API leaks into user-facing surface. | PipelineRun internals leaking into YAML schema or CLI output. |
| `TEMPLATE-RIGIDITY` | Pre-built templates too opinionated, users can't customize. | InnoSetup template assumes specific directory structure. |
| `SECURITY-THEATER` | Warning message exists but doesn't actually prevent harm. | Trust notice prints but user can't distinguish safe from unsafe YAML. |
| `YAML-DIALECT-DRIFT` | PipeForge YAML diverges from GitLab/GitHub conventions, confusing users. | Our `steps` vs their `jobs`, our `breakpoint` key has no equivalent. |
| `DEBUGGER-COUPLING` | Feature only works in VS debugger, useless in console mode. | State inspection that only works via hover, not via CLI output. |

### Cross-Campaign Transfers (from math campaigns)

| Signature | Applies to PipeForge as |
|---|---|
| `WRONG-LEVEL` | Solving at wrong abstraction. e.g., fixing CLI UX when the Core engine has the bug. |
| `CONSTANT-NOT-STRUCTURAL` | Optimizing a constant (buffer size, debounce ms) when the architecture is the problem. |
