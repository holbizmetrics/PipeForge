using System.Collections.ObjectModel;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PipeForge.Core.Engine;

namespace PipeForge.GUI.ViewModels;

public partial class PipelineEditorViewModel : ObservableObject
{
    private readonly System.Timers.Timer _debounceTimer;
    private string? _currentFilePath;

    [ObservableProperty]
    private string _yamlContent = string.Empty;

    [ObservableProperty]
    private string _validationOutput = string.Empty;

    [ObservableProperty]
    private string _statusText = "No file loaded";

    [ObservableProperty]
    private bool _hasErrors;

    [ObservableProperty]
    private bool _hasWarnings;

    [ObservableProperty]
    private ObservableCollection<ValidationMessageItem> _validationMessages = new();

    public PipelineEditorViewModel()
    {
        _debounceTimer = new System.Timers.Timer(500);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RunValidation);
        };
    }

    partial void OnYamlContentChanged(string value)
    {
        _debounceTimer.Stop();
        if (!string.IsNullOrWhiteSpace(value))
            _debounceTimer.Start();
    }

    public void LoadContent(string yaml, string? filePath)
    {
        _currentFilePath = filePath;
        YamlContent = yaml;
        StatusText = filePath != null ? $"Loaded: {filePath}" : "New pipeline";
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;

        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Open Pipeline YAML",
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("YAML files")
                    {
                        Patterns = new[] { "*.yml", "*.yaml" }
                    }
                },
                AllowMultiple = false
            });

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            _currentFilePath = path;
            YamlContent = await File.ReadAllTextAsync(path);
            StatusText = $"Loaded: {path}";
        }
    }

    [RelayCommand]
    private async Task SaveFileAsync()
    {
        if (_currentFilePath != null)
        {
            await File.WriteAllTextAsync(_currentFilePath, YamlContent);
            StatusText = $"Saved: {_currentFilePath}";
            return;
        }

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;

        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save Pipeline YAML",
                DefaultExtension = "yml",
                SuggestedFileName = "pipeforge.yml"
            });

        if (file != null)
        {
            _currentFilePath = file.Path.LocalPath;
            await File.WriteAllTextAsync(_currentFilePath, YamlContent);
            StatusText = $"Saved: {_currentFilePath}";
        }
    }

    [RelayCommand]
    private void Validate() => RunValidation();

    private void RunValidation()
    {
        ValidationMessages.Clear();

        if (string.IsNullOrWhiteSpace(YamlContent))
        {
            ValidationOutput = "";
            HasErrors = false;
            HasWarnings = false;
            return;
        }

        var result = PipelineValidator.ValidateYaml(YamlContent);

        HasErrors = result.HasErrors;
        HasWarnings = result.HasWarnings;

        foreach (var msg in result.Messages)
        {
            ValidationMessages.Add(new ValidationMessageItem(
                msg.Severity == ValidationSeverity.Error ? "ERROR" : "WARN",
                msg.Location,
                msg.Message,
                msg.Severity == ValidationSeverity.Error));
        }

        if (result.Messages.Count == 0)
            ValidationOutput = "Valid. No issues found.";
        else
            ValidationOutput = $"{result.Errors.Count()} error(s), {result.Warnings.Count()} warning(s)";
    }
}

public record ValidationMessageItem(string Severity, string Location, string Message, bool IsError);
