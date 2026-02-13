using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using PipeForge.GUI.ViewModels;
using TextMateSharp.Grammars;

namespace PipeForge.GUI.Views;

public partial class LiveRunView : UserControl
{
    private TextEditor? _sourceEditor;
    private TextMate.Installation? _tmInstallation;
    private CurrentStepHighlighter? _highlighter;

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
                    HighlightLine(vm.YamlHighlightLine, vm.AutoScrollSource);
                });
            }
            else if (args.PropertyName == nameof(vm.ShowSource) && vm.ShowSource)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    EnsureSourceEditor();
                    if (_sourceEditor != null && !string.IsNullOrEmpty(vm.YamlSource))
                    {
                        _sourceEditor.Document.Text = vm.YamlSource;
                        if (vm.YamlHighlightLine > 0)
                            HighlightLine(vm.YamlHighlightLine, vm.AutoScrollSource);
                    }
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

        // Add custom highlight renderer for current step
        _highlighter = new CurrentStepHighlighter(_sourceEditor.Document);
        _sourceEditor.TextArea.TextView.BackgroundRenderers.Add(_highlighter);
    }

    private void HighlightLine(int line, bool autoScroll)
    {
        if (_sourceEditor == null || line <= 0) return;

        var doc = _sourceEditor.Document;
        if (line > doc.LineCount) return;

        // Always update the highlight band
        if (_highlighter != null)
        {
            _highlighter.HighlightedLine = line;
            _sourceEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }

        if (autoScroll)
        {
            _sourceEditor.TextArea.Caret.Line = line;
            _sourceEditor.TextArea.Caret.Column = 1;

            // Defer scroll to after layout pass so the editor has rendered
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _sourceEditor.ScrollToLine(line);
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
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

/// <summary>
/// Renders a yellow highlight band on the current step's YAML line.
/// </summary>
internal class CurrentStepHighlighter : IBackgroundRenderer
{
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#33F9E2AF")); // semi-transparent yellow
    private static readonly IPen HighlightBorder = new Pen(new SolidColorBrush(Color.Parse("#F9E2AF")), 1);

    private readonly TextDocument _document;

    public int HighlightedLine { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public CurrentStepHighlighter(TextDocument document)
    {
        _document = document;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (HighlightedLine <= 0 || HighlightedLine > _document.LineCount) return;

        textView.EnsureVisualLines();
        var line = _document.GetLineByNumber(HighlightedLine);

        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
        {
            // Draw full-width highlight
            var fullRect = new Rect(0, rect.Top, textView.Bounds.Width, rect.Height);
            drawingContext.DrawRectangle(HighlightBrush, HighlightBorder, fullRect);
        }
    }
}
