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
        var verboseOption = new Option<bool>("--verbose", () => false, "Show debug-level output from the engine");
        verboseOption.AddAlias("-v");
        var quietOption = new Option<bool>("--quiet", () => false, "Only show errors and final summary");
        quietOption.AddAlias("-q");
        var notifyOption = new Option<bool>("--notify", () => false, "Send OS notification on watch-mode completion");

        runCommand.AddArgument(fileArg);
        runCommand.AddOption(interactiveOption);
        runCommand.AddOption(watchOption);
        runCommand.AddOption(verboseOption);
        runCommand.AddOption(quietOption);
        runCommand.AddOption(notifyOption);

        runCommand.SetHandler(RunPipeline, fileArg, interactiveOption, watchOption, verboseOption, quietOption, notifyOption);

        // ── pipeforge init <template> [--output] ──
        var initCommand = new Command("init", "Initialize a pipeline from a template");
        var templateArg = new Argument<string>("template", "Template name: innosetup, dotnet, security, twincat, custom");
        var outputOption = new Option<FileInfo>("--output", () => new FileInfo("pipeforge.yml"), "Output file path");
        outputOption.AddAlias("-o");

        initCommand.AddArgument(templateArg);
        initCommand.AddOption(outputOption);
        initCommand.SetHandler(InitTemplate, templateArg, outputOption);

        // ── pipeforge validate <file> ──
        var validateCommand = new Command("validate", "Validate a pipeline YAML file without running it");
        var validateFileArg = new Argument<FileInfo>("file", "Path to pipeline YAML file to validate");
        validateCommand.AddArgument(validateFileArg);
        validateCommand.SetHandler(ValidatePipeline, validateFileArg);

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
        rootCommand.AddCommand(validateCommand);
        rootCommand.AddCommand(listCommand);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task RunPipeline(FileInfo file, bool interactive, bool watch, bool verbose, bool quiet, bool notify)
    {
        // Verbose wins if both are set
        if (verbose && quiet)
        {
            AnsiConsole.MarkupLine("[yellow]Both --verbose and --quiet specified; using --verbose.[/]");
            quiet = false;
        }

        var logLevel = verbose ? LogLevel.Debug
                     : quiet ? LogLevel.Warning
                     : LogLevel.Information;

        using var loggerFactory = LoggerFactory.Create(b => b
            .AddConsole()
            .SetMinimumLevel(interactive ? LogLevel.Debug : logLevel));

        var engineLogger = loggerFactory.CreateLogger<PipelineEngine>();
        var watcherLogger = loggerFactory.CreateLogger<PipelineWatcher>();

        var engine = new PipelineEngine(engineLogger);

        // Wire up real-time output display (suppressed in quiet mode)
        if (!quiet)
        {
            engine.OnOutput += (_, line) =>
            {
                var color = line.Source == OutputSource.StdErr ? "red" : "grey";
                AnsiConsole.MarkupLine($"    [{color}]{Markup.Escape(line.Text)}[/]");
            };
        }

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

                    // Show stderr + hints when last step failed
                    if (last.Status == StepStatus.Failed)
                    {
                        var stderr = last.LastStderrLines();
                        if (stderr.Count > 0)
                        {
                            AnsiConsole.Write(new Panel(
                                string.Join("\n", stderr.Select(l => Markup.Escape(l))))
                                .Header("[red]stderr (last 10 lines)[/]")
                                .Border(BoxBorder.Rounded)
                                .BorderStyle(Style.Parse("red")));
                        }

                        if (last.Hints.Count > 0)
                        {
                            foreach (var hint in last.Hints)
                                AnsiConsole.MarkupLine($"  [yellow]→[/] {Markup.Escape(hint)}");
                        }
                    }
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

        // Trust check — show notice on first run or file modification
        try
        {
            var trustStore = new TrustStore();
            var trustCheck = trustStore.Check(file.FullName);
            PrintTrustNotice(pipeline, trustCheck, file);
            if (trustCheck.Status != TrustStatus.Trusted)
                trustStore.Trust(file.FullName, trustCheck.CurrentHash);
        }
        catch
        {
            // Trust check is advisory — don't block execution if it fails
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
                PrintRunSummary(run, quiet);
                NotifyCompletion(run, notify);
            };

            // Initial run
            var initialRun = await engine.RunAsync(pipeline, interactive, cts.Token);
            PrintRunSummary(initialRun, quiet);
            NotifyCompletion(initialRun, notify);

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
            PrintRunSummary(run, quiet);

            Environment.ExitCode = run.Status == PipelineRunStatus.Success ? 0 : 1;
        }
    }

    static void PrintRunSummary(PipelineRun run, bool showFailureDetails = false)
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

        // In quiet mode, stderr wasn't streamed — show failure details now
        if (showFailureDetails)
        {
            foreach (var result in run.StepResults.Where(r => r.Status == StepStatus.Failed))
            {
                var stderr = result.LastStderrLines();
                if (stderr.Count > 0)
                {
                    AnsiConsole.Write(new Panel(
                        string.Join("\n", stderr.Select(l => Markup.Escape(l))))
                        .Header($"[red]stderr: {Markup.Escape(result.StageName)}/{Markup.Escape(result.StepName)}[/]")
                        .Border(BoxBorder.Rounded)
                        .BorderStyle(Style.Parse("red")));
                }

                foreach (var hint in result.Hints)
                    AnsiConsole.MarkupLine($"  [yellow]→[/] {Markup.Escape(hint)}");
            }
        }

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

    static void ValidatePipeline(FileInfo file)
    {
        var result = PipelineValidator.ValidateFile(file.FullName);

        if (result.Messages.Count == 0)
        {
            AnsiConsole.MarkupLine($"[green]✓[/] [bold]{file.Name}[/] is valid. No issues found.");
            return;
        }

        foreach (var msg in result.Errors)
            AnsiConsole.MarkupLine($"  [red]ERROR[/]   [[{Markup.Escape(msg.Location)}]] {Markup.Escape(msg.Message)}");

        foreach (var msg in result.Warnings)
            AnsiConsole.MarkupLine($"  [yellow]WARN[/]    [[{Markup.Escape(msg.Location)}]] {Markup.Escape(msg.Message)}");

        AnsiConsole.WriteLine();
        if (result.HasErrors)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] [bold]{file.Name}[/]: {result.Errors.Count()} error(s), {result.Warnings.Count()} warning(s).");
            Environment.ExitCode = 1;
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✓[/] [bold]{file.Name}[/]: valid with {result.Warnings.Count()} warning(s).");
        }
    }

    static void NotifyCompletion(PipelineRun run, bool osNotify)
    {
        // Terminal bell — always in watch mode
        Notifier.Bell();

        // OS toast — only when --notify is set
        if (osNotify)
        {
            var status = run.Status == PipelineRunStatus.Success ? "succeeded" : "failed";
            Notifier.OsNotify("PipeForge", $"{run.PipelineName} {status} ({run.Elapsed.TotalSeconds:F1}s)");
        }
    }

    static void PrintTrustNotice(PipelineDefinition pipeline, TrustCheckResult check, FileInfo file)
    {
        if (check.Status == TrustStatus.Trusted)
            return;

        var header = check.Status == TrustStatus.New
            ? "[yellow]FIRST RUN[/]"
            : "[yellow]FILE MODIFIED[/]";

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(header).RuleStyle("yellow"));

        AnsiConsole.MarkupLine($"  File: [bold]{Markup.Escape(file.FullName)}[/]");
        AnsiConsole.MarkupLine($"  SHA-256: [dim]{check.CurrentHash[..16]}...[/]");

        if (check.Status == TrustStatus.Modified)
            AnsiConsole.MarkupLine($"  Previous: [dim]{check.PreviousHash![..16]}...[/]");

        // Command summary — this is what makes it NOT security theater.
        // The user sees exactly what shell commands will execute.
        AnsiConsole.MarkupLine("\n  [bold]Commands this pipeline will execute:[/]");
        foreach (var stage in pipeline.Stages)
        {
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(stage.Name)}:[/]");
            foreach (var step in stage.Steps)
            {
                var cmd = $"{step.Command} {step.Arguments ?? ""}".Trim();
                AnsiConsole.MarkupLine($"    [dim]•[/] {Markup.Escape(cmd)}");
            }
        }

        AnsiConsole.MarkupLine($"\n  [dim]Review the commands above. PipeForge will execute them as shell processes.[/]");
        AnsiConsole.Write(new Rule().RuleStyle("yellow"));
        AnsiConsole.WriteLine();
    }

    static void InitTemplate(string template, FileInfo output)
    {
        string yaml;
        try
        {
            yaml = CommentedYamlTemplates.GetTemplate(template);
        }
        catch (ArgumentException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Unknown template: [bold]{template}[/]");
            AnsiConsole.MarkupLine("  Available: innosetup, dotnet, security, twincat, custom");
            return;
        }

        var directory = Path.GetDirectoryName(output.FullName);
        if (directory != null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(output.FullName, yaml);

        AnsiConsole.MarkupLine($"[green]✓[/] Created [bold]{output.FullName}[/] from template [bold]{template}[/]");
        AnsiConsole.MarkupLine($"  Edit the file, then run: [bold]pipeforge run {output.Name} --interactive[/]");
    }
}
