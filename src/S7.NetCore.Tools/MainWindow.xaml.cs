using System.ComponentModel;
using System.Windows;
using S7.NetCore.Tools.ViewModels;

namespace S7.NetCore.Tools;

public partial class MainWindow : Window
{
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        e.Cancel = true;

        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ShutdownAsync();
        }

        Application.Current.Shutdown();
    }
}