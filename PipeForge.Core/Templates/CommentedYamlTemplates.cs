namespace PipeForge.Core.Templates;

/// <summary>
/// Hand-crafted YAML templates with inline comments for pipeforge init output.
/// These produce the same pipeline structure as PipelineTemplates but with
/// documentation that helps users understand and customize each field.
/// </summary>
public static class CommentedYamlTemplates
{
    /// <summary>
    /// Returns a commented YAML template string for the given template name.
    /// These are meant for pipeforge init output — user-facing, self-documenting.
    /// </summary>
    public static string GetTemplate(string templateName) => templateName.ToLower() switch
    {
        "innosetup" => InnoSetupYaml(),
        "dotnet" => DotNetYaml(),
        "security" => SecurityScanYaml(),
        "twincat" => TwinCatYaml(),
        "custom" => CustomYaml(),
        _ => throw new ArgumentException($"Unknown template: {templateName}")
    };

    public static string InnoSetupYaml() => """
        # ═══════════════════════════════════════════════════════════════════════
        # PipeForge Pipeline — Inno Setup Installer
        # ═══════════════════════════════════════════════════════════════════════
        #
        # Compiles an Inno Setup installer, optionally signs it, verifies output.
        #
        # Quick start:
        #   1. Edit the variables below to match your project
        #   2. Run:   pipeforge run pipeforge.yml
        #   3. Debug: pipeforge run pipeforge.yml --interactive
        #      Or set a breakpoint on PipelineEngine.ExecuteStepAsync in Visual
        #      Studio, hit F5, and step through your build with F10.
        #      Hover over 'run' to inspect the full pipeline state.
        #
        # ═══════════════════════════════════════════════════════════════════════

        # Schema version — PipeForge uses this to warn about compatibility.
        version: 1

        name: MyApp - Inno Setup Build
        description: Compile and package MyApp installer

        # All paths in steps are relative to this directory.
        working_directory: .\installer

        # ── Variables ─────────────────────────────────────────────────────────
        # Referenced in steps as ${VARIABLE_NAME}.
        # Change these to match your project.
        variables:
          PROJECT_NAME: MyApp
          ISS_FILE: .\installer\myapp.iss                          # Your .iss script
          OUTPUT_DIR: .\installer\Output                           # Where the compiled installer lands
          ISCC_PATH: C:\Program Files (x86)\Inno Setup 6\ISCC.exe  # Inno Setup compiler path
          # SIGNTOOL_PATH: C:\path\to\signtool.exe                 # Uncomment to enable code signing

        # ── Watch ─────────────────────────────────────────────────────────────
        # Auto-trigger the pipeline when watched files change.
        # Remove this section entirely for manual-only runs.
        watch:
        - path: .\installer
          filter: '*.iss'         # File pattern to watch
          debounce_ms: 1000       # Wait 1s after last change before triggering

        # ── Stages ────────────────────────────────────────────────────────────
        # Stages run top-to-bottom. Each stage contains one or more steps.
        stages:

          # Validate inputs before doing anything expensive
          - name: validate
            steps:
              - name: Check ISS file exists
                command: cmd.exe
                arguments: /c if not exist "${ISS_FILE}" exit 1
                description: Verify the .iss script file exists

          # Compile the installer
          - name: compile
            steps:
              - name: Compile Installer
                command: ${ISCC_PATH}
                arguments: /O"${OUTPUT_DIR}" "${ISS_FILE}"
                description: Run Inno Setup Compiler
                timeout_seconds: 120         # Max wait (default: 300s)
                artifacts:                   # Files to collect after this step
                  - ${OUTPUT_DIR}/*.exe
                # breakpoint — When the debugger pauses at this step:
                #   Never:     Never pause (default)
                #   Always:    Pause before every run
                #   OnFailure: Pause only on failure — inspect stderr and full state
                breakpoint: OnFailure

          # Code signing — only runs if SIGNTOOL_PATH is set
          - name: sign
            condition:
              only_if: SIGNTOOL_PATH       # Skipped when this variable is empty or missing
            steps:
              - name: Sign Installer
                command: ${SIGNTOOL_PATH}
                arguments: sign /tr http://timestamp.digicert.com /td sha256 /fd sha256 /a "${OUTPUT_DIR}/*.exe"
                description: Code sign the installer executable

          # Verify the installer was produced
          - name: verify
            steps:
              - name: Check Output
                command: cmd.exe
                arguments: /c dir "${OUTPUT_DIR}\*.exe"
                description: Verify installer was produced
        """;

