# MEMORY — PipeForge Campaign

## What This Project IS

PipeForge is a local pipeline engine built in C# that solves one problem: **you can't debug CI/CD pipelines**. GitLab, GitHub Actions, Azure Pipelines — all fire-and-forget. You push, wait, read logs. PipeForge makes pipeline execution step-debuggable via the .NET debugger.

**Core innovation:** Pipeline steps are C# method calls. Set breakpoint on `PipelineEngine.ExecuteStepAsync`, hit F5, F10 through your build. Hover over `PipelineRun` to see full state. No custom debugging protocol — Visual Studio provides it free.

## Architecture (Locked)

```
PipeForge.sln
├── PipeForge.Core/           # Reusable engine library (THE product)
│   ├── Models/               # PipelineDefinition, PipelineRun, StepResult
│   ├── Engine/               # PipelineEngine, ProcessWrapper, PipelineLoader
│   ├── Watcher/              # PipelineWatcher (FileSystemWatcher wrapper)
│   └── Templates/            # PipelineTemplates (building blocks)
├── PipeForge.Runner/         # CLI tool (pipeforge.exe)
├── PipeForge.Tests/          # xUnit tests
└── examples/                 # Sample YAML pipelines
```

**Key classes:**
- `PipelineEngine`: Executes steps, fires OnBeforeStep/OnAfterStep events. THE debugging surface.
- `PipelineRun`: Complete execution state. Variables, step results, artifacts, timing. THE inspection target.
- `StepResult`: Single step outcome. Stdout/stderr captured, exit code, duration.
- `PipelineTemplates`: Static methods returning pre-configured pipelines. Building blocks.

**Dependencies:** .NET 8, C# 10, YamlDotNet, System.CommandLine

## Known Issue (Fixed)

`Environment.NewLine` resolved to `Dictionary<string, string> Environment` property on PipelineRun instead of `System.Environment`. Fix: fully qualify as `System.Environment.NewLine` or rename property to `EnvironmentVariables`.

## DERIVE Findings (Converged — 3 passes)

### Critical (before v1)
1. YAML comments in generated templates — `pipeforge init` output must be self-documenting
2. `pipeforge validate` command — lint without running
3. Richer error messages — last 10 stderr lines + "did you mean?" suggestions
4. Security warning — on first run of unknown YAML, print trust notice
5. Path normalization — all variable resolution normalizes paths per OS

### Important (v1.1)
6. --verbose/--quiet flags
7. Watch mode notification (terminal bell + optional system toast)
8. Template versioning (version: field)
9. Single-file publish (one .exe)
10. FileSystemWatcher hardening

### v2
11. Schema mapping layer (abstract representation ↔ GitLab/GitHub/Azure)
12. pipeforge import/export commands
13. Template registry (local + remote)

### v3
14. Avalonia GUI (visual pipeline builder, live run view, template browser)

## INHABIT Findings (3 friction points)

1. **Bare YAML:** User opens pipeforge init output, sees raw YAML with no comments. Doesn't know what breakpoint: on_failure means. Closes file, returns to GitLab. → T-001 fixes this.
2. **Opaque errors:** Pipeline fails at breakpoint, shows "Process exited with code 1". Stderr exists but user doesn't see it. → T-003 fixes this.
3. **Silent completion:** Watch mode runs, user looks away, comes back — did it succeed? → T-009 fixes this.

## Design Principles

- **"Failure is the feature"** — Debugging IS the product. Error paths are the primary UX.
- **"Speaks your dialect"** — YAML shape familiar to GitLab/GitHub/Azure users.
- **"Building blocks"** — Templates snap together. Zero-to-pipeline in 30 seconds.
- **Core/Runner split** — Engine is reusable. CLI is one frontend. GUI is another. Both consume same library.

## Roadmap

- **v1:** Local engine + CLI + templates + Critical 5. Solves "can't debug my pipeline."
- **v2:** Import/export. `pipeforge import .gitlab-ci.yml`. Translation layer speaking everyone's dialect.
- **v3:** Avalonia GUI. Visual pipeline builder. Drag building blocks. Live run view. Cross-platform.
