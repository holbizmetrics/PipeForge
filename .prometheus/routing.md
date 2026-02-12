# Routing — PipeForge

## Active Routes

| Route | Description | Tier | Next Step | Graveyard Check |
|---|---|---|---|---|
| T-001 | Self-documenting YAML templates | PROVEN | CommentedYamlTemplates.cs + Program.cs updated. 5 templates, 10 tests. S1. | Clear |
| T-002 | pipeforge validate command | PROVEN | PipelineValidator.cs in Core + validate CLI command. 14 tests. S1. | Clear |
| T-003 | Richer error messages | PROVEN | ErrorHints.cs + StepResult.LastStderrLines/ErrorSummary/Hints + breakpoint UI. 10 tests. S1. | Clear |
| T-004 | Security trust notice | PROVEN | TrustStore.cs in Core — SHA-256 hash tracking + command summary on first run / modification. 8 tests. S2. | Clear — dodges SECURITY-THEATER by showing actual commands |
| T-005 | Path normalization | PROVEN | PathNormalizer.cs in Core — ~ expansion, separator normalization, relative resolution. Applied at 5 .NET API consumption points. 10 tests. S2. | Clear — uses Path.DirectorySeparatorChar, platform-correct by design |
| T-006 | Avalonia GUI scaffold | PROVEN | PipeForge.GUI — Avalonia 11.3.11 + CommunityToolkit.Mvvm + AvaloniaEdit. 3 views (Editor, LiveRun, Templates). TaskCompletionSource breakpoints. 16 files, 0 Core changes. S3. | Clear — zero Core modifications, GUI is additive |
| T-007 | Import/Export layer | PARKED | Design abstract PipelineSchema; implement GitLabMapper, GitHubMapper, AzureMapper | Waiting on v1 stability |
| T-008 | --verbose/--quiet flags | PROVEN | -v = Debug logging, -q = suppress streaming + failure details in summary. CLI wiring. S2. | Clear |
| T-009 | Watch mode notification | PROVEN | Terminal bell + OS toast (--notify). Notifier.cs with Windows/Linux/macOS support. S2. | Clear |
| T-010 | Template versioning | PROVEN | version: field + CurrentSchemaVersion constant + validator warnings. All templates emit version: 1. 3 tests. S2. | Clear |
| T-011 | Single-file publish | PROVEN | PublishSingleFile + compression in .csproj. pipeforge.exe = 36.8 MB self-contained. Verified. S2. | Clear |
| T-012 | FileSystemWatcher hardening | PROVEN | 64KB buffer, duplicate suppression, Error event with auto-recovery. S2. | Clear — uses platform-agnostic FSW API |

## Execution Order

### Phase 1: Critical 5 (v1 release gate)
```
T-001 (YAML comments) → T-002 (validate) → T-003 (error messages) → T-004 (security) → T-005 (paths)
```
Each is independent. Can be done in any order. Listed by user impact.

### Phase 2: CLI Polish (v1.1)
```
T-008 (verbose/quiet) → T-009 (notifications) → T-010 (versioning) → T-011 (single-file) → T-012 (watcher hardening)
```

### Phase 3: Decision Point
```
T-006 (Avalonia GUI) OR T-007 (Import/Export)
```
User decides at Phase 2 completion. Both are independent of each other.

## Routing Rationale

T-001 is NEXT because:
- **Proximity:** Directly extends existing PipelineTemplates (C-PF-007). Minimal new code.
- **Concreteness:** "Add comments to YAML strings" — completely clear next step.
- **Evidence:** DERIVE Pass 1 + INHABIT both identified bare YAML as the #1 friction point.
- **Graveyard:** Clear. No failure signature match.

T-002 is NEXT because:
- **Proximity:** Extends PipelineLoader (C-PF-005). Validation = parse + check without execute.
- **Concreteness:** "Add ValidateCommand, reuse PipelineLoader, report errors."
- **Evidence:** Standard DevOps convention. Every CI tool has `--check` or `--lint`.

T-003 is NEXT because:
- **Proximity:** Extends StepResult (C-PF-004). Add fields + surface in interactive prompt.
- **Concreteness:** "Add LastStderrLines property, populate during process execution."
- **Evidence:** INHABIT finding #2 — opaque errors at breakpoint prompt.
