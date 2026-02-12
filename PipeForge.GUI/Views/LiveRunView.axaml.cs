using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using PipeForge.GUI.ViewModels;

namespace PipeForge.GUI.Views;

public partial class LiveRunView : UserControl
{
    public LiveRunView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Auto-scroll output to bottom
        if (DataContext is LiveRunViewModel vm)
        {
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
