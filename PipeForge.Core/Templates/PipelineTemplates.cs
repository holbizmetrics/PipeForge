using PipeForge.Core.Models;
using PipeForge.Core.Engine;

namespace PipeForge.Core.Templates;

/// <summary>
/// Pre-built pipeline templates — the building blocks.
/// Each method returns a fully configured PipelineDefinition
/// that you can use as-is or customize.
/// 
/// Usage: 
///   var pipeline = PipelineTemplates.InnoSetupInstaller("MyApp", @"C:\src\myapp.iss");
///   engine.RunAsync(pipeline);
/// </summary>
public static class PipelineTemplates
{
    /// <summary>
    /// Inno Setup compiler pipeline.
    /// Watches the .iss file, compiles on change, optionally signs and tests.
    /// </summary>
    public static PipelineDefinition InnoSetupInstaller(
        string projectName,
        string issFilePath,
        string? outputDir = null,
        string? signtoolPath = null)
    {
        outputDir ??= Path.Combine(Path.GetDirectoryName(issFilePath) ?? ".", "Output");

        var pipeline = new PipelineDefinition
        {
            Name = $"{projectName} - Inno Setup Build",
            Description = $"Compile and package {projectName} installer",
            WorkingDirectory = Path.GetDirectoryName(issFilePath) ?? ".",
            Variables = new Dictionary<string, string>
            {
                ["PROJECT_NAME"] = projectName,
                ["ISS_FILE"] = issFilePath,
                ["OUTPUT_DIR"] = outputDir,
                ["ISCC_PATH"] = @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
            },
            Watch = new List<WatchTrigger>
            {
                new()
                {
                    Path = Path.GetDirectoryName(issFilePath) ?? ".",
                    Filter = "*.iss",
                    DebounceMs = 1000
                }
            },
            Stages = new List<PipelineStage>
            {
                new()
                {
                    Name = "validate",
                    Steps = new List<PipelineStep>
                    {
                        new()
                        {
                            Name = "Check ISS file exists",
                            Command = "cmd.exe",
                            Arguments = "/c if not exist \"${ISS_FILE}\" exit 1",
                            Description = "Verify the .iss script file exists"
                        }
                    }
                },
                new()
                {
                    Name = "compile",
                    Steps = new List<PipelineStep>
                    {
                        new()
                        {
                            Name = "Compile Installer",
                            Command = "${ISCC_PATH}",
                            Arguments = "/O\"${OUTPUT_DIR}\" \"${ISS_FILE}\"",
                            Description = "Run Inno Setup Compiler",
                            TimeoutSeconds = 120,
                            Artifacts = new List<string> { "${OUTPUT_DIR}/*.exe" },
                            Breakpoint = BreakpointMode.OnFailure
                        }
                    }
                },
                new()
                {
                    Name = "sign",
                    Condition = new StageCondition
                    {
                        OnlyIf = "SIGNTOOL_PATH"
                    },
                    Steps = new List<PipelineStep>
                    {
                        new()
                        {
                            Name = "Sign Installer",
                            Command = "${SIGNTOOL_PATH}",
                            Arguments = "sign /tr http://timestamp.digicert.com /td sha256 /fd sha256 /a \"${OUTPUT_DIR}/*.exe\"",
                            Description = "Code sign the installer executable"
                        }
                    }
                },
                new()
                {
                    Name = "verify",
                    Steps = new List<PipelineStep>
                    {
                        new()
                        {
                            Name = "Check Output",
                            Command = "cmd.exe",
                            Arguments = "/c dir \"${OUTPUT_DIR}\\*.exe\"",
                            Description = "Verify installer was produced"
                        }
                    }
                }
            }
        };

        if (signtoolPath != null)
            pipeline.Variables["SIGNTOOL_PATH"] = signtoolPath;

        return pipeline;
    }

