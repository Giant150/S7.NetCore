using System.Windows;
using System.Windows.Threading;
using S7.NetCore.Tools.Services;
using S7.NetCore.Tools.ViewModels;

namespace S7.NetCore.Tools;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var logStore = new OperationLogStore();
        var errorMapper = new ErrorPresentationMapper();
        var dialog = new DialogService();
        var sink = new ToolLogSink(logStore);
        var uiContext = SynchronizationContext.Current
            ?? new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher);
        var session = new PlcSessionManager(
            logStore,
            errorMapper,
            dialog,
            sink,
            uiContext);

        var connection = new ConnectionViewModel(session, dialog);
        var byteAccess = new ByteAccessViewModel(session);
        var typedAccess = new TypedAccessViewModel(session);
        var log = new LogViewModel(logStore, dialog, sink);
        var main = new MainWindowViewModel(session, dialog, connection, byteAccess, typedAccess, log);

        var window = new MainWindow { DataContext = main };
        MainWindow = window;
        window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"发生未处理异常：{e.Exception.Message}",
            "S7.NetCore 调试工具",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
    }
}