using System.Windows.Forms;

namespace FreakyNikki.Tray;

/// <summary>
/// System-tray presence. Double-click opens the window; the context menu offers
/// Open, Start/Stop and Quit. Uses WinForms' NotifyIcon (no extra dependency).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggleItem;
    private bool _disposed;

    public TrayIcon()
    {
        _toggleItem = new ToolStripMenuItem("Start", null, (_, _) => ToggleRequested?.Invoke());

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open", null, (_, _) => OpenRequested?.Invoke()));
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => QuitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Freaky Nikki",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;
    public event Action? ToggleRequested;
    public event Action? QuitRequested;

    public void SetRunning(bool running) => _toggleItem.Text = running ? "Stop" : "Start";

    private static System.Drawing.Icon LoadIcon()
    {
        var uri = new Uri("pack://application:,,,/Assets/app.ico");
        System.Windows.Resources.StreamResourceInfo info = System.Windows.Application.GetResourceStream(uri);
        return new System.Drawing.Icon(info.Stream);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
