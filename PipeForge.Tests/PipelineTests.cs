using Xunit;
using PipeForge.Core.Models;
using PipeForge.Core.Engine;
using PipeForge.Core.Templates;

namespace PipeForge.Tests;

public class PipelineLoaderTests
{
    [Fact]
    public void LoadFromYaml_ParsesBasicPipeline()
    {
        var yaml = @"
name: Test Pipeline
description: A test
working_directory: /tmp
variables:
  FOO: bar
  BAZ: qux
stages:
  - name: build
    steps:
      - name: Echo
        command: echo
        arguments: hello
        timeout_seconds: 30
";

        var pipeline = PipelineLoader.LoadFromYaml(yaml);

        Assert.Equal("Test Pipeline", pipeline.Name);
        Assert.Equal("/tmp", pipeline.WorkingDirectory);
        Assert.Equal("bar", pipeline.Variables["FOO"]);
        Assert.Single(pipeline.Stages);
        Assert.Single(pipeline.Stages[0].Steps);
        Assert.Equal("echo", pipeline.Stages[0].Steps[0].Command);
        Assert.Equal(30, pipeline.Stages[0].Steps[0].TimeoutSeconds);
    }

    [Fact]
    public void LoadFromYaml_ParsesWatchTriggers()
    {
        var yaml = @"
name: Watch Test
watch:
  - path: ./src
    filter: '*.cs'
    include_subdirectories: true
    debounce_ms: 2000
stages: []
";

        var pipeline = PipelineLoader.LoadFromYaml(yaml);

        Assert.Single(pipeline.Watch);
        Assert.Equal("./src", pipeline.Watch[0].Path);
        Assert.Equal("*.cs", pipeline.Watch[0].Filter);
        Assert.True(pipeline.Watch[0].IncludeSubdirectories);
        Assert.Equal(2000, pipeline.Watch[0].DebounceMs);
    }

    [Fact]
    public void RoundTrip_PreservesStructure()
    {
        var original = PipelineTemplates.DotNetBuild("TestProject", "./Test.sln");
        var yaml = PipelineLoader.ToYaml(original);
        var loaded = PipelineLoader.LoadFromYaml(yaml);

        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Stages.Count, loaded.Stages.Count);
        Assert.Equal(original.Variables.Count, loaded.Variables.Count);
    }
}

public class PipelineRunModelTests
{
    [Fact]
    public void PipelineRun_TracksState()
    {
        var run = new PipelineRun
        {
            PipelineName = "Test",
            Status = PipelineRunStatus.Running
        };

        Assert.NotEqual(Guid.Empty, run.RunId);
        Assert.False(run.HasFailures);
        Assert.Equal(0, run.SuccessCount);
    }

    [Fact]
    public void StepResult_FullOutput_CombinesStreams()
    {
        var result = new StepResult
        {
            StepName = "test",
            StageName = "build"
        };

        var now = DateTime.UtcNow;
        result.StandardOutput.Add(new OutputLine 
        { 
            Text = "line 1", 
            Source = OutputSource.StdOut, 
            Timestamp = now 
        });
        result.StandardError.Add(new OutputLine 
        { 
            Text = "error 1", 
            Source = OutputSource.StdErr, 
            Timestamp = now.AddMilliseconds(50) 
        });
        result.StandardOutput.Add(new OutputLine 
        { 
            Text = "line 2", 
            Source = OutputSource.StdOut, 
            Timestamp = now.AddMilliseconds(100) 
        });

        var full = result.FullOutput;
        Assert.Contains("line 1", full);
        Assert.Contains("error 1", full);
        Assert.Contains("line 2", full);
    }

    [Fact]
    public void StepResult_LastStderrLines_ReturnsLastN()
    {
        var result = new StepResult { StepName = "test", StageName = "build" };
        for (int i = 1; i <= 20; i++)
            result.StandardError.Add(new OutputLine { Text = $"error line {i}", Source = OutputSource.StdErr });

        var last10 = result.LastStderrLines();
        Assert.Equal(10, last10.Count);
        Assert.Equal("error line 11", last10[0]);
        Assert.Equal("error line 20", last10[9]);

        var last3 = result.LastStderrLines(3);
        Assert.Equal(3, last3.Count);
        Assert.Equal("error line 18", last3[0]);
    }

    [Fact]
    public void StepResult_ErrorSummary_IncludesStderrAndHints()
    {
        var result = new StepResult
        {
            StepName = "build",
            StageName = "compile",
            Status = StepStatus.Failed,
            ExitCode = 1
        };
        result.StandardError.Add(new OutputLine { Text = "fatal error CS1234", Source = OutputSource.StdErr });
        result.Hints.Add("Review the compiler errors above.");

        var summary = result.ErrorSummary;
        Assert.Contains("Exit code 1", summary);
        Assert.Contains("fatal error CS1234", summary);
        Assert.Contains("Review the compiler errors above.", summary);
    }

