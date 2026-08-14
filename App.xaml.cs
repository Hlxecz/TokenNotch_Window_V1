using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace TokenNotchWin;

public partial class App : Application
{
    private NotifyIcon? _tray;
    private MainWindow? _window;
    private ToolStripMenuItem? _showItem;
    private ToolStripMenuItem? _lockItem;
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--gen-icon"))
        {
            IconGen.Run(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources"));
            Shutdown();
            return;
        }

        // Double-clicking the exe again should not stack up a second crab.
        // Held for the process lifetime; the OS releases it if we crash.
        _singleInstance = new Mutex(true, @"Local\TokenNotchWin.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            // The running copy may have been hidden to the tray — nudge it
            // back into view so the click still feels like it did something.
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _window = new MainWindow();
        _window.Show();
        _window.MenuStateChanged += SyncMenu;

        // No taskbar button (ShowInTaskbar=false), so the tray icon is the only
        // way back once the widget is hidden.
        _showItem = new ToolStripMenuItem("보이기") { CheckOnClick = false };
        _showItem.Click += (_, _) => _window.SetHidden(!_window.IsHidden);

        _lockItem = new ToolStripMenuItem("위치 고정");
        _lockItem.Click += (_, _) => _window.ToggleLocked();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_showItem);
        menu.Items.Add(_lockItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Shutdown());

        _tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "TokenNotch",
            Visible = true,
            ContextMenuStrip = menu,
        };
        // Double-clicking the tray icon is the quickest way to get it back.
        _tray.DoubleClick += (_, _) => _window.SetHidden(false);

        ListenForShowSignal();
        SyncMenu();
    }

    private const string ShowSignalName = @"Local\TokenNotchWin.Show";

    /// Second launch: poke the already-running copy and let it surface.
    private static void SignalExistingInstance()
    {
        if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var handle))
        {
            using (handle) handle.Set();
        }
    }

    /// Waits on the signal a second launch raises, then unhides the widget.
    /// Background thread so it dies with the process; the UI touch hops back
    /// onto the dispatcher.
    private void ListenForShowSignal()
    {
        var handle = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        var thread = new Thread(() =>
        {
            while (true)
            {
                handle.WaitOne();
                Dispatcher.Invoke(() => _window?.SetHidden(false));
            }
        })
        {
            IsBackground = true,
            Name = "TokenNotch show-signal",
        };
        thread.Start();
    }

    /// Reads app.ico out of the WPF resource stream rather than off disk:
    /// Assembly.Location is empty in a single-file publish, so anything
    /// path-based silently falls back to the generic system icon there.
    private static Icon LoadTrayIcon()
    {
        var uri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
        using var stream = GetResourceStream(uri)?.Stream;
        return stream is null ? SystemIcons.Application : new Icon(stream);
    }

    private void SyncMenu()
    {
        if (_window is null) return;
        if (_showItem is not null) _showItem.Text = _window.IsHidden ? "보이기" : "숨기기";
        if (_lockItem is not null) _lockItem.Checked = _window.IsLocked;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }
}
