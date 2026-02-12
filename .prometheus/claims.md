# Claims — PipeForge

## Proven (Completed & Verified)

C-PF-001: Solution scaffold compiles — S0 — Manual build by Holger after C#10/Environment fix
C-PF-002: PipelineEngine executes steps sequentially with event hooks — S0 — Scaffolded with OnBeforeStep/OnAfterStep
C-PF-003: PipelineRun captures full execution state (vars, results, artifacts, timing) — S0 — Scaffolded
C-PF-004: StepResult captures stdout/stderr/exit code/duration per step — S0 — Scaffolded
C-PF-005: PipelineLoader parses YAML into PipelineDefinition — S0 — Scaffolded (YamlDotNet)
C-PF-006: PipelineWatcher wraps FileSystemWatcher with debounce — S0 — Scaffolded (500ms default)
C-PF-007: PipelineTemplates provide InnoSetup/DotNet/Security/TwinCAT building blocks — S0 — Scaffolded
C-PF-008: CLI supports run/init/templates commands — S0 — Scaffolded (System.CommandLine)
C-PF-009: Interactive mode pauses at each step with Continue/Skip/Retry/Abort — S0 — Scaffolded
C-PF-010: Variable resolution in step commands (${VAR_NAME} syntax) — S0 — Scaffolded in PipelineEngine
C-PF-011: Templates composable via PipelineTemplates.Compose() — S0 — Scaffolded

C-PF-012: pipeforge init emits self-documenting YAML with inline comments for all 5 templates — S1 — CommentedYamlTemplates.cs + 10 round-trip tests
C-PF-013: pipeforge validate lints YAML without running (parse errors, required fields, undefined ${VAR} refs, duplicate names, condition refs, timeouts) — S1 — PipelineValidator.cs + CLI command + 14 tests
C-PF-014: StepResult shows last stderr lines + pattern-matched error hints on failure; breakpoint prompt surfaces both — S1 — ErrorHints.cs + StepResult extensions + 10 tests
C-PF-015: Trust notice on first run / file modification — shows SHA-256 hash + command summary; stores seen hashes in ~/.pipeforge/trusted-hashes.json — S2 — TrustStore.cs + 8 tests
C-PF-016: Path normalization at all .NET API consumption points — ~ expansion, mixed separator handling, relative resolution — S2 — PathNormalizer.cs + 5 integration points (workDir, stepWorkDir, artifacts, conditions, watcher) + 10 tests
C-PF-017: --verbose/-v and --quiet/-q flags on pipeforge run — verbose = Debug logging, quiet = suppress streaming + show failure details in summary — S2 — CLI wiring in Program.cs
C-PF-018: Watch mode notification — terminal bell (\a) always + OS toast with --notify (Windows/Linux/macOS) — S2 — Notifier.cs + CLI wiring
C-PF-019: Template versioning — version: field in YAML, validator warns on missing/newer/older, all 5 templates emit version: 1 — S2 — PipelineDefinition.CurrentSchemaVersion + validator check + 3 tests
C-PF-020: Single-file publish — PublishSingleFile + EnableCompressionInSingleFile in .csproj; dotnet publish -r win-x64 --self-contained produces pipeforge.exe (36.8 MB) — S2 — Verified working
C-PF-021: FileSystemWatcher hardened — 64KB buffer (8x default), duplicate suppression via MinTriggerInterval, Error event handler with auto-recovery — S2 — PipelineWatcher.cs rewritten
C-PF-022: Avalonia GUI scaffold — PipeForge.GUI project with 3 views (Pipeline Editor, Live Run, Template Browser) consuming PipeForge.Core via identical API as CLI — S3 — Avalonia 11.3.11 + CommunityToolkit.Mvvm 8.4.0 + AvaloniaEdit 11.4.1. TaskCompletionSource breakpoint pattern, Dispatcher.UIThread.Post output streaming, Catppuccin Mocha dark theme. 16 new files, 0 Core changes.

## Verification Notes

All S0 claims are "scaffolded" — code structure exists and compiles, but has not been runtime-tested against real pipelines. First real verification = running `pipeforge run` against an actual InnoSetup project.

C-PF-012 is runtime-verified: `pipeforge init innosetup` produces commented YAML that round-trips through PipelineLoader.LoadFromYaml with correct structure (stage count, breakpoint modes, conditions).
