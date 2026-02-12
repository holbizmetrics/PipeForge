using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using PipeForge.GUI.ViewModels;
using TextMateSharp.Grammars;

namespace PipeForge.GUI.Views;

public partial class PipelineEditorView : UserControl
{
    private TextEditor? _editor;

    public PipelineEditorView()
    {
        InitializeComponent();

        _editor = this.FindControl<TextEditor>("YamlEditor");

        if (_editor != null)
            InstallTextMate(_editor);
    }

    private static void InstallTextMate(TextEditor editor)
    {
        var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        var installation = editor.InstallTextMate(registryOptions);
        installation.SetGrammar(registryOptions.GetScopeByLanguageId("yaml"));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_editor != null && DataContext is PipelineEditorViewModel vm)
        {
            // Initial content load
            _editor.Text = vm.YamlContent;

            // ViewModel → Editor
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.YamlContent) && _editor.Text != vm.YamlContent)
                    _editor.Text = vm.YamlContent;
            };

            // Editor → ViewModel
            _editor.TextChanged += (_, _) =>
            {
                if (_editor.Text != vm.YamlContent)
                    vm.YamlContent = _editor.Text ?? string.Empty;
            };
        }
    }
}