    public static string DotNetYaml() => """
        # ═══════════════════════════════════════════════════════════════════════
        # PipeForge Pipeline — .NET Build
        # ═══════════════════════════════════════════════════════════════════════
        #
        # Restores, builds, tests, and publishes a .NET solution.
        #
        # Quick start:
        #   1. Set SOLUTION to your .sln file path
        #   2. Run:   pipeforge run pipeforge.yml
        #   3. Debug: pipeforge run pipeforge.yml --interactive
        #      Or set a breakpoint on PipelineEngine.ExecuteStepAsync in Visual
        #      Studio, hit F5, and step through your build with F10.
        #
        # ═══════════════════════════════════════════════════════════════════════

        version: 1

        name: MyProject - .NET Build
        description: Build, test, and package MyProject

        working_directory: .

        # ── Variables ─────────────────────────────────────────────────────────
        # Referenced in steps as ${VARIABLE_NAME}.
        variables:
          PROJECT_NAME: MyProject
          SOLUTION: .\MyProject.sln              # Path to your .sln file
          CONFIGURATION: Release                 # Build configuration (Debug / Release)

        # ── Watch ─────────────────────────────────────────────────────────────
        # Re-runs the pipeline when source files change.
        watch:
        - path: .
          filter: '*.cs'
          include_subdirectories: true          # Watch all subfolders
          debounce_ms: 2000                     # Wait 2s for rapid saves to settle

        # ── Stages ────────────────────────────────────────────────────────────
        stages:

          - name: restore
            steps:
              - name: NuGet Restore
                command: dotnet
                arguments: restore "${SOLUTION}"
                timeout_seconds: 120

          - name: build
            steps:
              - name: Build Solution
                command: dotnet
                arguments: build "${SOLUTION}" -c ${CONFIGURATION} --no-restore
                # Pauses on build failure so you can inspect compiler errors.
                breakpoint: OnFailure

          - name: test
            steps:
              - name: Run Tests
                command: dotnet
                arguments: test "${SOLUTION}" -c ${CONFIGURATION} --no-build --logger trx
                artifacts:
                  - '**/*.trx'                 # Collect test result files
                breakpoint: OnFailure

          - name: publish
            steps:
              - name: Publish
                command: dotnet
                arguments: publish "${SOLUTION}" -c ${CONFIGURATION} --no-build -o ./publish
                artifacts:
                  - ./publish/**               # Collect all published output
        """;

    public static string SecurityScanYaml() => """
        # ═══════════════════════════════════════════════════════════════════════
        # PipeForge Pipeline — Security Scan
        # ═══════════════════════════════════════════════════════════════════════
        #
        # Generates an SBOM with Syft, scans for vulnerabilities with Grype.
        # Requires: syft and grype installed and on PATH.
        #   Install: https://github.com/anchore/syft
        #            https://github.com/anchore/grype
        #
        # Quick start:
        #   1. Set TARGET to the directory or image to scan
        #   2. Run: pipeforge run pipeforge.yml
        #
        # ═══════════════════════════════════════════════════════════════════════

        version: 1

        name: MyProject - Security Scan
        description: SBOM generation and vulnerability scanning

        working_directory: .

        # ── Variables ─────────────────────────────────────────────────────────
        variables:
          PROJECT_NAME: MyProject
          TARGET: .                              # Directory or container image to scan
          REPORT_DIR: .\security-reports         # Where reports are written

        # ── Stages ────────────────────────────────────────────────────────────
        stages:

          # Generate a Software Bill of Materials
          - name: sbom
            steps:
              - name: Generate SBOM (Syft)
                command: syft
                arguments: dir:${TARGET} -o spdx-json=${REPORT_DIR}/sbom.json
                artifacts:
                  - ${REPORT_DIR}/sbom.json

          # Scan the SBOM for known vulnerabilities
          - name: scan
            steps:
              - name: Vulnerability Scan (Grype)
                command: grype
                arguments: sbom:${REPORT_DIR}/sbom.json -o json --file ${REPORT_DIR}/vulns.json
                # allow_failure: true means the pipeline continues even if grype
                # returns non-zero (which it does when vulnerabilities are found).
                allow_failure: true
                artifacts:
                  - ${REPORT_DIR}/vulns.json
                breakpoint: OnFailure

          # Print a human-readable summary
          - name: report
            steps:
              - name: Summary Report
                command: grype
                arguments: sbom:${REPORT_DIR}/sbom.json -o table
                allow_failure: true
        """;

