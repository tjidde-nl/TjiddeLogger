using Avalonia.Controls;
using Avalonia.Interactivity;
using Tjidde.Logging.DesktopApp.ViewModels;

namespace Tjidde.Logging.DesktopApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnSendLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SendLog();
    }

    private void OnClearLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.ClearLog();
    }
}