    /// <summary>
    /// .NET build + test pipeline.
    /// </summary>
    public static PipelineDefinition DotNetBuild(
        string projectName,
        string solutionPath,
        string configuration = "Release")
    {
        return new PipelineDefinition
        {
            Name = $"{projectName} - .NET Build",
            Description = $"Build, test, and package {projectName}",
            WorkingDirectory = Path.GetDirectoryName(solutionPath) ?? ".",
            Variables = new Dictionary<string, string>
            {
                ["PROJECT_NAME"] = projectName,
                ["SOLUTION"] = solutionPath,
                ["CONFIGURATION"] = configuration
            },
            Watch = new List<WatchTrigger>
            {
                new()
                {
                    Path = Path.GetDirectoryName(solutionPath) ?? ".",
                    Filter = "*.cs",
                    IncludeSubdirectories = true,
                    DebounceMs = 2000
                }
            },
            Stages = new List<PipelineStage>
            {
                new()
                {
                    Name = "restore",
                    Steps = new List<PipelineStep>
                    {
                        new()
                        {
                            Name = "NuGet Restore",
                            Command = "dotnet",
                            Arguments = "restore \"${SOLUTION}\"",
                            TimeoutSeconds = 120
                        }
                    }
                },
                new()
                {
                    Name = "build",
                    Steps = new List<PipelineStep>
                    {
                        new()
                        {
                            Name = "Build Solution",
                            Command = "dotnet",
                            Arguments = "build \"${SOLUTION}\" -c ${CONFIGURATION} --no-restore",
                            TimeoutSeconds = 300,
                            Breakpoint = BreakpointMode.OnFailure
                        }
                    }
                },
                new()
                {
                    Name = "test",
                    Steps = new List<PipelineStep>
                    {
                        new()
                        {
                            Name = "Run Tests",
                            Command = "dotnet",
                            Arguments = "test \"${SOLUTION}\" -c ${CONFIGURATION} --no-build --logger trx",
                            TimeoutSeconds = 300,
                            Artifacts = new List<string> { "**/*.trx" },
                            Breakpoint = BreakpointMode.OnFailure
                        }
                    }
                },
                new()
                {
                    Name = "publish",
                    Steps = new List<PipelineStep>
                    {
                        new()
                        {
                            Name = "Publish",
                            Command = "dotnet",
                            Arguments = "publish \"${SOLUTION}\" -c ${CONFIGURATION} --no-build -o ./publish",
                            Artifacts = new List<string> { "./publish/**" }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Generic file processing pipeline — watch files, run commands.
    /// The simplest building block.
    /// </summary>
    public static PipelineDefinition FileProcessor(
        string name,
        string watchPath,
        string watchFilter,
        List<(string stepName, string command, string? args)> steps)
    {
        return new PipelineDefinition
        {
            Name = name,
            WorkingDirectory = watchPath,
            Watch = new List<WatchTrigger>
            {
                new()
                {
                    Path = watchPath,
                    Filter = watchFilter,
                    DebounceMs = 500
                }
            },
            Stages = new List<PipelineStage>
            {
                new()
                {
                    Name = "process",
                    Steps = steps.Select(s => new PipelineStep
                    {
                        Name = s.stepName,
                        Command = s.command,
                        Arguments = s.args,
                        Breakpoint = BreakpointMode.OnFailure
                    }).ToList()
                }
            }
        };
    }

    /// <summary>
    /// Security scanning pipeline (Grype/Syft style — you'll appreciate this one, Holger).
    /// </summary>
    public static PipelineDefinition SecurityScan(
        string projectName,
        string targetPath,
        string? reportOutputDir = null)
    {
        reportOutputDir ??= Path.Combine(targetPath, "security-reports");

        return new PipelineDefinition
        {
            Name = $"{projectName} - Security Scan",
            Description = "SBOM generation and vulnerability scanning",
            WorkingDirectory = targetPath,
            Variables = new Dictionary<string, string>
            {
                ["PROJECT_NAME"] = projectName,
                ["TARGET"] = targetPath,
                ["REPORT_DIR"] = reportOutputDir
            },
            Stages = new List<PipelineStage>
            {
                new()
                {
                    Name = "sbom",
                    Steps = new List<PipelineStep>
                    {
                        new()
                        {
                            Name = "Generate SBOM (Syft)",
                            Command = "syft",
                            Arguments = "dir:${TARGET} -o spdx-json=${REPORT_DIR}/sbom.json",
                            TimeoutSeconds = 300,
                            Artifacts = new List<string> { "${REPORT_DIR}/sbom.json" }
                        }
                    }
                },
                new()
                {
                    Name = "scan",
                    Steps = new List<PipelineStep>
                    {
                        new()
                        {
                            Name = "Vulnerability Scan (Grype)",
                            Command = "grype",
                            Arguments = "sbom:${REPORT_DIR}/sbom.json -o json --file ${REPORT_DIR}/vulns.json",
                            TimeoutSeconds = 300,
                            AllowFailure = true, // Grype returns non-zero when vulns found
                            Artifacts = new List<string> { "${REPORT_DIR}/vulns.json" },
                            Breakpoint = BreakpointMode.OnFailure
                        }
                    }
                },
                new()
                {
                    Name = "report",
                    Steps = new List<PipelineStep>
                    {
                        new()
                        {
                            Name = "Summary Report",
                            Command = "grype",
                            Arguments = "sbom:${REPORT_DIR}/sbom.json -o table",
                            AllowFailure = true
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// TwinCAT / PLC build pipeline.
    /// </summary>
    public static PipelineDefinition TwinCatBuild(
        string projectName,
        string solutionPath)
    {
        return new PipelineDefinition
        {
            Name = $"{projectName} - TwinCAT Build",
            Description = "Build and validate TwinCAT PLC project",
            WorkingDirectory = Path.GetDirectoryName(solutionPath) ?? ".",
            Variables = new Dictionary<string, string>
            {
                ["PROJECT_NAME"] = projectName,
                ["SOLUTION"] = solutionPath,
                ["TC_BUILD"] = @"C:\TwinCAT\3.1\Components\Plc\Common\TcBuild.exe"
            },
            Watch = new List<WatchTrigger>
            {
                new()
                {
                    Path = Path.GetDirectoryName(solutionPath) ?? ".",
                    Filter = "*.TcPOU",
                    IncludeSubdirectories = true,
                    DebounceMs = 2000
                }
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
                            Name = "Build TwinCAT Solution",
                            Command = "${TC_BUILD}",
                            Arguments = "\"${SOLUTION}\" /rebuild /configuration Release",
                            TimeoutSeconds = 600,
                            Breakpoint = BreakpointMode.OnFailure
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Compose multiple templates into a single pipeline.
    /// THIS is the building block composition.
    /// </summary>
    public static PipelineDefinition Compose(
        string name, 
        string? description,
        params PipelineDefinition[] pipelines)
    {
        var composed = new PipelineDefinition
        {
            Name = name,
            Description = description
        };

        foreach (var p in pipelines)
        {
            // Merge variables (later pipelines override earlier ones)
            foreach (var (key, value) in p.Variables)
                composed.Variables[key] = value;

            // Merge watch triggers
            composed.Watch.AddRange(p.Watch);

            // Append stages (prefix with source pipeline name to avoid collisions)
            foreach (var stage in p.Stages)
            {
                var prefixedStage = new PipelineStage
                {
                    Name = $"{p.Name}:{stage.Name}",
                    Steps = stage.Steps,
                    Condition = stage.Condition,
                    ContinueOnError = stage.ContinueOnError
                };
                composed.Stages.Add(prefixedStage);
            }
        }

        return composed;
    }
}
