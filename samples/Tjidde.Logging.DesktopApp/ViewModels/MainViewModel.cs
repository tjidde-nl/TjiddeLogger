using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tjidde.Logging.Context;
using Tjidde.Logging.Extensions;
using Tjidde.Logging.Sinks;
using Microsoft.Extensions.Logging;

namespace Tjidde.Logging.DesktopApp.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly InMemoryLogSink _sink;

    private string _customer = string.Empty;
    private string _message = string.Empty;
    private const string MetricsLevel = "Metrics";

    private string _selectedLevel = nameof(LogLevel.Information);
    private bool _includeException;
    private bool _includeInner;
    private string _exceptionMessage = "Something went wrong";

    public MainViewModel(ILogger<MainViewModel> logger, InMemoryLogSink sink)
    {
        _logger = logger;
        _sink = sink;
        _sink.EntryAdded += (_, _) => OnSinkChanged();
        _sink.Cleared += (_, _) => OnSinkChanged();
        RefreshEntries();
    }

    public ObservableCollection<TjiddeLogEntry> Entries { get; } = new();

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

    public string SelectedLevel
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

    // "Metrics" is not a log level: it is logged with LogMetrics (Information + MetricsEventId).
    public string[] LogLevels { get; } =
    [
        nameof(LogLevel.Trace),
        nameof(LogLevel.Debug),
        nameof(LogLevel.Information),
        nameof(LogLevel.Warning),
        nameof(LogLevel.Error),
        nameof(LogLevel.Critical),
        MetricsLevel
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
        if (_selectedLevel == MetricsLevel)
            _logger.LogMetrics(ex, "{Message}", _message.Trim());
        else
            _logger.Log(Enum.Parse<LogLevel>(_selectedLevel), ex, "{Message}", _message.Trim());
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
        var entries = _sink.GetSnapshot();
        Entries.Clear();
        foreach (var e in entries.Reverse())
            Entries.Add(e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
