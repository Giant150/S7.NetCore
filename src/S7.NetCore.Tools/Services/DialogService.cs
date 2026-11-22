using System.Windows;
using S7.NetCore.Tools.Models;

namespace S7.NetCore.Tools.Services;

public sealed class DialogService
{
    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        return Invoke(() => MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes);
    }

    public Task ShowErrorAsync(ErrorPresentation error, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        string message = $"类别：{Texts.Describe(error.Category)}\n"
            + $"错误码：{error.CodeText}\n"
            + $"阶段：{error.StageText}\n"
            + $"PLC 返回码：{error.PlcReturnText}\n\n"
            + $"说明：{error.Message}\n\n"
            + $"建议：{error.SuggestedAction}";

        return Invoke(() =>
        {
            MessageBox.Show(message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return true;
        });
    }

    private static Task<T> Invoke<T>(Func<T> action)
    {
        Application? application = Application.Current;
        if (application is null || application.Dispatcher.CheckAccess())
        {
            return Task.FromResult(action());
        }

        return application.Dispatcher.InvokeAsync(action).Task;
    }
}