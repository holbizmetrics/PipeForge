using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using PipeForge.GUI.ViewModels;
using TextMateSharp.Grammars;

namespace PipeForge.GUI.Views;

public partial class TemplateBrowserView : UserControl
{
    private TextEditor? _previewEditor;

    public TemplateBrowserView()
    {
        InitializeComponent();
        _previewEditor = this.FindControl<TextEditor>("PreviewEditor");

        if (_previewEditor != null)
        {
            var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
            var installation = _previewEditor.InstallTextMate(registryOptions);
            installation.SetGrammar(registryOptions.GetScopeByLanguageId("yaml"));

            // Read theme colors and apply to editor
            string? colorStr;

            if (installation.TryGetThemeColor("editor.background", out colorStr) && colorStr != null
                && Color.TryParse(colorStr, out Color bg))
                _previewEditor.Background = new SolidColorBrush(bg);

            if (installation.TryGetThemeColor("editor.foreground", out colorStr) && colorStr != null
                && Color.TryParse(colorStr, out Color fg))
                _previewEditor.Foreground = new SolidColorBrush(fg);

            if (installation.TryGetThemeColor("editorLineNumber.foreground", out colorStr) && colorStr != null
                && Color.TryParse(colorStr, out Color ln))
                _previewEditor.LineNumbersForeground = new SolidColorBrush(ln);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_previewEditor != null && DataContext is TemplateBrowserViewModel vm)
        {
            // Initial sync
            if (!string.IsNullOrEmpty(vm.PreviewYaml))
                _previewEditor.Document.Text = vm.PreviewYaml;

            // ViewModel → Editor
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.PreviewYaml))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _previewEditor.Document.Text = vm.PreviewYaml ?? string.Empty;
                    });
                }
            };
        }
    }
}
