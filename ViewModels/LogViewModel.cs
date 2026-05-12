using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Excalibur5.Services;

namespace Excalibur5.ViewModels;

public partial class LogViewModel : ObservableObject, IDisposable
{
    private const int MaxLines = 5000;
    private readonly Queue<string> _lines = new();
    private bool _dirty;
    private DispatcherTimer? _rebuildTimer;

    [ObservableProperty] private bool _isLogVisible;
    [ObservableProperty] private string _logText = string.Empty;

    public LogViewModel()
    {
        AppLogger.LogEntryAdded += OnLogEntry;
        _rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _rebuildTimer.Tick += (_, _) =>
        {
            _rebuildTimer.Stop();
            if (_dirty && IsLogVisible)
                RebuildText();
        };
    }

    private void OnLogEntry(string entry)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _lines.Enqueue(entry);

            while (_lines.Count > MaxLines)
                _lines.Dequeue();

            _dirty = true;
            ScheduleRebuild();
        });
    }

    partial void OnIsLogVisibleChanged(bool value)
    {
        if (value && _dirty)
            RebuildText();
    }

    private void ScheduleRebuild()
    {
        if (!IsLogVisible || _rebuildTimer == null) return;
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    private void RebuildText()
    {
        var sb = new StringBuilder(_lines.Count * 80);
        foreach (var line in _lines)
            sb.AppendLine(line);
        LogText = sb.ToString();
        _dirty = false;
    }

    [RelayCommand]
    private void ToggleLog()
    {
        IsLogVisible = !IsLogVisible;
    }

    [RelayCommand]
    private void CopyAll()
    {
        if (_dirty)
            RebuildText();
        if (!string.IsNullOrEmpty(LogText))
            Clipboard.SetText(LogText);
    }

    public void Dispose()
    {
        AppLogger.LogEntryAdded -= OnLogEntry;
        _rebuildTimer?.Stop();
        _rebuildTimer = null;
    }
}
