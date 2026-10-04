using Tjidde.Logging.Context;
using Tjidde.Logging.TuiApp.Services;
using Microsoft.Extensions.Logging;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Tjidde.Logging.TuiApp.Views;

public sealed class MainView
{
    private readonly IApplication _app;
    private readonly ILogService _logService;

    private ListView _logList = null!;

    public MainView(IApplication app, ILogService logService)
    {
        _app = app;
        _logService = logService;
    }

    public void Build(Window top)
    {
        var leftPane = CreateLeftPane();
        var rightPane = CreateRightPane(leftPane);
        top.Add(leftPane, rightPane);

        _logService.LogsChanged += () =>
            _app.Invoke(RefreshLogs);
    }

    private FrameView CreateLeftPane()
    {
        var pane = new FrameView
        {
            Title = "Send Log Entry",
            X = 0, Y = 0,
            Width = Dim.Percent(40), Height = Dim.Fill()
        };

        var lblCustomer = new Label { Text = "Customer:", X = 1, Y = 1 };
        var txtCustomer = new TextField { X = 1, Y = 2, Width = Dim.Fill(1) };

        var lblLevel = new Label { Text = "Level:", X = 1, Y = 4 };
        var txtLevel = new TextField { Text = "Information", X = 1, Y = 5, Width = Dim.Fill(1) };

        var lblMessage = new Label { Text = "Message:", X = 1, Y = 7 };
        var txtMessage = new TextField
        {
            X = 1, Y = 8,
            Width = Dim.Fill(1)
        };

        var chkException = new CheckBox { Text = "Include Exception", X = 1, Y = 10 };
        var lblExMsg = new Label { Text = "Exception Msg:", X = 1, Y = 11, Visible = false };
        var txtExMsg = new TextField { Text = "Something went wrong", X = 1, Y = 12, Width = Dim.Fill(1), Visible = false };
        var chkInner = new CheckBox { Text = "Include Inner Exception", X = 1, Y = 13, Visible = false };

        chkException.ValueChanged += (_, _) =>
        {
            var visible = chkException.Value == CheckState.Checked;
            lblExMsg.Visible = visible;
            txtExMsg.Visible = visible;
            chkInner.Visible = visible;
        };

        var btnSend = new Button { Text = "Send Log", X = 1, Y = 15 };
        var btnClear = new Button { Text = "Clear Log", X = Pos.Right(btnSend) + 2, Y = 15 };

        btnSend.Accepted += (_, _) =>
        {
            var msg = txtMessage.Text.ToString();
            if (string.IsNullOrWhiteSpace(msg)) return;

            var customer = txtCustomer.Text.ToString(); 
            CustomerContext.Set(customer);
            var requestedLevel = txtLevel.Text.ToString();
            var level = ParseLogLevel(requestedLevel);
            var metrics = string.Equals(requestedLevel, "Metrics", StringComparison.OrdinalIgnoreCase);
            var ex = BuildException(
                chkException.Value == CheckState.Checked,
                txtExMsg.Text.ToString(),
                chkInner.Value == CheckState.Checked);

            _logService.SendLog(
                msg.Trim(),
                level,
                string.IsNullOrWhiteSpace(customer) ? null : customer.Trim(),
                ex,
                metrics);

            RefreshLogs();
        };

        btnClear.Accepted += (_, _) =>
        {
            _logService.ClearLogs();
            RefreshLogs();
        };

        pane.Add(lblCustomer, txtCustomer, lblLevel, txtLevel, lblMessage, txtMessage,
            chkException, lblExMsg, txtExMsg, chkInner, btnSend, btnClear);

        return pane;
    }

    private FrameView CreateRightPane(View leftPane)
    {
        var pane = new FrameView
        {
            Title = "Log Output",
            X = Pos.Right(leftPane), Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill()
        };

        _logList = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill(),
            CanFocus = true
        };

        pane.Add(_logList);
        return pane;
    }

    private void RefreshLogs()
    {
        var entries = _logService.GetFormattedEntries();
        _logList.SetSource(new System.Collections.ObjectModel.ObservableCollection<string>(entries));
    }

    private static LogLevel ParseLogLevel(string? selectedLevel) => selectedLevel?.Trim().ToLowerInvariant() switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "warning" => LogLevel.Warning,
        "error" => LogLevel.Error,
        "critical" => LogLevel.Critical,
        _ => LogLevel.Information
    };

    private static Exception? BuildException(bool include, string? message, bool includeInner)
    {
        if (!include) return null;

        var inner = includeInner
            ? new InvalidOperationException("Inner: root cause of the problem")
            : null;

        return new ApplicationException(
            string.IsNullOrWhiteSpace(message) ? "Sample exception" : message,
            inner);
    }
}
