using Avalonia.Controls;
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
            InstallTextMate(_previewEditor);
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

        if (_previewEditor != null && DataContext is TemplateBrowserViewModel vm)
        {
            // Sync preview content
            _previewEditor.Text = vm.PreviewYaml;

            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.PreviewYaml))
                    _previewEditor.Text = vm.PreviewYaml;
            };
        }
    }
}