    [Fact]
    public void StepResult_ErrorSummary_EmptyWhenNotFailed()
    {
        var result = new StepResult { Status = StepStatus.Success };
        Assert.Equal(string.Empty, result.ErrorSummary);
    }
}

public class ErrorHintsTests
{
    [Fact]
    public void Analyze_CommandNotRecognized_SuggestsPath()
    {
        var result = MakeFailedResult("'dotnet' is not recognized as an internal or external command");
        ErrorHints.Analyze(result);

        Assert.Single(result.Hints);
        Assert.Contains("PATH", result.Hints[0]);
    }

    [Fact]
    public void Analyze_CommandNotFound_SuggestsPath()
    {
        var result = MakeFailedResult("bash: grype: command not found");
        ErrorHints.Analyze(result);

        Assert.Single(result.Hints);
        Assert.Contains("PATH", result.Hints[0]);
    }

    [Fact]
    public void Analyze_AccessDenied_SuggestsPermissions()
    {
        var result = MakeFailedResult("Access is denied.");
        ErrorHints.Analyze(result);

        Assert.Single(result.Hints);
        Assert.Contains("permissions", result.Hints[0]);
    }

    [Fact]
    public void Analyze_BuildFailed_SuggestsReview()
    {
        var result = MakeFailedResult("Build FAILED.\n  error CS0246: The type or namespace...");
        ErrorHints.Analyze(result);

        Assert.Contains(result.Hints, h => h.Contains("compiler errors"));
    }

    [Fact]
    public void Analyze_NoMatch_NoHints()
    {
        var result = MakeFailedResult("some unknown output");
        ErrorHints.Analyze(result);

        Assert.Empty(result.Hints);
    }

    [Fact]
    public void Analyze_SuccessStatus_NoHints()
    {
        var result = new StepResult { Status = StepStatus.Success };
        result.StandardError.Add(new OutputLine { Text = "command not found", Source = OutputSource.StdErr });
        ErrorHints.Analyze(result);

        Assert.Empty(result.Hints);
    }

    [Fact]
    public void Analyze_NoDuplicateHints()
    {
        // Both patterns match "PATH" hint — should only appear once
        var result = MakeFailedResult("command not found\n'foo' is not recognized as an internal command");
        ErrorHints.Analyze(result);

        Assert.Single(result.Hints);
    }

    private static StepResult MakeFailedResult(string stderrText)
    {
        var result = new StepResult
        {
            StepName = "test",
            StageName = "build",
            Status = StepStatus.Failed,
            ExitCode = 1
        };
        foreach (var line in stderrText.Split('\n'))
            result.StandardError.Add(new OutputLine { Text = line, Source = OutputSource.StdErr });
        return result;
    }
}

public class TemplateTests
{
    [Fact]
    public void InnoSetupTemplate_HasCorrectStructure()
    {
        var pipeline = PipelineTemplates.InnoSetupInstaller("TestApp", @"C:\test.iss");

        Assert.Contains("TestApp", pipeline.Name);
        Assert.Equal(4, pipeline.Stages.Count); // validate, compile, sign, verify
        Assert.Single(pipeline.Watch);
        Assert.Equal("*.iss", pipeline.Watch[0].Filter);
    }

    [Fact]
    public void Compose_MergesMultiplePipelines()
    {
        var a = PipelineTemplates.DotNetBuild("A", "./A.sln");
        var b = PipelineTemplates.SecurityScan("B", "./B");

        var composed = PipelineTemplates.Compose("Combined", "A + B", a, b);

        Assert.True(composed.Stages.Count > a.Stages.Count);
        Assert.True(composed.Variables.Count >= a.Variables.Count);
    }
}

public class CommentedYamlTemplateTests
{
    [Theory]
    [InlineData("innosetup")]
    [InlineData("dotnet")]
    [InlineData("security")]
    [InlineData("twincat")]
    [InlineData("custom")]
    public void CommentedYaml_RoundTrips(string templateName)
    {
        var yaml = CommentedYamlTemplates.GetTemplate(templateName);
        var pipeline = PipelineLoader.LoadFromYaml(yaml);

        Assert.NotNull(pipeline);
        Assert.NotEmpty(pipeline.Name);
        Assert.NotEmpty(pipeline.Stages);
    }

