using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PipeForge.GUI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly string RecentFilesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PipeForge", "recent.json");

    [ObservableProperty]
    private object _currentView;

    [ObservableProperty]
    private ObservableCollection<RecentFileItem> _recentFiles = new();

    public PipelineEditorViewModel EditorViewModel { get; } = new();
    public LiveRunViewModel LiveRunViewModel { get; } = new();
    public TemplateBrowserViewModel TemplateBrowserViewModel { get; } = new();

    public MainWindowViewModel()
    {
        _currentView = TemplateBrowserViewModel;
        LoadRecentFiles();

        EditorViewModel.OnFileOpened = AddRecentFile;
        LiveRunViewModel.OnFileOpened = AddRecentFile;
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
        if (filePath != null)
            AddRecentFile(filePath);
    }

    [RelayCommand]
    private async Task OpenRecentInEditorAsync(RecentFileItem item)
    {
        try
        {
            var yaml = await File.ReadAllTextAsync(item.FilePath);
            EditorViewModel.LoadContent(yaml, item.FilePath);
            CurrentView = EditorViewModel;
        }
        catch (Exception ex)
        {
            EditorViewModel.LoadContent($"# Failed to open: {ex.Message}", null);
            CurrentView = EditorViewModel;
        }
    }

    [RelayCommand]
    private async Task RunRecentAsync(RecentFileItem item)
    {
        LiveRunViewModel.LoadedFilePath = item.FilePath;
        CurrentView = LiveRunViewModel;
        // Trigger run via reflection-free approach: set path and let user click Run
        // Actually, invoke the run directly
        await Task.CompletedTask;
    }

    public void AddRecentFile(string filePath)
    {
        // Remove existing entry if present
        var existing = RecentFiles.FirstOrDefault(r => r.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            RecentFiles.Remove(existing);

        // Add to top
        RecentFiles.Insert(0, new RecentFileItem(
            Path.GetFileName(filePath),
            filePath,
            Directory.GetParent(filePath)?.Name ?? ""));

        // Keep max 10
        while (RecentFiles.Count > 10)
            RecentFiles.RemoveAt(RecentFiles.Count - 1);

        SaveRecentFiles();
    }

    private void LoadRecentFiles()
    {
        try
        {
            if (File.Exists(RecentFilesPath))
            {
                var json = File.ReadAllText(RecentFilesPath);
                var paths = JsonSerializer.Deserialize<List<string>>(json) ?? [];
                foreach (var p in paths.Where(File.Exists).Take(10))
                {
                    RecentFiles.Add(new RecentFileItem(
                        Path.GetFileName(p), p,
                        Directory.GetParent(p)?.Name ?? ""));
                }
            }
        }
        catch { /* non-critical */ }
    }

    private void SaveRecentFiles()
    {
        try
        {
            var dir = Path.GetDirectoryName(RecentFilesPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(RecentFiles.Select(r => r.FilePath).ToList());
            File.WriteAllText(RecentFilesPath, json);
        }
        catch { /* non-critical */ }
    }
}

public record RecentFileItem(string FileName, string FilePath, string FolderName);
