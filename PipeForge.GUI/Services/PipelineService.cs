using Microsoft.Extensions.Logging;
using PipeForge.Core.Engine;
using PipeForge.Core.Models;

namespace PipeForge.GUI.Services;

/// <summary>
/// Service layer wrapping PipeForge.Core for GUI consumption.
/// Creates engine instances with proper logging.
/// </summary>
public class PipelineService : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;

    public PipelineService()
    {
        _loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
    }

    public PipelineEngine CreateEngine()
        => new PipelineEngine(_loggerFactory.CreateLogger<PipelineEngine>());

    public PipelineDefinition LoadPipeline(string path)
        => PipelineLoader.LoadFromFile(path);

    public ValidationResult ValidateYaml(string yaml)
        => PipelineValidator.ValidateYaml(yaml);

    public TrustCheckResult CheckTrust(string path)
    {
        var store = new TrustStore();
        return store.Check(path);
    }

    public void MarkTrusted(string path, string hash)
    {
        var store = new TrustStore();
        store.Trust(path, hash);
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
    }
}
