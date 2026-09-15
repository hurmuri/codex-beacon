using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace CodexBeacon;

public sealed partial class LogWindow : Window
{
    private readonly DispatcherQueue _dispatcherQueue;
    private int _lineCount;

    public LogWindow()
    {
        InitializeComponent();
        Title = Localization.Get("LogWindowTitle");
        AppWindow.Resize(new SizeInt32(980, 680));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 680;
            presenter.PreferredMinimumHeight = 420;
        }

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        LogPathText.Text = AppLog.FilePath;
        var existing = AppLog.ReadText();
        LogTextBox.Text = existing;
        _lineCount = CountLines(existing);
        UpdateStatus();
        MoveToEnd();
        AppLog.LineAdded += OnLineAdded;
        Closed += (_, _) => AppLog.LineAdded -= OnLineAdded;
    }

    private void OnLineAdded(string line)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            LogTextBox.Text += (LogTextBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + line;
            _lineCount++;
            UpdateStatus();
            MoveToEnd();
        });
    }

    private void MoveToEnd()
    {
        LogTextBox.Select(LogTextBox.Text.Length, 0);
    }

    private void UpdateStatus()
        => LogStatusText.Text = Localization.Format("LogLineCount", _lineCount);

    private void CopyLogs_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(LogTextBox.Text);
        Clipboard.SetContent(package);
        LogStatusText.Text = Localization.Get("LogsCopied");
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppLog.DirectoryPath);
        Process.Start(new ProcessStartInfo("explorer.exe", AppLog.DirectoryPath) { UseShellExecute = true });
    }

    private static int CountLines(string value)
        => string.IsNullOrEmpty(value) ? 0 : value.Count(character => character == '\n') + 1;
}
