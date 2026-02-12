using Avalonia.Controls;
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
