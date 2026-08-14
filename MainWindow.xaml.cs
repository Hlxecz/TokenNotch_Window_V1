using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TokenNotchWin;

public partial class MainWindow : Window
{
    private const double CollapsedHeight = 72;
    private const double GrabbedHeight = 120; // headroom so the swing doesn't clip
    private const double RestingHeight = 155; // headroom for the parasol
    private const double Gravity = 2600;
    private const double IdleRestSeconds = 18; // no interaction for this long -> sits down
    private const double NeglectGraceSeconds = 60; // sitting calmly this long before it starts to "tan"
    private const double NeglectSpanSeconds = 20 * 60; // roughly four 5-min poll cycles to reach "please touch me"

    private readonly UsageViewModel _model = new();
    private readonly Settings _settings = Settings.Load();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly Random _random = new();

    private double _x;
    private double _bottomY;
    private int _direction = 1;
    private double _pauseUntil;
    private double _lastFrameTime;

    private const double StandUpSeconds = 0.55; // parasol furls / Clawd gets up

    private bool _expanded;
    private bool _grabbed;
    private bool _falling;
    private bool _resting;
    private double _closing; // 0..1 through the stand-up animation
    private double _fallVelocity;
    private double _grabDx, _grabDy;
    private double _lastInteraction;

    public MainWindow()
    {
        InitializeComponent();

        var work = SystemParameters.WorkArea;
        _x = _settings.X ?? work.Left + work.Width / 2;
        _bottomY = _settings.BottomY ?? work.Bottom;

        Loaded += OnLoaded;

        CharacterStrip.MouseLeftButtonDown += OnGrab;
        CharacterStrip.MouseLeftButtonUp += OnRelease;
        CharacterStrip.ContextMenu = BuildContextMenu();

        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!IsMouseOver) SetExpanded(false);
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyPlacement();

