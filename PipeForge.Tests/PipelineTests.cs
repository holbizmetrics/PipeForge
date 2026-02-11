using PipeForge.Core.Engine;
using PipeForge.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

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
