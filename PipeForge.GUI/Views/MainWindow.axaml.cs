using Avalonia.Controls;
using Avalonia.Input;
using PipeForge.GUI.ViewModels;

namespace PipeForge.GUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void RecentFilesList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is ListBox listBox)
        {
            if (listBox.SelectedItem is RecentFileItem item)
            {
                vm.OpenRecentInEditorCommand.Execute(item);
            }
        }
    }
}
