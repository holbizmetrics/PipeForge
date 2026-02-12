# Campaign State — PipeForge

*Last updated: 2026-02-11 (Session 3 — T-006 COMPLETE. Phase 3 in progress.)*

---

## Position

**Current phase:** Phase 3 — T-006 DONE. T-007 next.
**Next action:** T-007 (Import/Export layer) — or frontier items
**Blocking:** Nothing. Build clean, 62/62 tests pass. GUI builds and launches.

## Active Route

**Phase 1 (Critical 5):** ~~T-001~~ → ~~T-002~~ → ~~T-003~~ → ~~T-004~~ → ~~T-005~~ ALL DONE
**Phase 2 (CLI Polish):** ~~T-008~~ → ~~T-009~~ → ~~T-010~~ → ~~T-011~~ → ~~T-012~~ ALL DONE
**Phase 3:** ~~T-006~~ (Avalonia GUI) DONE | T-007 (Import/Export) — next

## Key Context for Cold Resume

- **Target framework:** .NET 10
- **Solution format:** .slnx
- **Architecture:** `PipeForge.Core` (engine) + `PipeForge.Runner` (CLI) + `PipeForge.GUI` (Avalonia) + `PipeForge.Tests` (xUnit)
- **Core innovation:** Pipeline steps are debuggable C# code. Breakpoint on `PipelineEngine.ExecuteStepAsync`, F5, F10, hover over `run`.
- **Single-file publish:** `dotnet publish PipeForge.Runner -c Release -r win-x64 --self-contained` → 36.8 MB pipeforge.exe
- **GUI stack:** Avalonia 11.3.11 + CommunityToolkit.Mvvm 8.4.0 + AvaloniaEdit 11.4.1

## Session 3 Summary

### T-006: Avalonia GUI scaffold
- **PipeForge.GUI project** — 16 new files, Avalonia 11.3.11 desktop app
- **3 views:** Pipeline Editor (AvaloniaEdit + live validation), Live Run (real-time output + breakpoints), Template Browser (list + preview)
- **CommunityToolkit.Mvvm** — [ObservableProperty]/[RelayCommand] source generators
- **Breakpoint interaction:** TaskCompletionSource pattern — blocks engine thread, UI remains responsive
- **Real-time output:** Dispatcher.UIThread.Post() marshaling, 10K line cap
- **Catppuccin Mocha** dark theme throughout (Fluent base)
- **Zero changes to PipeForge.Core** — GUI consumes identical API as CLI
- **Build:** 0 errors, 0 warnings. 62/62 existing tests pass.

### Totals: 62/62 tests, 0 warnings, 0 errors. GUI builds clean.

## What Remains

- **T-007:** Import/Export layer — GitLab/GitHub/Azure YAML mappers
- Frontier: template registry, plugin system, Avalonia live run view enhancements
