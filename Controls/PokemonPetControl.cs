using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokenNotchWin;

public sealed class PokemonPetControl : FrameworkElement
{
    public static readonly DependencyProperty MoodProperty =
        DependencyProperty.Register(nameof(Mood), typeof(Mood), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(TokenNotchWin.Mood.Happy,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StageProperty =
        DependencyProperty.Register(nameof(Stage), typeof(Stage), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(TokenNotchWin.Stage.Charmander,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CharacterProperty =
        DependencyProperty.Register(nameof(Character), typeof(PetCharacter), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(PetCharacter.PixelCharmander,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FacingRightProperty =
        DependencyProperty.Register(nameof(FacingRight), typeof(bool), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MovingProperty =
        DependencyProperty.Register(nameof(Moving), typeof(bool), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScaleProperty =
        DependencyProperty.Register(nameof(Scale), typeof(double), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure
                                                | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GrabbedProperty =
        DependencyProperty.Register(nameof(Grabbed), typeof(bool), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RestingProperty =
        DependencyProperty.Register(nameof(Resting), typeof(bool), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NeglectFractionProperty =
        DependencyProperty.Register(nameof(NeglectFraction), typeof(double), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EvolveFlashProperty =
        DependencyProperty.Register(nameof(EvolveFlash), typeof(double), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Mood Mood { get => (Mood)GetValue(MoodProperty); set => SetValue(MoodProperty, value); }
    public Stage Stage { get => (Stage)GetValue(StageProperty); set => SetValue(StageProperty, value); }
    public PetCharacter Character { get => (PetCharacter)GetValue(CharacterProperty); set => SetValue(CharacterProperty, value); }
    public bool FacingRight { get => (bool)GetValue(FacingRightProperty); set => SetValue(FacingRightProperty, value); }
    public bool Moving { get => (bool)GetValue(MovingProperty); set => SetValue(MovingProperty, value); }
    public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public bool Grabbed { get => (bool)GetValue(GrabbedProperty); set => SetValue(GrabbedProperty, value); }
    public bool Resting { get => (bool)GetValue(RestingProperty); set => SetValue(RestingProperty, value); }
    public double NeglectFraction { get => (double)GetValue(NeglectFractionProperty); set => SetValue(NeglectFractionProperty, value); }
    public double EvolveFlash { get => (double)GetValue(EvolveFlashProperty); set => SetValue(EvolveFlashProperty, value); }
    public double Time { get; set; }
    public double RestElapsed { get; set; }

    private const double CanvasWidth = 112;
    private const double CanvasHeight = 120;
    private static readonly Brush ShadowBrush = Frozen(Color.FromArgb(55, 0, 0, 0));
    private static readonly Brush SweatBrush = Frozen(Color.FromRgb(99, 188, 238));
    private static readonly Typeface Typeface = new("Segoe UI");

    public PokemonPetControl()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(CanvasWidth * Scale, CanvasHeight * Scale);

    protected override void OnRender(DrawingContext dc)
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        if (!PokemonSpriteAtlas.TryFor(PixelEvolution.SpeciesFor(Character, Stage), out var atlas)
            || atlas is null)
            return;

        var action = ActionForState();
        var elapsed = Resting ? RestElapsed : Time;
        var sprite = atlas.Frame(action, elapsed, holdLast: Resting);
        var u = Math.Max(0.1, Scale);
        var width = atlas.CellWidth * u;
        var height = atlas.CellHeight * u;
        var groundY = RenderSize.Height;
        var rect = new Rect(
            Math.Round((RenderSize.Width - width) / 2),
            Math.Round(groundY - height),
            width,
            height);

        if (!Grabbed)
        {
            var shadowRadius = Math.Max(8, atlas.CellWidth * 0.18) * u;
            dc.DrawEllipse(ShadowBrush, null,
                new Point(RenderSize.Width / 2, groundY - u), shadowRadius, u);
        }

        var centerX = rect.X + rect.Width / 2;
        var tilt = Grabbed ? Math.Sin(Time * 7.5) * 8 : 0;
        dc.PushTransform(new RotateTransform(tilt, centerX, rect.Bottom));
        dc.DrawImage(sprite, rect);
        DrawTint(dc, sprite, rect, Colors.Black, Math.Clamp(NeglectFraction, 0, 1) * 0.84);
        DrawTint(dc, sprite, rect, Colors.White, Math.Clamp(EvolveFlash, 0, 1) * 0.92);
        dc.Pop();

        DrawMoodEffect(dc, rect);
    }

    private string ActionForState()
    {
        if (Resting) return "failed";
        if (Grabbed) return "jump";
        if (Moving) return FacingRight ? "walk-right" : "walk-left";
        if (Mood is Mood.Worried or Mood.Critical) return "working";
        return "idle";
    }

    private static void DrawTint(DrawingContext dc, BitmapSource sprite, Rect rect,
        Color color, double opacity)
    {
        if (opacity <= 0.001) return;
        var mask = new ImageBrush(sprite) { Stretch = Stretch.Fill };
        mask.Freeze();
        dc.PushOpacity(opacity);
        dc.PushOpacityMask(mask);
        dc.DrawRectangle(Frozen(color), null, rect);
        dc.Pop();
        dc.Pop();
    }

    private void DrawMoodEffect(DrawingContext dc, Rect rect)
    {
        var u = Math.Max(0.1, Scale);
        if (NeglectFraction >= 0.92)
        {
            var pulse = 0.5 + 0.5 * Math.Sin(Time * 2.4);
            var brush = Frozen(Color.FromArgb((byte)(155 + 95 * pulse), 255, 132, 154));
            var heart = new FormattedText("♥", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface, (6 + pulse) * u, brush, 1.0);
            dc.DrawText(heart, new Point(rect.X + rect.Width / 2 - heart.Width / 2,
                Math.Max(0, rect.Y)));
        }
        else if (Mood is Mood.Worried or Mood.Critical && !Resting)
        {
            var phase = Mod1(Time * (Mood == Mood.Critical ? 3.5 : 2.0));
            var x = FacingRight ? rect.Right - 18 * u : rect.Left + 17 * u;
            dc.DrawRectangle(SweatBrush, null,
                new Rect(x, rect.Y + (15 + phase * 4) * u, u, 2 * u));
        }
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static double Mod1(double value)
    {
        var result = value % 1.0;
        return result < 0 ? result + 1 : result;
    }
}