    [Fact]
    public void CommentedYaml_InnoSetup_MatchesStructure()
    {
        var yaml = CommentedYamlTemplates.GetTemplate("innosetup");
        var pipeline = PipelineLoader.LoadFromYaml(yaml);

        Assert.Equal("MyApp - Inno Setup Build", pipeline.Name);
        Assert.Equal(4, pipeline.Stages.Count);
        Assert.Single(pipeline.Watch);
        Assert.Equal("*.iss", pipeline.Watch[0].Filter);
        Assert.Equal(BreakpointMode.OnFailure, pipeline.Stages[1].Steps[0].Breakpoint);
        Assert.NotNull(pipeline.Stages[2].Condition);
        Assert.Equal("SIGNTOOL_PATH", pipeline.Stages[2].Condition!.OnlyIf);
    }

    [Fact]
    public void CommentedYaml_DotNet_MatchesStructure()
    {
        var yaml = CommentedYamlTemplates.GetTemplate("dotnet");
        var pipeline = PipelineLoader.LoadFromYaml(yaml);

        Assert.Equal("MyProject - .NET Build", pipeline.Name);
        Assert.Equal(4, pipeline.Stages.Count);
        Assert.Equal("Release", pipeline.Variables["CONFIGURATION"]);
        Assert.Equal(BreakpointMode.OnFailure, pipeline.Stages[1].Steps[0].Breakpoint);
    }

    [Fact]
    public void CommentedYaml_Security_MatchesStructure()
    {
        var yaml = CommentedYamlTemplates.GetTemplate("security");
        var pipeline = PipelineLoader.LoadFromYaml(yaml);

        Assert.Equal("MyProject - Security Scan", pipeline.Name);
        Assert.Equal(3, pipeline.Stages.Count);
        Assert.True(pipeline.Stages[1].Steps[0].AllowFailure);
    }

    [Fact]
    public void CommentedYaml_Custom_HasBreakpointAlways()
    {
        var yaml = CommentedYamlTemplates.GetTemplate("custom");
        var pipeline = PipelineLoader.LoadFromYaml(yaml);

        Assert.Equal("My Pipeline", pipeline.Name);
        Assert.Equal(BreakpointMode.Always, pipeline.Stages[0].Steps[0].Breakpoint);
    }

    [Fact]
    public void GetTemplate_UnknownName_Throws()
    {
        Assert.Throws<ArgumentException>(() => CommentedYamlTemplates.GetTemplate("nonexistent"));
    }
}

public class TrustStoreTests
{
    [Fact]
    public void ComputeHash_ReturnsConsistentSha256()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello world");
            var hash1 = TrustStore.ComputeHash(tempFile);
            var hash2 = TrustStore.ComputeHash(tempFile);