    public static string TwinCatYaml() => """
        # ═══════════════════════════════════════════════════════════════════════
        # PipeForge Pipeline — TwinCAT Build
        # ═══════════════════════════════════════════════════════════════════════
        #
        # Builds a TwinCAT/PLC project using TcBuild.exe.
        # Requires: TwinCAT XAE (Engineering) installed.
        #
        # Quick start:
        #   1. Set SOLUTION to your TwinCAT .sln path
        #   2. Verify TC_BUILD points to your TcBuild.exe
        #   3. Run:   pipeforge run pipeforge.yml
        #   4. Debug: pipeforge run pipeforge.yml --interactive
        #
        # ═══════════════════════════════════════════════════════════════════════

        version: 1

        name: MyPLC - TwinCAT Build
        description: Build and validate TwinCAT PLC project

        working_directory: .

        # ── Variables ─────────────────────────────────────────────────────────
        variables:
          PROJECT_NAME: MyPLC
          SOLUTION: .\MyPLC.sln                                          # TwinCAT solution
          TC_BUILD: C:\TwinCAT\3.1\Components\Plc\Common\TcBuild.exe    # TwinCAT build tool

        # ── Watch ─────────────────────────────────────────────────────────────
        # Re-build when PLC source files change.
        watch:
        - path: .
          filter: '*.TcPOU'                    # TwinCAT POU source files
          include_subdirectories: true
          debounce_ms: 2000

        # ── Stages ────────────────────────────────────────────────────────────
        stages:

          - name: build
            steps:
              - name: Build TwinCAT Solution
                command: ${TC_BUILD}
                arguments: '"${SOLUTION}" /rebuild /configuration Release'
                timeout_seconds: 600           # PLC builds can be slow
                breakpoint: OnFailure
        """;

    public static string CustomYaml() => """
        # ═══════════════════════════════════════════════════════════════════════
        # PipeForge Pipeline — Custom
        # ═══════════════════════════════════════════════════════════════════════
        #
        # A blank scaffold. Fill in your own steps.
        #
        # Run:   pipeforge run pipeforge.yml
        # Debug: pipeforge run pipeforge.yml --interactive
        #        Or set a breakpoint on PipelineEngine.ExecuteStepAsync in Visual
        #        Studio, hit F5, step with F10, hover over 'run' for full state.
        #
        # Reference:
        #   ${VAR_NAME}  — Variable substitution in commands and arguments
        #   breakpoint:  — never (default), Always, OnFailure
        #   condition:   — only_if: VAR_NAME (run stage only if variable is set)
        #   allow_failure: true — continue the pipeline even if this step fails
        #   artifacts:   — file patterns to collect after the step completes
        #   timeout_seconds: — max wait time per step (default: 300)
        #
        # ═══════════════════════════════════════════════════════════════════════

        version: 1

        name: My Pipeline
        description: Describe your pipeline here

        # working_directory: .                   # Uncomment to set a custom working dir

        # ── Variables ─────────────────────────────────────────────────────────
        # Define your own. Reference with ${VARIABLE_NAME} in commands.
        variables:
          MY_VAR: my_value

        # ── Watch ─────────────────────────────────────────────────────────────
        # Auto-trigger on file changes. Remove for manual-only.
        watch:
        - path: .
          filter: '*.*'
          debounce_ms: 500

        # ── Stages ────────────────────────────────────────────────────────────
        stages:

          - name: build
            steps:
              - name: My Step
                command: echo
                arguments: Hello from PipeForge!
                # breakpoint: Always — pauses before this step so you can inspect state.
                breakpoint: Always
        """;
}
