using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using PipeForge.GUI.ViewModels;
using TextMateSharp.Grammars;

namespace PipeForge.GUI.Views;

public partial class LiveRunView : UserControl
{
    private TextEditor? _sourceEditor;
    private TextMate.Installation? _tmInstallation;

    public LiveRunView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not LiveRunViewModel vm) return;

        // Auto-scroll output to bottom
        vm.OutputLines.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
            {
                var listBox = this.FindControl<ListBox>("OutputListBox");
                if (listBox != null && vm.OutputLines.Count > 0)
                {
                    listBox.ScrollIntoView(vm.OutputLines[^1]);
                }
            }
        };

        // Set up source editor when YAML is loaded
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.YamlSource))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    EnsureSourceEditor();
                    if (_sourceEditor != null)
                        _sourceEditor.Document.Text = vm.YamlSource ?? string.Empty;
                });
            }
            else if (args.PropertyName == nameof(vm.YamlHighlightLine))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ScrollToLine(vm.YamlHighlightLine);
                });
            }
            else if (args.PropertyName == nameof(vm.ShowSource) && vm.ShowSource)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    EnsureSourceEditor();
                    if (_sourceEditor != null && !string.IsNullOrEmpty(vm.YamlSource))
                        _sourceEditor.Document.Text = vm.YamlSource;
                });
            }
        };
    }

    private void EnsureSourceEditor()
    {
        if (_sourceEditor != null) return;

        _sourceEditor = this.FindControl<TextEditor>("SourceEditor");
        if (_sourceEditor == null) return;

        _sourceEditor.IsReadOnly = true;

        var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        _tmInstallation = _sourceEditor.InstallTextMate(registryOptions);
        _tmInstallation.SetGrammar(registryOptions.GetScopeByLanguageId("yaml"));

        string? colorStr;

        if (_tmInstallation.TryGetThemeColor("editor.background", out colorStr) && colorStr != null
            && Color.TryParse(colorStr, out Color bg))
            _sourceEditor.Background = new SolidColorBrush(bg);

        if (_tmInstallation.TryGetThemeColor("editor.foreground", out colorStr) && colorStr != null
            && Color.TryParse(colorStr, out Color fg))
            _sourceEditor.Foreground = new SolidColorBrush(fg);

        if (_tmInstallation.TryGetThemeColor("editorLineNumber.foreground", out colorStr) && colorStr != null
            && Color.TryParse(colorStr, out Color ln))
            _sourceEditor.LineNumbersForeground = new SolidColorBrush(ln);

        if (_tmInstallation.TryGetThemeColor("editor.lineHighlightBackground", out colorStr) && colorStr != null
            && Color.TryParse(colorStr, out Color hl))
        {
            _sourceEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(hl);
            _sourceEditor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(hl));
        }
    }

    private void ScrollToLine(int line)
    {
        if (_sourceEditor == null || line <= 0) return;

        var doc = _sourceEditor.Document;
        if (line > doc.LineCount) return;

        _sourceEditor.TextArea.Caret.Line = line;
        _sourceEditor.TextArea.Caret.Column = 1;
        _sourceEditor.ScrollToLine(line);
    }

    private void StepListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is LiveRunViewModel vm && sender is ListBox listBox)
        {
            if (listBox.SelectedItem is StepProgressItem item)
            {
                vm.ToggleBreakpointCommand.Execute(item);
            }
        }
    }
}
