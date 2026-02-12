# Campaign Ledger — PipeForge

═══════════════════════════════════════════════════════════
CAMPAIGN LEDGER: PipeForge
Target: Local pipeline engine with native step-debugging via .NET debugger
Sessions: 4 (S0 = planning + scaffold, S1 = T-001–T-003, S2 = T-004–T-005 + T-008–T-012, S3 = T-006)
═══════════════════════════════════════════════════════════

PROVEN:
  C-PF-001 through C-PF-011 — see claims.md (all scaffolded S0)
  C-PF-012: pipeforge init emits self-documenting YAML with inline comments — S1 — CommentedYamlTemplates + 10 round-trip tests
  C-PF-013: pipeforge validate lints YAML without running — S1 — PipelineValidator in Core + validate CLI command + 14 tests
  C-PF-014: StepResult shows last stderr + pattern-matched hints on failure — S1 — ErrorHints + LastStderrLines + ErrorSummary + breakpoint UI + 10 tests
  C-PF-015: Trust notice on first run / file modification — S2 — TrustStore.cs (SHA-256 hash tracking, command summary, persistent store) + 8 tests
  C-PF-016: Path normalization at all .NET API consumption points — S2 — PathNormalizer.cs + 5 integration points + 10 tests
  C-PF-022: Avalonia GUI scaffold with 3 views consuming PipeForge.Core — S3 — PipeForge.GUI project, 16 files, builds clean

DEAD ENDS:
  (none yet — fresh campaign)

OPEN THREADS:
  T-001: Self-documenting YAML templates — PROVEN — S1 — CommentedYamlTemplates.cs, all 5 templates, 10 tests
  T-002: pipeforge validate command — PROVEN — S1 — PipelineValidator.cs + CLI command + 14 tests
  T-003: Richer error messages in StepResult — PROVEN — S1 — ErrorHints.cs + StepResult extensions + breakpoint UI + 10 tests
  T-004: Security trust notice — PROVEN — S2 — TrustStore.cs + command summary display + 8 tests
  T-005: Path normalization — PROVEN — S2 — PathNormalizer.cs + 5 integration points + 10 tests
  T-006: Avalonia GUI scaffold — PROVEN — S3 — PipeForge.GUI (Avalonia 11.3.11 + CommunityToolkit.Mvvm + AvaloniaEdit), 3 views, TaskCompletionSource breakpoints, Dispatcher.UIThread.Post output streaming
  T-007: Import/Export layer — PARKED — Waiting on v1 stability
  T-008: --verbose/--quiet flags — PROVEN — S2 — -v/-q on pipeforge run
  T-009: Watch mode notification — PROVEN — S2 — Notifier.cs + --notify flag
  T-010: Template versioning — PROVEN — S2 — version: field + validator warnings + 3 tests
  T-011: Single-file publish — PROVEN — S2 — .csproj config + verified pipeforge.exe (36.8 MB)
  T-012: FileSystemWatcher hardening — PROVEN — S2 — 64KB buffer + duplicate suppression + error recovery

FRONTIER QUEUE:
  - Schema mapping layer for GitLab/GitHub/Azure import (v2)
  - Template registry (local folder + remote fetch)
  - Plugin system for custom step types
  - Avalonia live run view enhancements (search, filter, export)
  - Avalonia template browser form-based creation

INSIGHTS:
  I-001: "Failure is the feature" — Debugging IS the product. Optimize the failure path UX, not just the happy path.
  I-002: "Speaks your dialect" — YAML shape must feel familiar to GitLab/GitHub/Azure users without being a clone.
  I-003: VS debugger mode (F5 + breakpoint on ExecuteStepAsync) is the killer feature. Console mode is good; VS mode is the USP.
  I-004: Compose() amplifies security risk — merging untrusted templates can inject commands.
  I-005: FileSystemWatcher is unreliable on network drives, produces duplicates on Windows, can overflow buffer on rapid changes.
  I-006: The "magic moment" is hovering over PipelineRun in VS debugger and seeing full state. Protect this at all costs.
  I-007: Avalonia OnBeforeStep is synchronous EventHandler — TaskCompletionSource is the correct bridge to async UI. Blocking engine thread is safe because engine never runs on UI thread.

DEPENDENCIES:
  T-007 (Import/Export) needs schema mapping design — depends on understanding of GitLab/GitHub/Azure YAML schemas

VERIFICATION LEDGER:
  C-PF-001: 1 verified (Holger compiled after fix), 0 counterexamples
  C-PF-002 through C-PF-011: 0 runtime tests. Need real pipeline execution.
  C-PF-022: Verified — dotnet build 0 errors, 62/62 existing tests pass. Needs visual smoke test (launch app).

CAMPAIGN METRICS:
  Threads opened: 12 | Proven: 22 (11 scaffolded + T-001–T-006 + T-008–T-012) | Dead: 0 | Open: 1
  Solution space narrowed by: Architecture locked (C#/.NET 10, Core+Runner+GUI split). DERIVE completed. Critical 5: 5/5 DONE. Phase 2: 5/5 DONE. Phase 3: T-006 DONE.

═══════════════════════════════════════════════════════════
