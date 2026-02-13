# PipeForge

**Local pipeline engine with step-debugging. F5 + F10 through your build pipeline.**

## The Problem

Every CI/CD tool (GitLab, GitHub Actions, Azure DevOps) is fire-and-forget. You define a pipeline, push it, wait, read logs. If something breaks, you add `echo` statements and push again. Repeat until it works.

PipeForge runs pipelines *locally* as a debuggable program. Every step is an inspectable object. Set a breakpoint, see the full state, step through — like the VS debugger, but for your CI/CD.

## Quick Start

### GUI (Recommended)

```bash
dotnet run --project PipeForge.GUI
```

- **Templates** — Browse pre-built pipeline templates, preview YAML, create from template
- **Editor** — New / Open / Save / Save As pipeline YAML with syntax highlighting and live validation
- **Run** — Load a pipeline, see step progress, toggle breakpoints, step through execution

### CLI

```bash
# Create a pipeline from a template
pipeforge init innosetup -o my-pipeline.yml

# Run with step-debugging
pipeforge run my-pipeline.yml --interactive

# Run with file watching (auto-rerun on changes)
pipeforge run my-pipeline.yml --watch

# Both
pipeforge run my-pipeline.yml -i -w
```

## The Step Debugger

### GUI Mode

The Run view provides a VS debugger-like experience:

- **Step Progress Panel** — See all steps, their status (pending/running/success/failed), duration, exit code
- **Breakpoints** — Double-click any step to toggle a breakpoint (red dot). YAML `breakpoint: always` is also honored.
- **Debug Controls** — Continue (F5), Step Next (F10), Skip, Retry, Abort — always visible in the toolbar
- **Source Panel** — Toggle the Source button to see the pipeline YAML with the current step highlighted
- **Output Log** — Real-time stdout/stderr from running processes
- **Restart** — Re-run the loaded pipeline without opening the file picker again
- **Recent Files** — Sidebar shows recently opened pipelines for quick access

### Console Mode (--interactive)

Each step pauses before execution. You see the full state and choose:
- **Continue** — run this step
- **Skip** — skip it, move to next
- **Retry** — re-run the last step (useful after fixing something)
- **Abort** — stop the pipeline

### Visual Studio Mode

Open the solution. Set a breakpoint on `PipelineEngine.ExecuteStepAsync`. Press F5.

Now F10 through your pipeline. Hover over `run` to see:
- All variables and their current values
- Every step result so far (stdout, stderr, exit codes, timing)
- Artifacts produced
- The exact command about to execute

## Templates (Building Blocks)

```bash
pipeforge templates       # List all available templates
pipeforge init dotnet     # .NET build + test + publish
pipeforge init innosetup  # Inno Setup compiler + signing
pipeforge init security   # SBOM + vulnerability scan (Syft/Grype)
pipeforge init twincat    # TwinCAT/PLC build
pipeforge init custom     # Empty scaffold
```

Templates are pre-filled — swap a few paths and you're running.

### Composing Templates (Code)

```csharp
var build = PipelineTemplates.DotNetBuild("MyApp", "./MyApp.sln");
var scan  = PipelineTemplates.SecurityScan("MyApp", "./src");
var full  = PipelineTemplates.Compose("Full Pipeline", "Build + Scan", build, scan);

await engine.RunAsync(full, interactive: true);
```

## Pipeline YAML Format

```yaml
name: My Pipeline
description: What this does
working_directory: .

variables:
  MY_VAR: my_value

watch:
  - path: ./src
    filter: "*.cs"
    include_subdirectories: true
    debounce_ms: 2000

stages:
  - name: build
    steps:
      - name: Compile
        command: dotnet
        arguments: build -c Release
        timeout_seconds: 300
        breakpoint: on_failure    # Pause on failure for inspection
        artifacts:
          - ./bin/Release/**

  - name: test
    steps:
      - name: Run Tests
        command: dotnet
        arguments: test --no-build
        breakpoint: always        # Always pause before this step
```

### Breakpoint Modes

| Mode | Behavior |
|------|----------|
| `never` | Default. No pausing. |
| `always` | Always pause before this step. |
| `on_failure` | Pause only if this step fails. |

## Architecture

```
PipeForge.sln
├── PipeForge.Core/          # The engine — reusable library
│   ├── Models/              # Pipeline definitions + runtime state
│   ├── Engine/              # Executor, process wrapper, YAML loader
│   ├── Watcher/             # FileSystemWatcher integration
│   └── Templates/           # Pre-built pipeline building blocks
├── PipeForge.GUI/           # Avalonia desktop app (debug UI)
│   ├── Views/               # Templates, Editor, Run views
│   ├── ViewModels/          # MVVM with CommunityToolkit.Mvvm
│   └── Converters/          # Value converters for UI bindings
├── PipeForge.Runner/        # CLI tool (pipeforge.exe)
└── PipeForge.Tests/         # xUnit tests (62 tests)
```

The Core library is independent — embed it in your own tools, wrap it in a UI, use it from tests.

## Extending

### Custom Templates

```csharp
public static PipelineDefinition MyCustomPipeline(string projectName)
{
    return new PipelineDefinition
    {
        Name = $"{projectName} - Custom",
        Stages = new List<PipelineStage>
        {
            // Your stages here
        }
    };
}
```

### Events

```csharp
engine.OnBeforeStep += (sender, args) =>
{
    // Log, approve, notify, inspect — whatever you need
    Console.WriteLine($"About to run: {args.Step.Name}");

    // Control execution:
    args.Action = DebugAction.Continue;  // or Skip, Retry, Abort
};

engine.OnAfterStep += (sender, args) =>
{
    // Collect metrics, send notifications, update dashboards
};

engine.OnOutput += (sender, line) =>
{
    // Real-time stdout/stderr from the running process
};
```

## Requirements

- .NET 10.0 SDK
- Windows (primary target) / Linux / macOS

## License

MIT
