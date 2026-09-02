using System.Drawing;
using System.Windows.Forms;

namespace StealthRAT;

/// <summary>
/// Provides a visual monitoring interface that displays real-time log messages.
/// Uses a RichTextBox with a console-style appearance for readability.
/// This form runs on a dedicated STA thread to avoid blocking the main application.
/// </summary>
public sealed class RemoteUIManager : Form
{
    private readonly RichTextBox _logTextBox;
    private const int MaxLogLines = 1000;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteUIManager"/> class
    /// with a pre-configured console-style appearance.
    /// </summary>
    public RemoteUIManager()
    {
        InitializeFormProperties();
        _logTextBox = CreateLogTextBox();
        Controls.Add(_logTextBox);
    }

    /// <summary>
    /// Appends a timestamped log message to the display.
    /// Thread-safe: can be called from any thread.
    /// Automatically scrolls to show the latest message.
    /// </summary>
    /// <param name="message">The message to display in the log.</param>
    public void Log(string message)
    {
        if (IsDisposed) return;

        if (_logTextBox.InvokeRequired)
        {
            _logTextBox.BeginInvoke(new Action(() => Log(message)));
            return;
        }

        // Prevent unlimited memory growth by trimming old entries
        if (_logTextBox.Lines.Length > MaxLogLines)
        {
            TrimOldEntries();
        }

        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _logTextBox.ScrollToCaret();
    }

    /// <summary>
    /// Configures the form's visual properties and behavior.
    /// </summary>
    private void InitializeFormProperties()
    {
        Text = "System Monitor";
        Size = new Size(600, 450);
        MinimumSize = new Size(400, 300);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        TopMost = false;
        ShowInTaskbar = true;
    }

    /// <summary>
    /// Creates and configures the log display text box with console styling.
    /// </summary>
    /// <returns>A configured RichTextBox control.</returns>
    private static RichTextBox CreateLogTextBox()
    {
        return new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(0, 255, 128),
            Font = new Font("Consolas", 10f, FontStyle.Regular),
            BorderStyle = BorderStyle.None,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
    }

    /// <summary>
    /// Removes the oldest log entries to prevent excessive memory usage.
    /// Keeps the most recent half of the entries.
    /// </summary>
    private void TrimOldEntries()
    {
        string[] lines = _logTextBox.Lines;
        int keepFrom = lines.Length / 2;
        _logTextBox.Text = string.Join(Environment.NewLine, lines.Skip(keepFrom));
        _logTextBox.SelectionStart = _logTextBox.TextLength;
    }
}
