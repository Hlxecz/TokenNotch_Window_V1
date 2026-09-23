using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokenNotchWin;

public sealed class PokemonPetControl : FrameworkElement
{
    public const double EvolutionChargeSeconds = 1.2;
    public const double EvolutionDurationSeconds = 2.4;
    public const double EvolutionSwitchProgress = EvolutionChargeSeconds / EvolutionDurationSeconds;
    public const double SkillDurationSeconds = 0.9;

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

    public static readonly DependencyProperty EvolutionProgressProperty =
        DependencyProperty.Register(nameof(EvolutionProgress), typeof(double), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SkillProgressProperty =
        DependencyProperty.Register(nameof(SkillProgress), typeof(double), typeof(PokemonPetControl),
            new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

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
    public double EvolutionProgress { get => (double)GetValue(EvolutionProgressProperty); set => SetValue(EvolutionProgressProperty, value); }
    public double SkillProgress { get => (double)GetValue(SkillProgressProperty); set => SetValue(SkillProgressProperty, value); }
    public double Time { get; set; }
    public double RestElapsed { get; set; }

    private const double CanvasWidth = 112;
    private const double CanvasHeight = 120;
    private static readonly Brush ShadowBrush = Frozen(Color.FromArgb(55, 0, 0, 0));
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
        var elapsed = Resting
            ? RestElapsed
            : EvolutionProgress >= 0 && EvolutionProgress < EvolutionSwitchProgress
                ? EvolutionProgress * EvolutionDurationSeconds
                : SkillProgress >= 0
                    ? SkillProgress * SkillDurationSeconds
                    : Time;
        var sprite = atlas.Frame(action, elapsed, holdLast: Resting || SkillProgress >= 0);
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
        dc.PushTransform(new ScaleTransform(EvolutionScale(), EvolutionScale(), centerX, rect.Bottom));
        dc.PushTransform(new RotateTransform(tilt, centerX, rect.Bottom));
        dc.DrawImage(sprite, rect);
        if (EvolutionProgress < 0)
            DrawTint(dc, sprite, rect, Colors.Black, Math.Clamp(NeglectFraction, 0, 1) * 0.84);
        DrawTint(dc, sprite, rect, Colors.White, Math.Clamp(EvolveFlash, 0, 1) * 0.92);
        dc.Pop();
        dc.Pop();

        if (SkillProgress >= 0) DrawSkillEffect(dc, rect);
        else if (EvolutionProgress < 0) DrawMoodEffect(dc, rect);
    }

    private string ActionForState()
    {
        if (EvolutionProgress >= 0 && EvolutionProgress < EvolutionSwitchProgress) return "evolve";
        if (SkillProgress >= 0) return FacingRight ? "skill-right" : "skill-left";
        if (Resting) return "failed";
        if (Grabbed) return "jump";
        if (Moving) return FacingRight ? "walk-right" : "walk-left";
        if (Mood is Mood.Worried or Mood.Critical) return "working";
        return "idle";
    }

    private double EvolutionScale()
    {
        if (EvolutionProgress < 0) return 1;

        var progress = Math.Clamp(EvolutionProgress, 0, 1);
        if (progress < EvolutionSwitchProgress)
        {
            var charge = progress / EvolutionSwitchProgress;
            return 1 + Math.Sin(charge * Math.PI * 6) * 0.025 * charge;
        }

        var settle = (progress - EvolutionSwitchProgress) / (1 - EvolutionSwitchProgress);
        var eased = 1 - Math.Pow(1 - settle, 3);
        return 1.16 - 0.16 * eased;
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
    }

    private void DrawSkillEffect(DrawingContext dc, Rect rect)
    {
        var progress = Math.Clamp(SkillProgress, 0, 1);
        var direction = FacingRight ? 1.0 : -1.0;
        var u = Math.Max(0.1, Scale);
        var startX = rect.X + rect.Width / 2
                     + direction * Math.Min(rect.Width * 0.32, 25 * u);
        var endX = FacingRight ? RenderSize.Width - 7 * u : 7 * u;
        var y = rect.Y + rect.Height * 0.47;
        var color = SkillColor(Character);
        var brush = Frozen(color);
        var core = Frozen(Color.FromRgb(255, 252, 224));

        if (progress < 0.28)
        {
            var charge = progress / 0.28;
            var radius = (2.2 + charge * 2.8) * u;
            DrawDiamond(dc, brush, new Point(startX, y), radius);
            DrawDiamond(dc, core, new Point(startX, y), radius * 0.38);
            return;
        }

        var travel = (progress - 0.28) / 0.72;
        var eased = 1 - Math.Pow(1 - travel, 2);
        var x = startX + (endX - startX) * eased;
        var size = (4.4 - travel * 1.2) * u;
        for (var i = 1; i <= 3; i++)
        {
            var trailSize = Math.Max(1, size * (0.42 - i * 0.07));
            dc.DrawRectangle(brush, null, new Rect(
                x - direction * i * 3.2 * u - trailSize / 2,
                y - trailSize / 2,
                trailSize,
                trailSize));
        }
        DrawDiamond(dc, brush, new Point(x, y), size);
        DrawDiamond(dc, core, new Point(x, y), size * 0.38);
    }

    private static void DrawDiamond(DrawingContext dc, Brush brush, Point center, double radius)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(center.X, center.Y - radius), true, true);
            context.LineTo(new Point(center.X + radius, center.Y), true, false);
            context.LineTo(new Point(center.X, center.Y + radius), true, false);
            context.LineTo(new Point(center.X - radius, center.Y), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private static Color SkillColor(PetCharacter pet) => pet switch
    {
        PetCharacter.PixelCharmander => Color.FromRgb(255, 112, 55),
        PetCharacter.PixelPikachu => Color.FromRgb(255, 218, 62),
        PetCharacter.PixelBulbasaur => Color.FromRgb(93, 205, 92),
        PetCharacter.PixelSquirtle => Color.FromRgb(78, 180, 255),
        PetCharacter.Ditto => Color.FromRgb(205, 119, 255),
        PetCharacter.Snorlax => Color.FromRgb(83, 190, 183),
        PetCharacter.Arceus => Color.FromRgb(255, 210, 70),
        PetCharacter.Dialga => Color.FromRgb(70, 195, 255),
        PetCharacter.Palkia => Color.FromRgb(236, 118, 220),
        PetCharacter.Giratina => Color.FromRgb(244, 82, 72),
        PetCharacter.Mewtwo => Color.FromRgb(185, 116, 255),
        PetCharacter.Lugia => Color.FromRgb(133, 199, 255),
        PetCharacter.Kyogre => Color.FromRgb(56, 142, 255),
        PetCharacter.Groudon => Color.FromRgb(255, 79, 54),
        PetCharacter.Rayquaza => Color.FromRgb(69, 222, 130),
        _ => Colors.White,
    };

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

}