            Assert.Equal(hash1, hash2);
            Assert.Equal(64, hash1.Length); // SHA-256 = 64 hex chars
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void ComputeHash_DifferentContent_DifferentHash()
    {
        var file1 = Path.GetTempFileName();
        var file2 = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file1, "hello");
            File.WriteAllText(file2, "world");
            Assert.NotEqual(TrustStore.ComputeHash(file1), TrustStore.ComputeHash(file2));
        }
        finally { File.Delete(file1); File.Delete(file2); }
    }

    [Fact]
    public void Check_NewFile_ReturnsNew()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"pipeforge-test-{Guid.NewGuid():N}");
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");
            var store = new TrustStore(configDir);
            var result = store.Check(tempFile);

            Assert.Equal(TrustStatus.New, result.Status);
            Assert.NotEmpty(result.CurrentHash);
            Assert.Null(result.PreviousHash);
        }
        finally
        {
            File.Delete(tempFile);
            if (Directory.Exists(configDir)) Directory.Delete(configDir, true);
        }
    }

    [Fact]
    public void Trust_ThenCheck_ReturnsTrusted()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"pipeforge-test-{Guid.NewGuid():N}");
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");
            var store = new TrustStore(configDir);
            store.Trust(tempFile);
            var result = store.Check(tempFile);

            Assert.Equal(TrustStatus.Trusted, result.Status);
        }
        finally
        {
            File.Delete(tempFile);
            if (Directory.Exists(configDir)) Directory.Delete(configDir, true);
        }
    }

    [Fact]
    public void Trust_ModifyFile_ReturnsModified()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"pipeforge-test-{Guid.NewGuid():N}");
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "original content");
            var store = new TrustStore(configDir);
            store.Trust(tempFile);

            File.WriteAllText(tempFile, "modified content");
            var result = store.Check(tempFile);

            Assert.Equal(TrustStatus.Modified, result.Status);
            Assert.NotNull(result.PreviousHash);
            Assert.NotEqual(result.PreviousHash, result.CurrentHash);
        }
        finally
        {
            File.Delete(tempFile);
            if (Directory.Exists(configDir)) Directory.Delete(configDir, true);
        }
    }

    [Fact]
    public void Trust_Persists_AcrossInstances()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"pipeforge-test-{Guid.NewGuid():N}");
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");

            var store1 = new TrustStore(configDir);
            store1.Trust(tempFile);

            // New instance reads from disk — simulates app restart
            var store2 = new TrustStore(configDir);
            var result = store2.Check(tempFile);

            Assert.Equal(TrustStatus.Trusted, result.Status);
        }
        finally
        {
            File.Delete(tempFile);
            if (Directory.Exists(configDir)) Directory.Delete(configDir, true);
        }
    }

    [Fact]
    public void CorruptStoreFile_RecoverGracefully()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"pipeforge-test-{Guid.NewGuid():N}");
        var tempFile = Path.GetTempFileName();
        try
        {
            Directory.CreateDirectory(configDir);
            File.WriteAllText(Path.Combine(configDir, "trusted-hashes.json"), "not valid json {{{");

            File.WriteAllText(tempFile, "test");
            var store = new TrustStore(configDir);
            var result = store.Check(tempFile);

            Assert.Equal(TrustStatus.New, result.Status); // Treats everything as new
        }
        finally
        {
            File.Delete(tempFile);
            if (Directory.Exists(configDir)) Directory.Delete(configDir, true);
        }
    }

    [Fact]
    public void Trust_WithExplicitHash_StoresThatHash()
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"pipeforge-test-{Guid.NewGuid():N}");
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");
            var store = new TrustStore(configDir);
            var hash = TrustStore.ComputeHash(tempFile);

            store.Trust(tempFile, hash);
            var result = store.Check(tempFile);

            Assert.Equal(TrustStatus.Trusted, result.Status);
            Assert.Equal(hash, result.CurrentHash);
        }
        finally
        {
            File.Delete(tempFile);
            if (Directory.Exists(configDir)) Directory.Delete(configDir, true);
        }
    }
}

public class PathNormalizerTests
{
    [Fact]
    public void Normalize_TildeExpansion_ResolvesToHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = PathNormalizer.Normalize("~/projects/test");

        Assert.StartsWith(home, result);
        Assert.Contains("projects", result);
        Assert.Contains("test", result);
    }

    [Fact]
    public void Normalize_TildeAlone_ResolvesToHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(home, PathNormalizer.Normalize("~"));
    }

    [Fact]
    public void Normalize_MixedSeparators_NormalizesToOs()
    {
        var result = PathNormalizer.Normalize("C:\\projects/test\\build/output");

        // On Windows, all should be backslash; on Linux, all forward slash
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar.ToString(), result);
    }

    [Fact]
    public void Normalize_RelativePath_ResolvesToAbsolute()
    {
        var basePath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var result = PathNormalizer.Normalize("./subdir/file.txt", basePath);

        Assert.True(Path.IsPathRooted(result));
        Assert.Contains("subdir", result);
    }

    [Fact]
    public void Normalize_DotDotSegments_Resolved()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "a", "b");
        var result = PathNormalizer.Normalize("../c/file.txt", basePath);

        Assert.DoesNotContain("..", result);
        Assert.Contains("c", result);
    }

    [Fact]
    public void Normalize_AbsolutePath_StaysAbsolute()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "test", "path");
        var result = PathNormalizer.Normalize(absolute);

        Assert.Equal(Path.GetFullPath(absolute), result);
    }

    [Fact]
    public void Normalize_EmptyOrWhitespace_ReturnsAsIs()
    {
        Assert.Equal("", PathNormalizer.Normalize(""));
        Assert.Equal("  ", PathNormalizer.Normalize("  "));
    }

    [Fact]
    public void NormalizeSeparators_MixedSlashes_NormalizesToOs()
    {
        var result = PathNormalizer.NormalizeSeparators("bin/Release/net10.0\\publish");

        Assert.DoesNotContain(Path.AltDirectorySeparatorChar.ToString(), result);
    }

    [Fact]
    public void NormalizeSeparators_StaysRelative()
    {
        var result = PathNormalizer.NormalizeSeparators("./output/build");

        Assert.StartsWith(".", result);
    }

    [Fact]
    public void NormalizeSeparators_EmptyOrWhitespace_ReturnsAsIs()
    {
        Assert.Equal("", PathNormalizer.NormalizeSeparators(""));
        Assert.Null(PathNormalizer.NormalizeSeparators(null!));
    }
}

