using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tjidde.Logging.Context;
using Tjidde.Logging.DesktopApp.Logging;
using Tjidde.Logging.Extensions;
using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.DesktopApp.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly InMemoryLogSink _sink;

    private string _customer = string.Empty;
    private string _message = string.Empty;
    private LogLevel _selectedLevel = LogLevel.Information;
    private bool _includeException;
    private bool _includeInner;
    private string _exceptionMessage = "Something went wrong";

    public MainViewModel(ILogger<MainViewModel> logger, InMemoryLogSink sink)
    {
        _logger = logger;
        _sink = sink;
        _sink.OnChanged += OnSinkChanged;
        RefreshEntries();
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public string Customer
    {
        get => _customer;
        set { _customer = value; OnPropertyChanged(); }
    }

    public string Message
    {
        get => _message;
        set { _message = value; OnPropertyChanged(); }
    }

    public LogLevel SelectedLevel
    {
        get => _selectedLevel;
        set { _selectedLevel = value; OnPropertyChanged(); }
    }

    public bool IncludeException
    {
        get => _includeException;
        set { _includeException = value; OnPropertyChanged(); }
    }

    public bool IncludeInner
    {
        get => _includeInner;
        set { _includeInner = value; OnPropertyChanged(); }
    }

    public string ExceptionMessage
    {
        get => _exceptionMessage;
        set { _exceptionMessage = value; OnPropertyChanged(); }
    }

    public LogLevel[] LogLevels { get; } =
    [
        LogLevel.Trace,
        LogLevel.Debug,
        LogLevel.Information,
        LogLevel.Warning,
        LogLevel.Error,
        LogLevel.Critical,
        MetricsLoggerExtensions.Metrics
    ];

    public void SendLog()
    {
        if (string.IsNullOrWhiteSpace(_message)) return;

        CustomerContext.Set(string.IsNullOrWhiteSpace(_customer) ? null : _customer.Trim());

        Exception? ex = null;
        if (_includeException)
        {
            var inner = _includeInner
                ? new InvalidOperationException("Inner: root cause of the problem")
                : null;
            ex = new ApplicationException(
                string.IsNullOrWhiteSpace(_exceptionMessage) ? "Sample exception" : _exceptionMessage,
                inner);
        }
        _logger.Log(_selectedLevel, ex, "{Message}", _message.Trim());
        CustomerContext.Clear();
    }

    public void ClearLog()
    {
        _sink.Clear();
    }

    private void OnSinkChanged()
    {
        Dispatcher.UIThread.Post(RefreshEntries);
    }

    private void RefreshEntries()
    {
        var entries = _sink.GetEntries();
        Entries.Clear();
        foreach (var e in entries.Reverse())
            Entries.Add(e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
