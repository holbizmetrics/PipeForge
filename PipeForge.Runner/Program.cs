using System.CommandLine;
using Microsoft.Extensions.Logging;
using PipeForge.Core.Engine;
using PipeForge.Core.Models;
using PipeForge.Core.Watcher;
using PipeForge.Core.Templates;
using Spectre.Console;

namespace PipeForge.Runner;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("PipeForge — Local pipeline engine with step-debugging");

        // ── pipeforge run <file> [--interactive] [--watch] ──
        var runCommand = new Command("run", "Run a pipeline from a YAML file");
        var fileArg = new Argument<FileInfo>("file", "Path to pipeline YAML file");
        var interactiveOption = new Option<bool>("--interactive", () => false, "Enable step-by-step debugging");
        interactiveOption.AddAlias("-i");
        var watchOption = new Option<bool>("--watch", () => false, "Keep running and watch for file changes");
        watchOption.AddAlias("-w");

        runCommand.AddArgument(fileArg);
        runCommand.AddOption(interactiveOption);
        runCommand.AddOption(watchOption);

        runCommand.SetHandler(RunPipeline, fileArg, interactiveOption, watchOption);

        // ── pipeforge init <template> [--output] ──
        var initCommand = new Command("init", "Initialize a pipeline from a template");
        var templateArg = new Argument<string>("template", "Template name: innosetup, dotnet, security, twincat, custom");
        var outputOption = new Option<FileInfo>("--output", () => new FileInfo("pipeforge.yml"), "Output file path");
        outputOption.AddAlias("-o");

        initCommand.AddArgument(templateArg);
        initCommand.AddOption(outputOption);
        initCommand.SetHandler(InitTemplate, templateArg, outputOption);

        // ── pipeforge templates ──
        var listCommand = new Command("templates", "List available pipeline templates");
        listCommand.SetHandler(() =>
        {
            var table = new Table();
            table.AddColumn("Template");
            table.AddColumn("Description");
            table.AddRow("innosetup", "Inno Setup installer compilation + signing");
            table.AddRow("dotnet", ".NET build, test, publish pipeline");
            table.AddRow("security", "SBOM + vulnerability scanning (Syft/Grype)");
            table.AddRow("twincat", "TwinCAT/PLC build pipeline");
            table.AddRow("custom", "Empty pipeline scaffold to fill in yourself");
            AnsiConsole.Write(table);
        });

        rootCommand.AddCommand(runCommand);
        rootCommand.AddCommand(initCommand);
        rootCommand.AddCommand(listCommand);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task RunPipeline(FileInfo file, bool interactive, bool watch)
    {
        using var loggerFactory = LoggerFactory.Create(b => b
            .AddConsole()
            .SetMinimumLevel(interactive ? LogLevel.Debug : LogLevel.Information));

        var engineLogger = loggerFactory.CreateLogger<PipelineEngine>();
        var watcherLogger = loggerFactory.CreateLogger<PipelineWatcher>();

        var engine = new PipelineEngine(engineLogger);

        // Wire up real-time output display
        engine.OnOutput += (_, line) =>
        {
            var color = line.Source == OutputSource.StdErr ? "red" : "grey";
            AnsiConsole.MarkupLine($"    [{color}]{Markup.Escape(line.Text)}[/]");
        };

        // Wire up the interactive step debugger
        if (interactive)
        {
            engine.OnBeforeStep += (_, e) =>
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[yellow]⏸ BREAKPOINT[/] Step {e.StepIndex}/{e.TotalSteps}: {e.StageName}/{e.Step.Name}").RuleStyle("yellow"));
                
                // Show current pipeline state
                var stateTable = new Table().Border(TableBorder.Rounded);
                stateTable.AddColumn("Property");
                stateTable.AddColumn("Value");
                stateTable.AddRow("Pipeline", e.Run.PipelineName);
                stateTable.AddRow("Status", e.Run.Status.ToString());
                stateTable.AddRow("Steps completed", $"{e.Run.StepResults.Count(r => r.Status != StepStatus.Pending)}");
                stateTable.AddRow("Failures so far", e.Run.FailedCount.ToString());
                stateTable.AddRow("Elapsed", $"{e.Run.Elapsed.TotalSeconds:F1}s");
                stateTable.AddRow("Command", $"{e.Step.Command} {e.Step.Arguments ?? ""}");

                AnsiConsole.Write(stateTable);

                // Show last step result if available
                if (e.Run.LastCompleted != null)
                {
                    var last = e.Run.LastCompleted;
                    AnsiConsole.MarkupLine($"  Last step: [{(last.Status == StepStatus.Success ? "green" : "red")}]{last}[/]");
                }

                // Show variables
                if (e.Run.Variables.Count > 0)
                {
                    var varsExpander = new Panel(
                        string.Join("\n", e.Run.Variables.Select(v => $"  {v.Key} = {v.Value}")))
                        .Header("[dim]Variables[/]")
                        .Border(BoxBorder.Rounded)
                        .BorderStyle(Style.Parse("dim"));
                    AnsiConsole.Write(varsExpander);
                }

                // Prompt for action
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("  [yellow]Action?[/]")
                        .AddChoices("▶ Continue", "⏭ Skip this step", "🔄 Retry last", "⛔ Abort pipeline"));

                e.Action = choice switch
                {
                    "⏭ Skip this step" => DebugAction.Skip,
                    "🔄 Retry last" => DebugAction.Retry,
                    "⛔ Abort pipeline" => DebugAction.Abort,
                    _ => DebugAction.Continue
                };
            };
        }

        // Load pipeline
        PipelineDefinition pipeline;
        try
        {
            pipeline = PipelineLoader.LoadFromFile(file.FullName);
            AnsiConsole.MarkupLine($"[green]✓[/] Loaded pipeline: [bold]{pipeline.Name}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Failed to load pipeline: {ex.Message}");
            return;
        }

        if (watch && pipeline.Watch.Count > 0)
        {
            // Watch mode: run once, then watch for changes
            AnsiConsole.MarkupLine("[blue]👁 Watch mode enabled[/] — press Ctrl+C to stop");

            using var watcher = new PipelineWatcher(watcherLogger);
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                watcher.Stop();
            };

            watcher.OnTriggered += async (filePath, trigger) =>
            {
                AnsiConsole.MarkupLine($"\n[blue]📂 Change detected:[/] {filePath}");
                var run = await engine.RunAsync(pipeline, interactive, cts.Token);
                PrintRunSummary(run);
            };

            // Initial run
            var initialRun = await engine.RunAsync(pipeline, interactive, cts.Token);
            PrintRunSummary(initialRun);

            // Start watching
            watcher.Start(pipeline.Watch);

            // Block until Ctrl+C
            try { await Task.Delay(Timeout.Infinite, cts.Token); }
            catch (OperationCanceledException) { }
        }
        else
        {
            // Single run
            var run = await engine.RunAsync(pipeline, interactive);
            PrintRunSummary(run);

            Environment.ExitCode = run.Status == PipelineRunStatus.Success ? 0 : 1;
        }
    }

    static void PrintRunSummary(PipelineRun run)
    {
        AnsiConsole.WriteLine();
        var color = run.Status == PipelineRunStatus.Success ? "green" : "red";
        AnsiConsole.Write(new Rule($"[{color}]{run.Status}[/]").RuleStyle(color));

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Stage / Step");
        table.AddColumn("Status");
        table.AddColumn("Duration");
        table.AddColumn("Exit Code");

        foreach (var result in run.StepResults)
        {
            var stepColor = result.Status switch
            {
                StepStatus.Success => "green",
                StepStatus.Failed => "red",
                StepStatus.Skipped => "dim",
                _ => "yellow"
            };
            
            table.AddRow(
                $"{result.StageName} / {result.StepName}",
                $"[{stepColor}]{result.Status}[/]",
                $"{result.Elapsed.TotalSeconds:F1}s",
                result.ExitCode.ToString());
        }

        AnsiConsole.Write(table);

        if (run.Artifacts.Count > 0)
        {
            AnsiConsole.MarkupLine($"\n[green]📦 Artifacts:[/]");
            foreach (var artifact in run.Artifacts)
            {
                AnsiConsole.MarkupLine($"  {artifact.Path} ({artifact.SizeBytes / 1024.0:F1} KB)");
            }
        }

        AnsiConsole.MarkupLine($"\nTotal time: [bold]{run.Elapsed.TotalSeconds:F1}s[/]");
    }

    static void InitTemplate(string template, FileInfo output)
    {
        PipelineDefinition pipeline = template.ToLower() switch
        {
            "innosetup" => PipelineTemplates.InnoSetupInstaller(
                "MyApp", @".\installer\myapp.iss"),
            "dotnet" => PipelineTemplates.DotNetBuild(
                "MyProject", @".\MyProject.sln"),
            "security" => PipelineTemplates.SecurityScan(
                "MyProject", "."),
            "twincat" => PipelineTemplates.TwinCatBuild(
                "MyPLC", @".\MyPLC.sln"),
            "custom" => new PipelineDefinition
            {
                Name = "My Pipeline",
                Description = "Describe your pipeline here",
                Variables = new Dictionary<string, string>
                {
                    ["MY_VAR"] = "my_value"
                },
                Watch = new List<WatchTrigger>
                {
                    new() { Path = ".", Filter = "*.*", DebounceMs = 500 }
                },
                Stages = new List<PipelineStage>
                {
                    new()
                    {
                        Name = "build",
                        Steps = new List<PipelineStep>
                        {
                            new()
                            {
                                Name = "My Step",
                                Command = "echo",
                                Arguments = "Hello from PipeForge!",
                                Breakpoint = BreakpointMode.Always
                            }
                        }
                    }
                }
            },
            _ => throw new ArgumentException($"Unknown template: {template}")
        };

        PipelineLoader.SaveToFile(pipeline, output.FullName);
        AnsiConsole.MarkupLine($"[green]✓[/] Created [bold]{output.FullName}[/] from template [bold]{template}[/]");
        AnsiConsole.MarkupLine($"  Edit the file, then run: [bold]pipeforge run {output.Name} --interactive[/]");
    }
}
