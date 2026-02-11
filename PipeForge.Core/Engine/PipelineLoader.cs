using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using PipeForge.Core.Models;

namespace PipeForge.Core.Engine;

/// <summary>
/// Loads pipeline definitions from YAML files.
/// Supports variable overrides and template inheritance.
/// </summary>
public class PipelineLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .Build();

    /// <summary>
    /// Load a pipeline from a YAML file.
    /// </summary>
    public static PipelineDefinition LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Pipeline file not found: {path}");

        var yaml = File.ReadAllText(path);
        return LoadFromYaml(yaml);
    }

    /// <summary>
    /// Load a pipeline from a YAML string.
    /// </summary>
    public static PipelineDefinition LoadFromYaml(string yaml)
    {
        return Deserializer.Deserialize<PipelineDefinition>(yaml) 
            ?? throw new InvalidOperationException("Failed to parse pipeline YAML");
    }

    /// <summary>
    /// Load a template, apply variable overrides, and return a ready-to-run pipeline.
    /// This is how building blocks work: load template, fill in your values, go.
    /// </summary>
    public static PipelineDefinition LoadFromTemplate(
        string templatePath, 
        Dictionary<string, string>? variableOverrides = null)
    {
        var pipeline = LoadFromFile(templatePath);

        if (variableOverrides != null)
        {
            foreach (var (key, value) in variableOverrides)
            {
                pipeline.Variables[key] = value;
            }
        }

        return pipeline;
    }

    /// <summary>
    /// Save a pipeline definition to YAML. Useful for generating templates
    /// programmatically or saving modified pipelines.
    /// </summary>
    public static void SaveToFile(PipelineDefinition pipeline, string path)
    {
        var yaml = Serializer.Serialize(pipeline);
        var directory = Path.GetDirectoryName(path);
        if (directory != null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, yaml);
    }

    /// <summary>
    /// Serialize a pipeline to YAML string.
    /// </summary>
    public static string ToYaml(PipelineDefinition pipeline)
    {
        return Serializer.Serialize(pipeline);
    }
}
