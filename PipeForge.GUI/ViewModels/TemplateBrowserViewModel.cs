using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PipeForge.Core.Templates;

namespace PipeForge.GUI.ViewModels;

public partial class TemplateBrowserViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<TemplateItem> _templates;

    [ObservableProperty]
    private TemplateItem? _selectedTemplate;

    [ObservableProperty]
    private string _previewYaml = string.Empty;

    public TemplateBrowserViewModel()
    {
        _templates = new ObservableCollection<TemplateItem>
        {
            new("innosetup", "Inno Setup", "Inno Setup installer compilation + signing"),
            new("dotnet", ".NET Build", ".NET build, test, publish pipeline"),
            new("security", "Security Scan", "SBOM + vulnerability scanning (Syft/Grype)"),
            new("twincat", "TwinCAT", "TwinCAT/PLC build pipeline"),
            new("custom", "Custom", "Empty pipeline scaffold to fill in yourself")
        };
    }

    partial void OnSelectedTemplateChanged(TemplateItem? value)
    {
        if (value != null)
        {
            PreviewYaml = CommentedYamlTemplates.GetTemplate(value.Key);
        }
        else
        {
            PreviewYaml = string.Empty;
        }
    }

    [RelayCommand]
    private async Task CreateFromTemplateAsync()
    {
        if (SelectedTemplate == null || string.IsNullOrEmpty(PreviewYaml))
            return;

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;

        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save Pipeline From Template",
                DefaultExtension = "yml",
                SuggestedFileName = $"pipeforge-{SelectedTemplate.Key}.yml"
            });

        if (file != null)
        {
            var path = file.Path.LocalPath;
            try
            {
                await File.WriteAllTextAsync(path, PreviewYaml);

                // Navigate to editor with the new file
                if (topLevel.DataContext is MainWindowViewModel mainVm)
                {
                    mainVm.NavigateToEditorWithContent(PreviewYaml, path);
                }
            }
            catch (Exception ex)
            {
                // Show error in preview area since we don't have a StatusText here
                PreviewYaml = $"# Failed to save: {ex.Message}\n\n{PreviewYaml}";
            }
        }
    }
}

public record TemplateItem(string Key, string Name, string Description);
