using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using PipeForge.GUI.ViewModels;
using TextMateSharp.Grammars;

namespace PipeForge.GUI.Views;

public partial class PipelineEditorView : UserControl
{
    private TextEditor? _editor;
    private bool _updatingFromEditor;
    private bool _updatingFromViewModel;

    public PipelineEditorView()
    {
        InitializeComponent();

        _editor = this.FindControl<TextEditor>("YamlEditor");

        if (_editor != null)
        {
            var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
            var installation = _editor.InstallTextMate(registryOptions);
            installation.SetGrammar(registryOptions.GetScopeByLanguageId("yaml"));

            // Read theme colors and apply to editor
            string? colorStr;

            if (installation.TryGetThemeColor("editor.background", out colorStr) && colorStr != null
                && Color.TryParse(colorStr, out Color bg))
                _editor.Background = new SolidColorBrush(bg);

            if (installation.TryGetThemeColor("editor.foreground", out colorStr) && colorStr != null
                && Color.TryParse(colorStr, out Color fg))
                _editor.Foreground = new SolidColorBrush(fg);

            if (installation.TryGetThemeColor("editorLineNumber.foreground", out colorStr) && colorStr != null
                && Color.TryParse(colorStr, out Color ln))
                _editor.LineNumbersForeground = new SolidColorBrush(ln);

            if (installation.TryGetThemeColor("editor.selectionBackground", out colorStr) && colorStr != null
                && Color.TryParse(colorStr, out Color sel))
                _editor.TextArea.SelectionBrush = new SolidColorBrush(sel);

            if (installation.TryGetThemeColor("editor.lineHighlightBackground", out colorStr) && colorStr != null
                && Color.TryParse(colorStr, out Color hl))
            {
                _editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(hl);
                _editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(hl));
            }
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_editor != null && DataContext is PipelineEditorViewModel vm)
        {
            // Initial content
            if (!string.IsNullOrEmpty(vm.YamlContent))
                _editor.Document.Text = vm.YamlContent;

            // ViewModel → Editor
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.YamlContent) && !_updatingFromEditor)
                {
                    _updatingFromViewModel = true;
                    Dispatcher.UIThread.Post(() =>
                    {
                        _editor.Document.Text = vm.YamlContent ?? string.Empty;
                        _updatingFromViewModel = false;
                    });
                }
            };

            // Editor → ViewModel
            _editor.Document.TextChanged += (_, _) =>
            {
                if (!_updatingFromViewModel)
                {
                    _updatingFromEditor = true;
                    vm.YamlContent = _editor.Document.Text;
                    _updatingFromEditor = false;
                }
            };
        }
    }
}
