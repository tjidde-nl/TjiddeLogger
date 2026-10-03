using Tjidde.Logging.Context;
using Tjidde.Logging.TuiApp.Services;
using Microsoft.Extensions.Logging;
using Terminal.Gui;

namespace Tjidde.Logging.TuiApp.Views;

public sealed class MainView
{
    private readonly ILogService _logService;
    private readonly ColorScheme _colorScheme;

    private ListView _logList = null!;

    public MainView(ILogService logService, ColorScheme colorScheme)
    {
        _logService = logService;
        _colorScheme = colorScheme;
    }

    public void Build(Toplevel top)
    {
        var win = new Window("Tjidde Logger TUI")
        {
            X = 0, Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill(),
            ColorScheme = _colorScheme
        };
        top.Add(win);

        var leftPane = CreateLeftPane();
        var rightPane = CreateRightPane(leftPane);
        win.Add(leftPane, rightPane);

        RegisterHelpKey(top);

        _logService.LogsChanged += () =>
            Application.MainLoop.Invoke(RefreshLogs);
    }

    private FrameView CreateLeftPane()
    {
        var pane = new FrameView("Send Log Entry")
        {
            X = 0, Y = 0,
            Width = Dim.Percent(40), Height = Dim.Fill(),
            ColorScheme = _colorScheme
        };

        // Customer
        var lblCustomer = new Label("_Customer:") { X = 1, Y = 1 };
        var txtCustomer = new TextField("") { X = 1, Y = 2, Width = Dim.Fill(1), ColorScheme = _colorScheme };
        lblCustomer.Clicked += () => txtCustomer.SetFocus();

        // Log Level
        var lblLevel = new Label("_Log Level:") { X = 1, Y = 4 };
        var levels = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "Metrics" };
        var comboLevel = new ComboBox
        {
            X = 1, Y = 5,
            Width = Dim.Fill(1), Height = 5,
            ColorScheme = _colorScheme
        };
        comboLevel.SetSource(levels);
        comboLevel.Text = levels[2];
        lblLevel.Clicked += () => comboLevel.SetFocus();

        // Message
        var lblMessage = new Label("_Message:") { X = 1, Y = 7 };
        var txtMessage = new TextView
        {
            X = 1, Y = 8,
            Width = Dim.Fill(1), Height = 3,
            ColorScheme = _colorScheme
        };
        lblMessage.Clicked += () => txtMessage.SetFocus();

        // Exception
        var chkException = new CheckBox("Include _Exception") { X = 1, Y = 12, ColorScheme = _colorScheme };
        var lblExMsg = new Label("E_xception Msg:") { X = 1, Y = 13, Visible = false };
        var txtExMsg = new TextField("Something went wrong") { X = 1, Y = 14, Width = Dim.Fill(1), Visible = false, ColorScheme = _colorScheme };
        lblExMsg.Clicked += () => { if (txtExMsg.Visible) txtExMsg.SetFocus(); };
        var chkInner = new CheckBox("Include _Inner Exception") { X = 1, Y = 15, Visible = false, ColorScheme = _colorScheme };

        chkException.Toggled += _ =>
        {
            lblExMsg.Visible = chkException.Checked;
            txtExMsg.Visible = chkException.Checked;
            chkInner.Visible = chkException.Checked;
        };

        // Buttons
        var btnSend = new Button("_Send Log") { X = 1, Y = 17, ColorScheme = _colorScheme };
        var btnClear = new Button("C_lear Log") { X = Pos.Right(btnSend) + 2, Y = 17, ColorScheme = _colorScheme };

        btnSend.Clicked += () =>
        {
           
            var msg = txtMessage.Text.ToString();
            if (string.IsNullOrWhiteSpace(msg)) return;

            var customer = txtCustomer.Text.ToString(); 
            CustomerContext.Set(customer);
            var level = ParseLogLevel(comboLevel.SelectedItem);
            var metrics = comboLevel.SelectedItem == MetricsIndex;
            var ex = BuildException(chkException.Checked, txtExMsg.Text.ToString(), chkInner.Checked);

            _logService.SendLog(
                msg.Trim(),
                level,
                string.IsNullOrWhiteSpace(customer) ? null : customer.Trim(),
                ex,
                metrics);

            RefreshLogs();
        };

        btnClear.Clicked += () =>
        {
            _logService.ClearLogs();
            RefreshLogs();
        };

        pane.Add(lblCustomer, txtCustomer, lblLevel, comboLevel, lblMessage, txtMessage,
            chkException, lblExMsg, txtExMsg, chkInner, btnSend, btnClear);

        return pane;
    }

    private FrameView CreateRightPane(View leftPane)
    {
        var pane = new FrameView("Log Output")
        {
            X = Pos.Right(leftPane), Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill(),
            ColorScheme = _colorScheme
        };

        _logList = new ListView
        {
            X = 0, Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill(),
            CanFocus = true,
            ColorScheme = _colorScheme,
            HotKey = Key.AltMask | (Key)'o'
        };

        pane.Add(_logList);
        return pane;
    }

    private void RefreshLogs()
    {
        var entries = _logService.GetFormattedEntries();
        _logList.SetSource(entries.ToList());
    }

    private static void RegisterHelpKey(Toplevel top)
    {
        top.KeyPress += e =>
        {
            if (e.KeyEvent.Key == Key.F1 ||
                e.KeyEvent.Key == (Key.AltMask | (Key)'h') ||
                e.KeyEvent.Key == (Key.AltMask | (Key)'H'))
            {
                MessageBox.Query("Hotkeys Help",
                    "Alt+C: Customer\n" +
                    "Alt+L: Log Level\n" +
                    "Alt+M: Message\n" +
                    "Alt+E: Include Exception\n" +
                    "Alt+X: Exception Msg (if visible)\n" +
                    "Alt+I: Include Inner Exception (if visible)\n" +
                    "Alt+S: Send Log\n" +
                    "Alt+A: Clear Log\n" +
                    "Alt+O: Log Output\n" +
                    "F1 or Alt+H: Show this Help",
                    "Close");
                e.Handled = true;
            }
        };
    }

    // "Metrics" is not a log level: it is logged with LogMetrics (Information + MetricsEventId).
    private const int MetricsIndex = 6;

    private static LogLevel ParseLogLevel(int selectedIndex) => selectedIndex switch
    {
        0 => LogLevel.Trace,
        1 => LogLevel.Debug,
        2 => LogLevel.Information,
        3 => LogLevel.Warning,
        4 => LogLevel.Error,
        5 => LogLevel.Critical,
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
