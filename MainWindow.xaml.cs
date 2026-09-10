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
    private const double CollapsedHeight = 190; // tall enough for the sprite plus its bob
    private const double GrabbedHeight = 200;   // headroom so the swing doesn't clip
    private const double RestingHeight = 240;   // headroom for Clawd's parasol
    private const double Gravity = 2600;
    private const double IdleRestSeconds = 18; // no interaction for this long -> sits down
    private const double NeglectGraceSeconds = 60; // sitting calmly this long before it starts to "tan"
    private const double NeglectSpanSeconds = 20 * 60; // roughly four 5-min poll cycles to reach "please touch me"

    private const double EvolveFlashSeconds = 2.2;

    private readonly Settings _settings = Settings.Load();
    private readonly UsageViewModel _model;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly Random _random = new();

    private double _x;
    private double _bottomY;
    private int _direction = 1;
    private double _pauseUntil;
    private double _lastFrameTime;

    private const double StandUpSeconds = 0.55; // Clawd's parasol furls

    private bool _expanded;
    private bool _grabbed;
    private bool _falling;
    private bool _resting;
    private double _closing; // 0..1 through the stand-up animation
    private double _fallVelocity;
    private double _grabDx, _grabDy;
    private double _lastInteraction;
    private double _restStartedAt;

    private Stage _stage;
    private double _evolveUntil; // clock time the evolution flash ends
    private bool _previewStage; // True when TOKENNOTCH_STAGE forces a preview.
    private PetCharacter _activePet = PetCharacter.Clawd;

    private bool UsesPokemonRest => PixelEvolution.IsPokemon(_activePet);

    private readonly Dictionary<PetCharacter, MenuItem> _characterItems = new();
    private readonly Dictionary<AiProvider, MenuItem> _mainProviderItems = new();

    private const string ProviderDragFormat = "TokenNotchWin.AiProvider";
    private Point _cardDragStart;
    private AiProvider? _draggedProvider;
    private UIElement? _cardDragHandle;
    private Border? _cardDragTarget;

    public MainWindow()
    {
        InitializeComponent();

        _activePet = PokemonSpriteAtlas.IsAvailable(_settings.Pet)
            ? _settings.Pet
            : PetCharacter.Clawd;

        _model = new UsageViewModel(_settings.CumulativeUsagePoints);
        _stage = _model.Stage;
        // Preview a stage without accumulating usage. Preview mode also keeps
        // the pet awake so the idle tint does not obscure the selected stage.
        if (Environment.GetEnvironmentVariable("TOKENNOTCH_STAGE") is { } s
            && Enum.TryParse<Stage>(s, true, out var forced))
        {
            _stage = forced;
            _previewStage = true;
        }
        _model.UsagePointsChanged += OnUsagePointsChanged;

        var work = SystemParameters.WorkArea;
        _x = _settings.X ?? work.Left + work.Width / 2;
        _bottomY = _settings.BottomY ?? work.Bottom;

        Loaded += OnLoaded;

        CharacterStrip.MouseLeftButtonDown += OnGrab;
        CharacterStrip.MouseLeftButtonUp += OnRelease;
        CharacterStrip.ContextMenu = BuildContextMenu();
        ApplyCharacter();
        ApplyProviderPreferences();
        ApplyCardOrder();

        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (_draggedProvider is null && !IsMouseOver) SetExpanded(false);
        };
    }

    /// Persist every gain — the pet's progress should survive a crash, not just
    /// a clean exit — and fire the evolution beat when a threshold is crossed.
    private void OnUsagePointsChanged(double points)
    {
        _settings.CumulativeUsagePoints = points;
        _settings.Save();

        var stage = _model.Stage;
        if (stage != _stage)
        {
            _stage = stage;
            _evolveUntil = _clock.Elapsed.TotalSeconds + EvolveFlashSeconds;
        }
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
        var neglect = _resting && _closing <= 0 && !_previewStage
            ? Math.Clamp((idle - IdleRestSeconds - NeglectGraceSeconds) / NeglectSpanSeconds, 0, 1)
            : 0;

        // Fades out over the flash window rather than cutting off, so the
        // white wash and the burst retreat together.
        var evolve = _evolveUntil > t ? (_evolveUntil - t) / EvolveFlashSeconds : 0;

        var primaryMood = _model.MoodFor(_settings.PrimaryProvider);
        var pokemonMoving = !_settings.Locked && !_resting && !_expanded
                            && primaryMood != Mood.Sleeping && t >= _pauseUntil
                            && !_grabbed && !_falling;

        if (ClawdPet.Visibility == Visibility.Visible)
        {
            ClawdPet.Time = t;
            ClawdPet.Mood = primaryMood;
            ClawdPet.FacingRight = _direction > 0;
            ClawdPet.Grabbed = _grabbed;
            ClawdPet.Resting = _resting;
            ClawdPet.ClosingProgress = _closing;
            ClawdPet.NeglectFraction = neglect;
            ClawdPet.InvalidateVisual();
        }

        if (PokemonPet.Visibility == Visibility.Visible)
        {
            PokemonPet.Time = t;
            PokemonPet.RestElapsed = Math.Max(0, t - _restStartedAt);
            PokemonPet.Mood = primaryMood;
            PokemonPet.Character = _activePet;
            PokemonPet.Stage = _stage;
            PokemonPet.FacingRight = _direction > 0;
            PokemonPet.Moving = pokemonMoving;
            PokemonPet.Grabbed = _grabbed;
            PokemonPet.Resting = _resting;
            PokemonPet.NeglectFraction = neglect;
            PokemonPet.EvolveFlash = evolve;
            PokemonPet.InvalidateVisual();
        }

        AdvanceClosing(dt);

        if (_expanded)
        {
            UpdateCharacterPanel(t, primaryMood);
        }

        UpdateHover();

        if (_grabbed) Drag();
        else if (_falling) Fall(dt);
        else Walk(t, dt);
    }

    private void UpdateCharacterPanel(double t, Mood mood)
    {
        if (PanelPokemon.Visibility == Visibility.Visible)
        {
            PanelPokemon.Time = t;
            PanelPokemon.Mood = mood;
            PanelPokemon.Character = _activePet;
            PanelPokemon.Stage = _stage;
            PanelPokemon.Moving = false;
            PanelPokemon.Resting = false;
            PanelPokemon.InvalidateVisual();
        }
        else if (PanelClawdCrab.Visibility == Visibility.Visible)
        {
            PanelClawdCrab.Time = t;
            PanelClawdCrab.Mood = mood;
            PanelClawdCrab.InvalidateVisual();
        }
    }

    /// Pokemon settle through their own animation. Only Clawd keeps the slower
    /// stand-up transition used by its separate parasol drawing.
    private void SetResting(bool resting)
    {
        if (resting)
        {
            if (_resting) return;
            _resting = true;
            _closing = 0;
            _restStartedAt = _clock.Elapsed.TotalSeconds;
            ApplyWindowHeight();
            return;
        }

        if (UsesPokemonRest)
        {
            if (!_resting) return;
            _resting = false;
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

        // WPF reports IsMouseOver=false while DoDragDrop owns the pointer.
        // Treat an active card reorder as interaction so the panel does not
        // collapse before the pointer reaches the other card.
        if (_draggedProvider is not null)
        {
            _collapseTimer.Stop();
            return;
        }

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
        // For Clawd, _resting also covers the stand-up transition until its
        // parasol is fully furled.
        var primaryMood = _model.MoodFor(_settings.PrimaryProvider);
        if (_settings.Locked || _resting || _expanded || primaryMood == Mood.Sleeping || t < _pauseUntil) return;

        var speed = primaryMood switch
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
        else if (_resting && !UsesPokemonRest) Height = RestingHeight;
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
        menu.Items.Add(BuildCharacterMenu());
        menu.Items.Add(BuildMainProviderMenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(_lockItem);
        menu.Items.Add(hide);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        return menu;
    }

    private MenuItem BuildCharacterMenu()
    {
        var root = new MenuItem { Header = "캐릭터" };
        foreach (var (pet, label) in new[]
                 {
                     (PetCharacter.PixelCharmander, "픽셀 파이리 (진화형)"),
                     (PetCharacter.PixelPikachu, "픽셀 피카츄"),
                     (PetCharacter.PixelBulbasaur, "픽셀 이상해씨 (진화형)"),
                     (PetCharacter.PixelSquirtle, "픽셀 꼬부기 (진화형)"),
                     (PetCharacter.Clawd, "Clawd (게)"),
                     (PetCharacter.Ditto, "메타몽"),
                     (PetCharacter.Snorlax, "잠만보"),
                 })
        {
            if (!PokemonSpriteAtlas.IsAvailable(pet)) continue;

            var item = new MenuItem { Header = label, IsCheckable = true };
            _characterItems[pet] = item;
            item.Click += (_, _) => SetCharacter(pet);
            root.Items.Add(item);
        }
        return root;
    }

    private void SetCharacter(PetCharacter pet)
    {
        if (!PokemonSpriteAtlas.IsAvailable(pet)) return;

        _activePet = pet;
        _settings.Character = pet.ToString();
        _settings.Save();
        ApplyCharacter();
        RefreshMenus();
    }

    /// Swaps which sprite is on screen. The window is sized for the taller of
    /// the two, so switching never needs a resize.
    private void ApplyCharacter()
    {
        var pet = _activePet;
        var pokemon = PixelEvolution.IsPokemon(pet);
        var crab = pet == PetCharacter.Clawd;

        PokemonPet.Visibility = pokemon ? Visibility.Visible : Visibility.Collapsed;
        ClawdPet.Visibility = crab ? Visibility.Visible : Visibility.Collapsed;

        // The hover panel shows the same pet as the taskbar, and the stage bar
        // only appears for Pokemon families that evolve.
        PanelPokemon.Visibility = PokemonPet.Visibility;
        PanelClawdCrab.Visibility = ClawdPet.Visibility;
        GrowthPanel.Visibility = PixelEvolution.Evolves(pet)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (pokemon)
        {
            PokemonPet.Character = pet;
            PokemonPet.Stage = _stage;
            PanelPokemon.Character = pet;
            PanelPokemon.Stage = _stage;
        }
        CharacterNameText.Text = pokemon
            ? PixelEvolution.Name(pet, _stage)
            : "Clawd";
        if (PixelEvolution.Evolves(pet)) ApplyGrowth();
        ApplyWindowHeight();

        foreach (var (key, item) in _characterItems) item.IsChecked = key == pet;
    }

    private MenuItem BuildMainProviderMenu()
    {
        var root = new MenuItem { Header = "메인 AI" };
        foreach (var (provider, label) in new[]
                 {
                     (AiProvider.Claude, "Claude"),
                     (AiProvider.Codex, "GPT / Codex"),
                 })
        {
            var item = new MenuItem { Header = label, IsCheckable = true };
            _mainProviderItems[provider] = item;
            item.Click += (_, _) => SetMainProvider(provider);
            root.Items.Add(item);
        }
        return root;
    }

    private void MainProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && ProviderFromTag(element.Tag) is { } provider)
            SetMainProvider(provider);
    }

    private void SetMainProvider(AiProvider provider)
    {
        _settings.MainProvider = provider.ToString();
        _settings.Save();

        ApplyProviderPreferences();
        ApplyModel();

        _lastInteraction = _clock.Elapsed.TotalSeconds;
        SetResting(false);
    }

    private void ApplyProviderPreferences()
    {
        var provider = _settings.PrimaryProvider;
        ClaudeMainOption.IsChecked = provider == AiProvider.Claude;
        CodexMainOption.IsChecked = provider == AiProvider.Codex;
        ClaudeMainBadge.Visibility = provider == AiProvider.Claude
            ? Visibility.Visible
            : Visibility.Collapsed;
        CodexMainBadge.Visibility = provider == AiProvider.Codex
            ? Visibility.Visible
            : Visibility.Collapsed;
        MainProviderText.Text = provider == AiProvider.Claude ? "CLAUDE" : "GPT";

        foreach (var (key, item) in _mainProviderItems)
            item.IsChecked = key == provider;
    }

    // MARK: Provider card order

    private static AiProvider? ProviderFromTag(object? tag) =>
        tag is string value && Enum.TryParse<AiProvider>(value, ignoreCase: true, out var provider)
            ? provider
            : null;

    private Border CardFor(AiProvider provider) => provider switch
    {
        AiProvider.Codex => CodexCard,
        _ => ClaudeCard,
    };

    private void ApplyCardOrder()
    {
        var order = _settings.OrderedProviders;
        CardHost.Children.Clear();
        for (var i = 0; i < order.Count; i++)
        {
            var card = CardFor(order[i]);
            card.Margin = i < order.Count - 1
                ? new Thickness(0, 0, 0, 7)
                : new Thickness(0);
            CardHost.Children.Add(card);
        }
    }

    private void SaveCardOrder(IReadOnlyList<AiProvider> order)
    {
        _settings.SetCardOrder(order);
        _settings.Save();
        ApplyCardOrder();

        if (_expanded)
        {
            UpdateLayout();
            FitToPanel();
            ApplyPlacement();
        }
    }

    private void CardHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement handle || sender is not FrameworkElement element) return;
        if (ProviderFromTag(element.Tag) is not { } provider) return;

        _draggedProvider = provider;
        _cardDragStart = e.GetPosition(this);
        _cardDragHandle = handle;
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void CardHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CancelCardDrag();
        e.Handled = true;
    }

    private void CardHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedProvider is not { } provider || e.LeftButton != MouseButtonState.Pressed)
        {
            CancelCardDrag();
            return;
        }

        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _cardDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - _cardDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var handle = _cardDragHandle ?? sender as UIElement;
        handle?.ReleaseMouseCapture();
        _cardDragHandle = null;

        var data = new DataObject();
        data.SetData(ProviderDragFormat, provider.ToString());
        try
        {
            DragDrop.DoDragDrop(handle ?? this, data, DragDropEffects.Move);
        }
        finally
        {
            _draggedProvider = null;
            SetCardDragTarget(null);
        }
        e.Handled = true;
    }

    private void CancelCardDrag()
    {
        _cardDragHandle?.ReleaseMouseCapture();
        _cardDragHandle = null;
        _draggedProvider = null;
        SetCardDragTarget(null);
    }

    private static AiProvider? ProviderFromDrag(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(ProviderDragFormat)) return null;
        return e.Data.GetData(ProviderDragFormat) is string value
               && Enum.TryParse<AiProvider>(value, ignoreCase: true, out var provider)
            ? provider
            : null;
    }

    private void ProviderCard_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border card && ProviderFromDrag(e) is not null)
            SetCardDragTarget(card);
    }

    private void ProviderCard_DragLeave(object sender, DragEventArgs e)
    {
        if (ReferenceEquals(sender, _cardDragTarget)) SetCardDragTarget(null);
    }

    private void ProviderCard_DragOver(object sender, DragEventArgs e)
    {
        var source = ProviderFromDrag(e);
        var target = sender is FrameworkElement element ? ProviderFromTag(element.Tag) : null;
        e.Effects = source is not null && target is not null && source != target
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ProviderCard_Drop(object sender, DragEventArgs e)
    {
        var source = ProviderFromDrag(e);
        var target = sender is FrameworkElement element ? ProviderFromTag(element.Tag) : null;
        if (source is { } from && target is { } to && from != to)
        {
            var order = _settings.OrderedProviders.ToList();
            var sourceIndex = order.IndexOf(from);
            var targetIndex = order.IndexOf(to);
            (order[sourceIndex], order[targetIndex]) = (order[targetIndex], order[sourceIndex]);
            SaveCardOrder(order);
        }

        SetCardDragTarget(null);
        e.Handled = true;
    }

    private void CardHost_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = ProviderFromDrag(e) is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    private void CardHost_Drop(object sender, DragEventArgs e)
    {
        if (ProviderFromDrag(e) is { } provider)
        {
            var order = _settings.OrderedProviders.Where(item => item != provider).ToList();
            order.Add(provider);
            SaveCardOrder(order);
        }
        SetCardDragTarget(null);
        e.Handled = true;
    }

    private void SetCardDragTarget(Border? target)
    {
        if (_cardDragTarget is not null)
            _cardDragTarget.BorderBrush = Brushes.Transparent;
        _cardDragTarget = target;
        if (_cardDragTarget is not null)
            _cardDragTarget.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 255, 255, 255));
    }

    /// The tray menu mirrors these toggles, so both have to be told.
    public event Action? MenuStateChanged;

    private void RefreshMenus()
    {
        _lockItem.IsChecked = _settings.Locked;
        MenuStateChanged?.Invoke();
    }

    // MARK: Binding

    /// The growth bar tracks lifetime usage, so unlike every other row here it
    /// only ever fills — a window reset doesn't walk it back.
    private void ApplyGrowth()
    {
        if (!PixelEvolution.Evolves(_activePet)) return;

        var points = _model.CumulativeUsagePoints;
        var (fraction, remaining) = PixelEvolution.Progress(points);

        CharacterNameText.Text = PixelEvolution.Name(_activePet, _stage);
        StageText.Text = $"누적 {points:N0}";
        StageProgressText.Text = _stage == Stage.Charizard
            ? "최종 진화"
            : $"다음 진화까지 {remaining:N0}";

        // The track is the parent Border; width is only known once laid out.
        if (StageBar.Parent is FrameworkElement track && track.ActualWidth > 0)
            StageBar.Width = track.ActualWidth * fraction;
    }

    private void ApplyModel()
    {
        var provider = _settings.PrimaryProvider;
        PercentText.Text = _model.PercentTextFor(provider);
        PercentText.Foreground = _model.PercentBrushFor(provider);
        MainProviderText.Text = provider == AiProvider.Claude ? "CLAUDE" : "GPT";

        UpdatedText.Text = _model.LastUpdatedText;
        ClaudePhrase.Text = _model.ClaudePhrase;
        CodexPhrase.Text = _model.CodexPhrase;
        CodexTitleText.Text = _model.CodexTitle;

        ApplyGrowth();

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