        // Driven by the render loop rather than a DispatcherTimer: a background
        // -priority timer gets starved while the layered window is being moved,
        // which is exactly when the animation needs to stay smooth.
        CompositionTarget.Rendering += OnFrame;

        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _pollTimer.Start();

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await _model.RefreshAsync();
        ApplyModel();
    }

    // MARK: Drag

    private void OnGrab(object sender, MouseButtonEventArgs e)
    {
        var cursor = CursorScreenPoint();
        _grabDx = _x - cursor.X;
        _grabDy = _bottomY - cursor.Y;
        _grabbed = true;
        _falling = false;
        _fallVelocity = 0;
        // Being yanked into the air skips the polite stand-up entirely.
        _resting = false;
        _closing = 0;
        _lastInteraction = _clock.Elapsed.TotalSeconds;

        SetExpanded(false);
        ApplyWindowHeight();
        CharacterStrip.CaptureMouse();
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (!_grabbed) return;
        _grabbed = false;
        _lastInteraction = _clock.Elapsed.TotalSeconds;
        CharacterStrip.ReleaseMouseCapture();

        var work = CurrentWorkArea();
        _x = Math.Clamp(_x, work.Left + Width / 2, work.Right - Width / 2);

        if (_settings.Locked)
        {
            // Parked on purpose — leave it exactly where it was dropped.
            SaveParkedPosition();
        }
        else if (_bottomY < work.Bottom - 1)
        {
            // Dropped mid-air: fall back down to the taskbar and carry on.
            _falling = true;
            _fallVelocity = 0;
        }
        else
        {
            _bottomY = work.Bottom;
        }
        ApplyWindowHeight();
    }

    // MARK: Frame loop

    private void OnFrame(object? sender, EventArgs e)
    {
        var t = _clock.Elapsed.TotalSeconds;
        if (_lastFrameTime == 0)
        {
            // Seed the clock; a dt of 0 would otherwise trip the throttle below
            // before _lastFrameTime is ever set, freezing the loop for good.
            _lastFrameTime = t;
            return;
        }

        var dt = Math.Min(t - _lastFrameTime, 0.1);
        // Idle animation looks fine at 30fps; dragging gets every frame the
        // compositor offers so it tracks the pointer cleanly.
        if (!_grabbed && dt < 1.0 / 30) return;
        _lastFrameTime = t;

        // Only counts down while it's free to wander — being locked or mid-
        // fall shouldn't quietly arm a rest the moment either one ends.
        var idle = t - _lastInteraction;
        if (!_grabbed && !_falling && !_settings.Locked && idle > IdleRestSeconds)
            SetResting(true);

        // Keeps sitting there long enough and it starts to tan toward black.
        // Grabbing or hovering resets _lastInteraction and starts the furl,
        // either of which snaps the colour back on the very next frame.
        var neglect = _resting && _closing <= 0
            ? Math.Clamp((idle - IdleRestSeconds - NeglectGraceSeconds) / NeglectSpanSeconds, 0, 1)
            : 0;

        Clawd.Time = t;
        Clawd.Mood = _model.ClaudeMood;
        Clawd.FacingRight = _direction > 0;
        Clawd.Grabbed = _grabbed;
        Clawd.Resting = _resting;
        Clawd.ClosingProgress = _closing;
        Clawd.NeglectFraction = neglect;
        Clawd.InvalidateVisual();

        AdvanceClosing(dt);

        if (_expanded)
        {
            PanelClawd.Time = t;
            PanelClawd.Mood = _model.ClaudeMood;
            PanelClawd.InvalidateVisual();
            PanelCodex.Time = t;
            PanelCodex.Mood = _model.CodexMood;
            PanelCodex.InvalidateVisual();
        }

        UpdateHover();

        if (_grabbed) Drag();
        else if (_falling) Fall(dt);
        else Walk(t, dt);
    }

    /// The parasol needs vertical room the walking states don't, so settling
    /// down and getting back up both have to resize the window.
    ///
    /// Standing up isn't instant: Resting stays true while _closing sweeps to
    /// 1 so the furl has something to animate against, and only then does the
    /// state actually flip. Sitting down is immediate.
    private void SetResting(bool resting)
    {
        if (resting)
        {
            if (_resting) return;
            _resting = true;
            _closing = 0;
            ApplyWindowHeight();
            return;
        }

        // Already up, or already on the way up — don't restart the furl.
        if (!_resting || _closing > 0) return;
        _closing = double.Epsilon; // nonzero marks "closing in progress"
    }

    /// Advances the furl; when it completes, Resting finally drops and the
    /// window shrinks back to walking height.
    private void AdvanceClosing(double dt)
    {
        if (!_resting || _closing <= 0) return;

        _closing = Math.Min(1, _closing + dt / StandUpSeconds);
        if (_closing >= 1)
        {
            _resting = false;
            _closing = 0;
            ApplyWindowHeight();
        }
    }

    /// Polled rather than driven by MouseEnter/MouseLeave: after a drag ends
    /// the pointer is usually already inside the window, so no enter event
    /// would ever arrive to open the panel.
    private void UpdateHover()
    {
        if (_grabbed) return;

        if (IsMouseOver)
        {
            _collapseTimer.Stop();
            SetExpanded(true);
            // Checking on it counts as attention — give it a fresh stretch of
            // walking once you move on, instead of sitting right back down.
            _lastInteraction = _clock.Elapsed.TotalSeconds;
            SetResting(false);
        }
        else if (_expanded && !_collapseTimer.IsEnabled)
        {
            // Debounced so crossing the gap between crab and panel doesn't
            // slam it shut.
            _collapseTimer.Start();
        }
    }

    private void Drag()
    {
        var cursor = CursorScreenPoint();
        _x = cursor.X + _grabDx;
        _bottomY = cursor.Y + _grabDy;
        ApplyPlacement();
    }

    private void Fall(double dt)
    {
        var floor = CurrentWorkArea().Bottom;
        _fallVelocity += Gravity * dt;
        _bottomY += _fallVelocity * dt;

        if (_bottomY >= floor)
        {
            _bottomY = floor;
            // One small bounce on landing, then settle.
            if (_fallVelocity > 450)
            {
                _fallVelocity = -_fallVelocity * 0.32;
                _bottomY = floor - 1;
            }
            else
            {
                _falling = false;
                ApplyWindowHeight();
            }
        }
        ApplyPlacement();
    }

    /// Clawd patrols the taskbar edge, turning around at the screen bounds and
    /// stopping to rest now and then. Hovering freezes it so the panel stays
    /// under the pointer.
    private void Walk(double t, double dt)
    {
        // _resting covers the stand-up too, so no walking until the parasol
        // is fully furled.
        if (_settings.Locked || _resting || _expanded || _model.ClaudeMood == Mood.Sleeping || t < _pauseUntil) return;

        var speed = _model.ClaudeMood switch
        {
            Mood.Happy => 26.0,
            Mood.Worried => 44.0,
            Mood.Critical => 72.0, // frantic dashing
            _ => 0.0,
        };

        _x += speed * dt * _direction;

        var work = CurrentWorkArea();
        var half = Width / 2;
        if (_x - half <= work.Left)
        {
            _x = work.Left + half;
            Turn(t);
        }
        else if (_x + half >= work.Right)
        {
            _x = work.Right - half;
            Turn(t);
        }
        else if (_random.NextDouble() < 0.0015)
        {
            // Occasional idle break, sometimes with a change of heart.
            _pauseUntil = t + 1.5 + _random.NextDouble() * 3;
            if (_random.NextDouble() < 0.4) _direction = -_direction;
        }

        ApplyPlacement();
    }

    private void Turn(double t)
    {
        _direction = -_direction;
        // Beat at the wall before heading back — reads as "thinking".
        _pauseUntil = t + 0.6 + _random.NextDouble();
    }

    /// The work area of whichever monitor Clawd is currently standing on.
    /// SystemParameters.WorkArea only ever describes the primary display, so
    /// using it would yank the crab back whenever it was moved to another one.
    private Rect CurrentWorkArea()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var probe = new System.Drawing.Point(
            (int)(_x * dpi.DpiScaleX),
            (int)((_bottomY - 2) * dpi.DpiScaleY));
        var area = System.Windows.Forms.Screen.FromPoint(probe).WorkingArea;
        return new Rect(
            area.Left / dpi.DpiScaleX,
            area.Top / dpi.DpiScaleY,
            area.Width / dpi.DpiScaleX,
            area.Height / dpi.DpiScaleY);
    }

    /// Each assignment repaints the whole layered window, so skip no-op moves.
    private void ApplyPlacement()
    {
        var left = _x - Width / 2;
        var top = _bottomY - Height;
        if (Math.Abs(Left - left) > 0.01) Left = left;
        if (Math.Abs(Top - top) > 0.01) Top = top;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// The window moves while dragging, so pointer positions relative to it
    /// would feed back into themselves — screen coordinates stay stable.
    private Point CursorScreenPoint()
    {
        if (!GetCursorPos(out var raw)) return new Point(_x, _bottomY);
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice;
        return transform is { } m ? m.Transform(new Point(raw.X, raw.Y)) : new Point(raw.X, raw.Y);
    }

    // MARK: Hover panel

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;

        Panel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ApplyWindowHeight();
    }

    /// Single owner of the window height — several states each need their own
    /// amount of vertical room, and having them set Height independently made
    /// whichever ran last win.
    private void ApplyWindowHeight()
    {
        if (_expanded) FitToPanel();
        else if (_grabbed) Height = GrabbedHeight;
        else if (_resting) Height = RestingHeight;
        else Height = CollapsedHeight;
        ApplyPlacement();
    }

    /// The panel's height depends on how many usage rows the plan reports and
    /// whether an error line is showing, so it's measured rather than fixed.
    private void FitToPanel()
    {
        Panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Height = Panel.DesiredSize.Height + CollapsedHeight + 6;
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // MARK: Lock / hide

    public bool IsLocked => _settings.Locked;

    /// Locked means "stay exactly where I put you": no patrol, and no falling
    /// back to the taskbar when dropped — which is also how you park it on a
    /// second monitor.
    public void ToggleLocked()
    {
        _settings.Locked = !_settings.Locked;
        _falling = false;
        SaveParkedPosition();
        RefreshMenus();
    }

    public void SetHidden(bool hidden)
    {
        if (hidden) Hide();
        else Show();
        RefreshMenus();
    }

    public bool IsHidden => !IsVisible;

    private void SaveParkedPosition()
    {
        _settings.X = _settings.Locked ? _x : null;
        _settings.BottomY = _settings.Locked ? _bottomY : null;
        _settings.Save();
    }

    private MenuItem _lockItem = null!;

    private ContextMenu BuildContextMenu()
    {
        _lockItem = new MenuItem { Header = "위치 고정", IsCheckable = true, IsChecked = _settings.Locked };
        _lockItem.Click += (_, _) => ToggleLocked();

        var hide = new MenuItem { Header = "숨기기" };
        hide.Click += (_, _) => SetHidden(true);

        var quit = new MenuItem { Header = "종료" };
        quit.Click += (_, _) => Application.Current.Shutdown();

        var menu = new ContextMenu();
        menu.Items.Add(_lockItem);
        menu.Items.Add(hide);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        return menu;
    }

    /// The tray menu mirrors these toggles, so both have to be told.
    public event Action? MenuStateChanged;

    private void RefreshMenus()
    {
        _lockItem.IsChecked = _settings.Locked;
        MenuStateChanged?.Invoke();
    }

    // MARK: Binding

    private void ApplyModel()
    {
        PercentText.Text = _model.ClaudePercentText;
        PercentText.Foreground = _model.ClaudePercentBrush;

        UpdatedText.Text = _model.LastUpdatedText;
        ClaudePhrase.Text = _model.ClaudePhrase;
        CodexPhrase.Text = _model.CodexPhrase;
        CodexTitleText.Text = _model.CodexTitle;

        SetError(ClaudeErrorText, _model.ClaudeError);
        SetError(CodexErrorText, _model.CodexError);

        ClaudeRows.Children.Clear();
        ClaudeRows.Children.Add(BuildRow("5시간 세션", _model.FiveHour, withCountdown: true));
        ClaudeRows.Children.Add(BuildRow("주간 (전체)", _model.SevenDay, withCountdown: false));
        if (_model.SevenDayOpus?.Utilization is not null)
            ClaudeRows.Children.Add(BuildRow("주간 (Opus)", _model.SevenDayOpus, withCountdown: false));
        if (_model.SevenDaySonnet?.Utilization is not null)
            ClaudeRows.Children.Add(BuildRow("주간 (Sonnet)", _model.SevenDaySonnet, withCountdown: false));

        CodexRows.Children.Clear();
        if (_model.CodexFiveHour is not null)
            CodexRows.Children.Add(BuildRow("5시간 세션", _model.CodexFiveHour, withCountdown: true));
        CodexRows.Children.Add(BuildRow("주간", _model.CodexSevenDay, withCountdown: true));

        if (_expanded)
        {
            UpdateLayout();
            FitToPanel();
            ApplyPlacement();
        }
    }

    private static void SetError(TextBlock target, string? message)
    {
        target.Text = message ?? string.Empty;
        target.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// One usage window: title, remaining badge, HP bar, reset time.
    private static UIElement BuildRow(string title, UsageWindow? window, bool withCountdown)
    {
        var remaining = window?.RemainingPercent;
        var brush = Format.StatusBrush(remaining);

        var header = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 255, 255, 255)),
        });
        header.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            CornerRadius = new CornerRadius(7),
            Background = brush,
            Padding = new Thickness(6, 1, 6, 1),
            Child = new TextBlock
            {
                Text = $"{Format.Percent(remaining)} 남음",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
            },
        });

        var reset = withCountdown
            ? $"{Format.ClockTime(window?.ResetsAt)} 리셋 · {Format.Countdown(window?.ResetsAt)}"
            : $"{Format.DayTime(window?.ResetsAt)} 리셋";

        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
        stack.Children.Add(header);
        stack.Children.Add(BuildHpBar(remaining, brush));
        stack.Children.Add(new TextBlock
        {
            Text = reset,
            Margin = new Thickness(0, 3, 0, 0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromArgb(0x88, 255, 255, 255)),
        });
        return stack;
    }

    /// Game-style HP bar: the filled part is what's LEFT.
    private static UIElement BuildHpBar(double? remaining, Brush brush)
    {
        var fraction = Math.Clamp((remaining ?? 0) / 100.0, 0, 1);
        var track = new Border
        {
            Height = 7,
            CornerRadius = new CornerRadius(3.5),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
        };
        var fill = new Border
        {
            Height = 7,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(3.5),
            Background = brush,
        };

        var grid = new Grid();
        grid.Children.Add(track);
        grid.Children.Add(fill);
        // The bar's pixel width is only known once the panel is laid out.
        grid.SizeChanged += (_, e) => fill.Width = Math.Max(7, e.NewSize.Width * fraction);
        return grid;
    }
}