public class PipelineValidatorTests
{
    [Fact]
    public void ValidPipeline_NoIssues()
    {
        var yaml = @"
name: Valid Pipeline
version: 1
variables:
  MY_VAR: hello
stages:
  - name: build
    steps:
      - name: Echo
        command: echo
        arguments: ${MY_VAR}
";
        var result = PipelineValidator.ValidateYaml(yaml);

        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void InvalidYaml_ReportsParseError()
    {
        var result = PipelineValidator.ValidateYaml("stages: [[[invalid yaml");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, m => m.Message.Contains("parse"));
    }

    [Fact]
    public void NoStages_ReportsError()
    {
        var yaml = @"
name: Empty Pipeline
stages: []
";
        var result = PipelineValidator.ValidateYaml(yaml);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, m => m.Message.Contains("no stages"));
    }

    [Fact]
    public void EmptyCommand_ReportsError()
    {
        var yaml = @"
name: Bad Pipeline
stages:
  - name: build
    steps:
      - name: Missing Command
        command: ''
";
        var result = PipelineValidator.ValidateYaml(yaml);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, m => m.Message.Contains("no command"));
    }

    [Fact]
    public void UndefinedVariable_ReportsWarning()
    {
        var yaml = @"
name: Var Test
stages:
  - name: build
    steps:
      - name: Use Var
        command: echo
        arguments: ${UNDEFINED_VAR}
";
        var result = PipelineValidator.ValidateYaml(yaml);

        Assert.True(result.IsValid); // warnings don't make it invalid
        Assert.True(result.HasWarnings);
        Assert.Contains(result.Warnings, m => m.Message.Contains("UNDEFINED_VAR"));
    }

    [Fact]
    public void DefinedVariable_NoWarning()
    {
        var yaml = @"
name: Var Test
version: 1
variables:
  MY_VAR: hello
stages:
  - name: build
    steps:
      - name: Use Var
        command: echo
        arguments: ${MY_VAR}
";
        var result = PipelineValidator.ValidateYaml(yaml);

        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void DuplicateStageNames_ReportsError()
    {
        var yaml = @"
name: Dup Test
stages:
  - name: build
    steps:
      - name: Step 1
        command: echo
  - name: build
    steps:
      - name: Step 2
        command: echo
";
        var result = PipelineValidator.ValidateYaml(yaml);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, m => m.Message.Contains("Duplicate stage name"));
    }

    [Fact]
    public void ConditionReferencesUndefinedVariable_ReportsWarning()
    {
        var yaml = @"
name: Condition Test
stages:
  - name: optional
    condition:
      only_if: MAYBE_SET
    steps:
      - name: Step
        command: echo
";
        var result = PipelineValidator.ValidateYaml(yaml);

        Assert.True(result.HasWarnings);
        Assert.Contains(result.Warnings, m => m.Message.Contains("MAYBE_SET"));
    }

    [Theory]
    [InlineData("innosetup")]
    [InlineData("dotnet")]
    [InlineData("security")]
    [InlineData("twincat")]
    [InlineData("custom")]
    public void CommentedTemplates_PassValidation(string templateName)
    {
        var yaml = CommentedYamlTemplates.GetTemplate(templateName);
        var result = PipelineValidator.ValidateYaml(yaml);

        // Templates should have no errors
        Assert.False(result.HasErrors,
            $"Template '{templateName}' has errors: " +
            string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void FileNotFound_ReportsError()
    {
        var result = PipelineValidator.ValidateFile("/nonexistent/path.yml");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, m => m.Message.Contains("not found"));
    }

    [Fact]
    public void MissingVersion_ReportsWarning()
    {
        var yaml = @"
name: No Version
stages:
  - name: build
    steps:
      - name: Echo
        command: echo
";
        var result = PipelineValidator.ValidateYaml(yaml);

        Assert.True(result.HasWarnings);
        Assert.Contains(result.Warnings, m => m.Location == "version" && m.Message.Contains("version"));
    }

    [Fact]
    public void CurrentVersion_NoVersionWarning()
    {
        var yaml = @"
name: Versioned
version: 1
stages:
  - name: build
    steps:
      - name: Echo
        command: echo
";
        var result = PipelineValidator.ValidateYaml(yaml);

        Assert.DoesNotContain(result.Warnings, m => m.Location == "version");
    }

    [Fact]
    public void FutureVersion_WarnsNewer()
    {
        var yaml = @"
name: Future
version: 99
stages:
  - name: build
    steps:
      - name: Echo
        command: echo
";
        var result = PipelineValidator.ValidateYaml(yaml);

        Assert.Contains(result.Warnings, m => m.Location == "version" && m.Message.Contains("newer"));
    }
}
