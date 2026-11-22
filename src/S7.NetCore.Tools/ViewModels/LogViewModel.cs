using System.Collections.ObjectModel;
using System.Windows.Threading;
using S7.NetCore.Tools.Infrastructure;
using S7.NetCore.Tools.Models;
using S7.NetCore.Tools.Services;

namespace S7.NetCore.Tools.ViewModels;

public sealed class LogViewModel : ObservableObject
{
    private readonly OperationLogStore _store;
    private readonly DialogService _dialog;
    private readonly ToolLogSink _sink;
    private readonly DispatcherTimer _timer;
    private long _lastVersion = -1;
    private string _selectedLevel = "全部";
    private string _selectedCategory = "全部";
    private string _keyword = string.Empty;
    private string _countText = "当前 0/10000（已裁剪 0）";
    private bool _isShowLibraryDiagnostics;
    private OperationLogEntry? _selectedEntry;

    public LogViewModel(OperationLogStore store, DialogService dialog, ToolLogSink sink)
    {
        _store = store;
        _dialog = dialog;
        _sink = sink;
        CopySelectedCommand = new RelayCommand(CopySelected, () => SelectedEntry is not null);
        ClearCommand = new AsyncRelayCommand(ClearAsync);
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _timer.Tick += (_, _) => RefreshIfChanged();
        _timer.Start();
        Refresh(force: true);
    }

    public ObservableCollection<OperationLogEntry> Entries { get; } = new();

    public IReadOnlyList<string> Levels { get; } = ["全部", "信息", "警告", "错误"];

    public IReadOnlyList<string> Categories { get; } =
        ["全部", "连接", "断开", "字节读取", "字节写入", "类型化读取", "类型化写入", "自动重连", "诊断"];

    public RelayCommand CopySelectedCommand { get; }

    public AsyncRelayCommand ClearCommand { get; }

    public string SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (SetProperty(ref _selectedLevel, value))
            {
                Refresh(force: true);
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                Refresh(force: true);
            }
        }
    }

    public string Keyword
    {
        get => _keyword;
        set
        {
            if (SetProperty(ref _keyword, value))
            {
                Refresh(force: true);
            }
        }
    }

    public string CountText { get => _countText; private set => SetProperty(ref _countText, value); }

    public bool IsShowLibraryDiagnostics
    {
        get => _isShowLibraryDiagnostics;
        set
        {
            if (SetProperty(ref _isShowLibraryDiagnostics, value))
            {
                _sink.IsDiagnosticsEnabled = value;
                Refresh(force: true);
            }
        }
    }

    public OperationLogEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                CopySelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void RefreshIfChanged()
    {
        if (_store.Version != _lastVersion)
        {
            Refresh(force: false);
        }
    }

    private void Refresh(bool force)
    {
        long version = _store.Version;
        if (!force && version == _lastVersion)
        {
            return;
        }

        _lastVersion = version;
        IReadOnlyList<OperationLogEntry> source = _store.Snapshot();
        var filtered = source.Where(Matches).ToArray();

        Entries.Clear();
        foreach (OperationLogEntry entry in filtered)
        {
            Entries.Add(entry);
        }

        CountText = $"当前 {Entries.Count}/{OperationLogStore.Capacity}（已裁剪 {_store.TrimmedCount}）";
    }

    private bool Matches(OperationLogEntry entry)
    {
        if (SelectedLevel != "全部" && Texts.Describe(entry.Level) != SelectedLevel)
        {
            return false;
        }

        if (SelectedCategory != "全部" && Texts.Describe(entry.Category) != SelectedCategory)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(Keyword)
            || entry.Summary.Contains(Keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void CopySelected()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        OperationLogEntry entry = SelectedEntry;
        string text = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {Texts.Describe(entry.Level)} | "
            + $"{Texts.Describe(entry.Category)} | {Texts.Describe(entry.Stage)} | "
            + $"{(entry.Succeeded ? "成功" : "失败")} | {entry.Elapsed.TotalMilliseconds:0} ms | "
            + $"{(entry.ErrorCode is null ? "-" : entry.ErrorCode.ToString())} | "
            + $"{(entry.PlcReturnCode is null ? "-" : $"0x{entry.PlcReturnCode:X2}")} | "
            + $"{entry.RecoveryCount?.ToString() ?? "-"} | {entry.ReplayAttempted} | {entry.Summary}";
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception exception)
        {
            _ = _dialog.ShowErrorAsync(new ErrorPresentation(
                S7ErrorCategory.Protocol,
                null,
                null,
                null,
                $"复制到剪贴板失败：{exception.Message}",
                "请检查剪贴板是否被其他程序占用。"));
        }
    }

    private async Task ClearAsync()
    {
        bool confirmed = await _dialog.ConfirmAsync("清空日志", "确定清空当前内存中的全部日志吗？");
        if (!confirmed)
        {
            return;
        }

        _store.Clear();
        Refresh(force: true);
    }
}