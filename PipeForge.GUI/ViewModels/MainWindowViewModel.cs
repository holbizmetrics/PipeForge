using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PipeForge.GUI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object _currentView;

    public PipelineEditorViewModel EditorViewModel { get; } = new();
    public LiveRunViewModel LiveRunViewModel { get; } = new();
    public TemplateBrowserViewModel TemplateBrowserViewModel { get; } = new();

    public MainWindowViewModel()
    {
        _currentView = TemplateBrowserViewModel;
    }

    [RelayCommand]
    private void NavigateToEditor() => CurrentView = EditorViewModel;

    [RelayCommand]
    private void NavigateToRun() => CurrentView = LiveRunViewModel;

    [RelayCommand]
    private void NavigateToTemplates() => CurrentView = TemplateBrowserViewModel;

    /// <summary>
    /// Navigate to editor with pre-loaded YAML content (used by Template Browser → Create).
    /// </summary>
    public void NavigateToEditorWithContent(string yamlContent, string? filePath = null)
    {
        EditorViewModel.LoadContent(yamlContent, filePath);
        CurrentView = EditorViewModel;
    }
}
