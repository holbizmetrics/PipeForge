# PipeForge

**Local pipeline engine with step-debugging. F5 + F10 through your build pipeline.**

## The Problem

Every CI/CD tool (GitLab, GitHub Actions, Azure DevOps) is fire-and-forget. You define a pipeline, push it, wait, read logs. If something breaks, you add `echo` statements and push again. Repeat until it works.

PipeForge runs pipelines *locally* as a debuggable C# program. Every step is an inspectable object. Set a breakpoint, see the full state, step through.

## Quick Start

```bash
# Create a pipeline from a template
pipeforge init innosetup -o my-pipeline.yml

# Edit the generated YAML (swap paths, adjust steps)

# Run with step-debugging
pipeforge run my-pipeline.yml --interactive

# Run with file watching (auto-rerun on changes)
pipeforge run my-pipeline.yml --watch

# Both
pipeforge run my-pipeline.yml -i -w
```

## The Step Debugger

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

This is what no other tool gives you.

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
├── PipeForge.Runner/        # CLI tool (pipeforge.exe)
└── PipeForge.Tests/         # xUnit tests
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

- .NET 8.0 SDK
- Windows (primary target) / Linux / macOS

## License

MIT
