using Avalonia.Controls;
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
