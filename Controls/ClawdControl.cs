using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TokenNotchWin;

/// <summary>
/// The animated Clawd. Motion is a pure function of elapsed time, so the host
/// only has to invalidate on a timer — no per-frame state to keep in sync.
/// </summary>
public sealed class ClawdControl : FrameworkElement
{
    public static readonly DependencyProperty MoodProperty =
        DependencyProperty.Register(nameof(Mood), typeof(Mood), typeof(ClawdControl),
            new FrameworkPropertyMetadata(TokenNotchWin.Mood.Happy, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FacingRightProperty =
        DependencyProperty.Register(nameof(FacingRight), typeof(bool), typeof(ClawdControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScaleProperty =
        DependencyProperty.Register(nameof(Scale), typeof(double), typeof(ClawdControl),
            new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsMeasure
                                              | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GrabbedProperty =
        DependencyProperty.Register(nameof(Grabbed), typeof(bool), typeof(ClawdControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RestingProperty =
        DependencyProperty.Register(nameof(Resting), typeof(bool), typeof(ClawdControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NeglectFractionProperty =
        DependencyProperty.Register(nameof(NeglectFraction), typeof(double), typeof(ClawdControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Mood Mood
    {
        get => (Mood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    public bool FacingRight
    {
        get => (bool)GetValue(FacingRightProperty);
        set => SetValue(FacingRightProperty, value);
    }

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    /// Picked up by the pointer: Clawd dangles and thrashes until let go.
    public bool Grabbed
    {
        get => (bool)GetValue(GrabbedProperty);
        set => SetValue(GrabbedProperty, value);
    }

    /// Left alone for a while: Clawd sits still instead of patrolling, with
    /// just enough motion (breathing, an occasional glance) to read as alive
    /// rather than frozen.
    public bool Resting
    {
        get => (bool)GetValue(RestingProperty);
        set => SetValue(RestingProperty, value);
    }

    /// 0 at "just sat down", climbing toward (but never reaching) 1 the
    /// longer Resting goes uninterrupted — the host owns the clock and just
    /// hands over how neglected Clawd should look this frame.
    public double NeglectFraction
    {
        get => (double)GetValue(NeglectFractionProperty);
        set => SetValue(NeglectFractionProperty, value);
    }

    /// Seconds since the widget started; the host advances this each frame.
    public double Time { get; set; }

    // Terminal cells are ~twice as tall as wide, so a quadrant "pixel" is 1:2 —
    // rendering them square squashes the sprite flat.
    private double PxW => 1.5 * Scale;
    private double PxH => 3.0 * Scale;

    private double SpriteWidth => ClawdSprite.GridWidth * PxW;
    private double SpriteHeight => ClawdSprite.GridHeight * PxH;

    private static readonly Brush BodyBrush = Freeze(new SolidColorBrush(ClawdSprite.BodyColor));
    private static readonly Brush DarkBrush = Freeze(new SolidColorBrush(Color.FromRgb(20, 12, 10)));
    private static readonly Brush EyeBrush = Freeze(new SolidColorBrush(Colors.Black));
    private static readonly Brush ShadowBrush = Freeze(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)));
    private static readonly Brush SweatBrush = Freeze(new SolidColorBrush(Color.FromRgb(102, 179, 255)));
    private static readonly Typeface Typeface = new("Segoe UI");

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// Fades the body colour toward black; capped at 85% so there's always
    /// enough colour left to read as "sitting there", not "gone".
    private static Brush TannedBrush(double neglect)
    {
        var amount = Math.Clamp(neglect, 0, 1) * 0.85;
        var c = ClawdSprite.BodyColor;
        byte Darken(byte channel) => (byte)(channel * (1 - amount));
        var brush = new SolidColorBrush(Color.FromRgb(Darken(c.R), Darken(c.G), Darken(c.B)));
        brush.Freeze();
        return brush;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(SpriteWidth + 18 * Scale / 3, SpriteHeight + 14 * Scale / 3);

    protected override void OnRender(DrawingContext dc)
    {
        // Aliased edges keep the pixel art crisp instead of blurring at
        // fractional device pixels.
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        var t = Time;
        var originX = (RenderSize.Width - SpriteWidth) / 2;
        var standY = RenderSize.Height - SpriteHeight - Bob(t);
        // Feet are hidden while lounging, so drop the body by exactly their
        // height — otherwise the tucked-in crab appears to hover.
        var feetHeight = SpriteHeight * 2 / 6;
        var originY = Resting ? standY + feetHeight : standY;
        var groundY = standY + SpriteHeight;

        if (!Grabbed) DrawShadow(dc, t);
        // Behind the body, so the pole reads as planted rather than held.
        // Bob() lifts the sprite but not the stake — the ground doesn't breathe.
        if (Resting) DrawParasol(dc, t, originX, groundY + Bob(t));

        dc.PushTransform(new TranslateTransform(originX + Tremble(t), originY));
        if (Grabbed)
        {
            // Dangling from the pointer: swing about the point it's held by.
            dc.PushTransform(new RotateTransform(
                Math.Sin(t * 7.5) * 13, SpriteWidth / 2, -2 * Scale / 3));
        }
        if (!FacingRight)
        {
            // Mirror around the sprite's own centre so it turns in place.
            dc.PushTransform(new ScaleTransform(-1, 1, SpriteWidth / 2, 0));
        }

        DrawBody(dc);
        DrawEyes(dc, t);

        if (!FacingRight) dc.Pop();
        if (Grabbed) dc.Pop();
        dc.Pop();

        DrawEffects(dc, t, originX, originY);
    }

    private void DrawBody(DrawingContext dc)
    {
        // Left alone under the parasol too long: Clawd tans toward black,
        // stopping just short of it — a wordless "come touch me" rather than
        // a full blackout, which would just look broken.
        var bodyBrush = NeglectFraction > 0.001 ? TannedBrush(NeglectFraction) : BodyBrush;

        var grid = ClawdSprite.Grid(CurrentPose(Time));
        for (var row = 0; row < ClawdSprite.GridHeight; row++)
        {
            // Feet tucked under while lounging — no legs showing reads as
            // "sitting down" far more clearly than any change of pose.
            if (Resting && row >= 4) continue;

            // The last sprite row is Clawd's feet — kicking them out of step
            // with the claws is what sells "held up in the air".
            var kick = Grabbed && row >= 4 ? Math.Sin(Time * 19 + row) * 2.2 * Scale / 3 : 0;

            for (var col = 0; col < ClawdSprite.GridWidth; col++)
            {
                var cell = grid[row, col];
                if (cell == ClawdSprite.Pixel.Clear) continue;
                // Left and right feet kick in opposite directions.
                var legSwing = kick * (col < ClawdSprite.GridWidth / 2 ? 1 : -1);
                // The +0.4 overlap hides hairline seams between adjacent pixels.
                dc.DrawRectangle(
                    cell == ClawdSprite.Pixel.Body ? bodyBrush : DarkBrush,
                    null,
                    new Rect(col * PxW + legSwing, row * PxH, PxW + 0.4, PxH + 0.4));
            }
        }
    }

    /// Round eyes at face height, glancing the way Clawd walks; a blink
    /// flattens them into a line.
    private void DrawEyes(DrawingContext dc, double t)
    {
        var glance = CurrentPose(t) switch
        {
            ClawdSprite.Pose.LookLeft => -1.2 * Scale / 3,
            ClawdSprite.Pose.LookRight => 1.2 * Scale / 3,
            _ => 0,
        };
        var closed = EyesClosed(t);
        var centerY = 1.8 * PxH;

        foreach (var centerCol in new[] { 5.5, 12.5 })
        {
            var centerX = centerCol * PxW + glance;
            var rect = closed
                ? new Rect(centerX - 1.2 * Scale / 3 * 2, centerY - 0.5 * Scale / 3 * 2,
                    2.4 * Scale / 3 * 2, 1.0 * Scale / 3 * 2)
                : new Rect(centerX - 1.0 * Scale / 3 * 2, centerY - 1.7 * Scale / 3 * 2,
                    2.0 * Scale / 3 * 2, 3.4 * Scale / 3 * 2);
            dc.DrawRoundedRectangle(EyeBrush, null, rect, 0.9 * Scale / 3, 0.9 * Scale / 3);
        }
    }

    /// A squashed ellipse that shrinks as Clawd hops — sells the bounce as
    /// vertical motion rather than the whole sprite sliding.
    private void DrawShadow(DrawingContext dc, double t)
    {
        var lift = Bob(t);
        var shrink = 1 - Math.Min(lift / (6 * Scale / 3), 0.45);
        var cx = RenderSize.Width / 2 + Tremble(t);
        var cy = RenderSize.Height - 1.5;
        dc.DrawEllipse(ShadowBrush, null, new Point(cx, cy),
            SpriteWidth * 0.34 * shrink, 2.2 * Scale / 3);
    }

    private void DrawEffects(DrawingContext dc, double t, double originX, double originY)
    {
        if (Grabbed)
        {
            DrawFluster(dc, t, originX, originY);
            return;
        }
        // Sweat and sparkles both belong to the walking states; under the
        // parasol they'd fight the calm — except once it's tanned nearly
        // all the way, which is the one thing worth interrupting the calm for.
        if (Resting)
        {
            if (NeglectFraction >= 0.92) DrawPleaseTouch(dc, t, originX, originY);
            return;
        }

        switch (Mood)
        {
            case Mood.Worried:
            case Mood.Critical:
                DrawSweat(dc, t, originX, originY);
                break;
            case Mood.Sleeping:
                DrawSleepZs(dc, t, originX, originY);
                break;
            case Mood.Happy:
                DrawSparkle(dc, t, originX, originY, 0);
                DrawSparkle(dc, t, originX, originY, 0.55);
                break;
        }
    }

    private static readonly Brush ParasolCream = Freeze(new SolidColorBrush(Color.FromRgb(247, 238, 224)));
    private static readonly Brush ParasolRed = Freeze(new SolidColorBrush(Color.FromRgb(226, 96, 80)));
    private static readonly Brush PoleBrush = Freeze(new SolidColorBrush(Color.FromRgb(150, 108, 78)));

    /// A striped beach parasol staked into the ground beside Clawd and tilted
    /// so the canopy leans over it. Drawn in a rotated frame about the stake
    /// point, which also makes the lazy sway a single extra degree of tilt.
    private void DrawParasol(DrawingContext dc, double t, double originX, double groundY)
    {
        var unit = Scale / 3;
        var stakeX = originX + SpriteWidth * 0.06;
        var poleLength = SpriteHeight * 1.02;
        var radius = SpriteWidth * 0.52;

        // Positive angle tips the canopy clockwise — i.e. to the right, over
        // the crab sitting beside the stake.
        dc.PushTransform(new RotateTransform(16 + Math.Sin(t * 0.7) * 2.0, stakeX, groundY));

        var poleTopY = groundY - poleLength;
        dc.DrawRectangle(PoleBrush, null,
            new Rect(stakeX - 0.9 * unit, poleTopY, 1.8 * unit, poleLength));

        var center = new Point(stakeX, poleTopY);
        const int panels = 6;
        for (var i = 0; i < panels; i++)
        {
            dc.DrawGeometry(i % 2 == 0 ? ParasolRed : ParasolCream, null,
                Wedge(center, radius, 180 + i * (180.0 / panels), 180.0 / panels));
        }

        // Knob at the apex, and a soft rim line so the dome has an edge.
        dc.DrawEllipse(PoleBrush, null, new Point(stakeX, poleTopY - radius - 0.6 * unit), 1.2 * unit, 1.6 * unit);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)), 1.0 * unit),
            new Point(stakeX - radius, poleTopY), new Point(stakeX + radius, poleTopY));

        dc.Pop();
    }

    /// Pie slice of the canopy: 180°–360° sweeps over the top, since Y grows
    /// downward in device space.
    private static Geometry Wedge(Point center, double radius, double startDeg, double sweepDeg)
    {
        Point OnArc(double deg) => new(
            center.X + radius * Math.Cos(deg * Math.PI / 180),
            center.Y + radius * Math.Sin(deg * Math.PI / 180));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(center, true, true);
            ctx.LineTo(OnArc(startDeg), true, false);
            ctx.ArcTo(OnArc(startDeg + sweepDeg), new Size(radius, radius), 0, false,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    /// Little motion ticks either side of the head, alternating so the
    /// thrashing reads even in a still frame.
    private void DrawFluster(DrawingContext dc, double t, double originX, double originY)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1.4 * Scale / 3);
        pen.Freeze();
        var swing = Math.Sin(t * 16);

        for (var side = -1; side <= 1; side += 2)
        {
            var lean = side * swing > 0 ? 1.0 : 0.45;
            var x = originX + SpriteWidth / 2 + side * (SpriteWidth * 0.62);
            var y = originY + SpriteHeight * 0.22;
            for (var i = 0; i < 2; i++)
            {
                var spread = (2.5 + i * 3.0) * Scale / 3 * lean;
                dc.DrawLine(pen,
                    new Point(x + side * spread, y - spread * 0.55),
                    new Point(x + side * (spread + 2.4 * Scale / 3), y - spread * 0.95));
            }
        }
    }

    private void DrawSweat(DrawingContext dc, double t, double originX, double originY)
    {
        var speed = Mood == Mood.Critical ? 3.5 : 2.0;
        var drip = Mod1(t * speed);
        var x = originX + SpriteWidth * 0.86;
        var y = originY - 2 * Scale / 3 + drip * 6 * Scale / 3;
        dc.DrawEllipse(SweatBrush, null, new Point(x, y), 1.4 * Scale / 3, 1.8 * Scale / 3);
    }

    private void DrawSleepZs(DrawingContext dc, double t, double originX, double originY)
    {
        for (var i = 0; i < 2; i++)
        {
            var phase = Mod1(t / 2.2 + i * 0.5);
            var brush = new SolidColorBrush(Color.FromArgb((byte)(220 * (1 - phase)), 255, 255, 255));
            brush.Freeze();
            var zed = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface, 5.5 * Scale / 3 * (1 + phase * 0.6), brush, 1.0);
            dc.DrawText(zed, new Point(
                originX + SpriteWidth * 0.8 + phase * 3 * Scale / 3,
                originY - 3 * Scale / 3 - phase * 9 * Scale / 3));
        }
    }

    private void DrawSparkle(DrawingContext dc, double t, double originX, double originY, double offset)
    {
        var phase = Mod1(t / 2.4 + offset);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(230 * (1 - phase)), 255, 217, 102));
        brush.Freeze();
        var x = originX + SpriteWidth / 2 + Math.Sin((phase + offset) * 2 * Math.PI) * 12 * Scale / 3;
        var y = originY - 3 * Scale / 3 - phase * 9 * Scale / 3;
        var size = 2.0 * Scale / 3;
        // Four-point star drawn as two crossed diamonds.
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x, y - size), true, true);
            ctx.LineTo(new Point(x + size * 0.32, y - size * 0.32), true, false);
            ctx.LineTo(new Point(x + size, y), true, false);
            ctx.LineTo(new Point(x + size * 0.32, y + size * 0.32), true, false);
            ctx.LineTo(new Point(x, y + size), true, false);
            ctx.LineTo(new Point(x - size * 0.32, y + size * 0.32), true, false);
            ctx.LineTo(new Point(x - size, y), true, false);
            ctx.LineTo(new Point(x - size * 0.32, y - size * 0.32), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    /// A slow pulsing heart once Clawd has tanned nearly to the limit — the
    /// wordless cue that a touch is what resets it, not more waiting.
    private void DrawPleaseTouch(DrawingContext dc, double t, double originX, double originY)
    {
        var pulse = 0.5 + 0.5 * Math.Sin(t * 2.4);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(150 + 100 * pulse), 255, 138, 158));
        brush.Freeze();
        var heart = new FormattedText("♥", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface, (6.0 + pulse * 1.2) * Scale / 3, brush, 1.0);
        dc.DrawText(heart, new Point(
            originX + SpriteWidth * 0.5 - heart.Width / 2,
            originY - 9 * Scale / 3 - pulse * 1.5 * Scale / 3));
    }

    // MARK: Motion

    private double PatrolPeriod => Mood switch
    {
        Mood.Happy => 6.0,
        Mood.Worried => 3.5,
        Mood.Critical => 1.6, // frantic dashing
        _ => 1.0,
    };

    /// -1..1 triangle wave: Clawd patrols sideways, as crabs do.
    private double PatrolPhase(double t) => Math.Abs(Mod1(t / PatrolPeriod) * 2 - 1) * 2 - 1;

    private ClawdSprite.Pose CurrentPose(double t)
    {
        // Claws thrash far faster than any walking pose changes.
        if (Grabbed) return Math.Sin(t * 16) > 0 ? ClawdSprite.Pose.ArmsUp : ClawdSprite.Pose.Standing;
        if (Mood == Mood.Sleeping) return ClawdSprite.Pose.Standing;
        // A slow, deliberate look side to side — enough to read as "awake and
        // watching" without the bustle of the walk cycle.
        if (Resting) return Mod1(t / 9.0) < 0.5 ? ClawdSprite.Pose.LookLeft : ClawdSprite.Pose.LookRight;
        // Claws go up briefly at each turnaround; in critical mood the short
        // period turns this into panicked flailing.
        if (Math.Abs(PatrolPhase(t)) > 0.88) return ClawdSprite.Pose.ArmsUp;
        return FacingRight ? ClawdSprite.Pose.LookRight : ClawdSprite.Pose.LookLeft;
    }

    private bool EyesClosed(double t)
    {
        if (Grabbed) return false; // too startled to blink
        if (Mood == Mood.Sleeping) return true;
        // Two detuned sines make blinks land irregularly instead of on a beat.
        return Math.Sin(t * 1.7) * Math.Sin(t * 2.3) > 0.93;
    }

    private double Bob(double t)
    {
        if (Grabbed) return 0; // the pointer, not the ground, sets its height
        if (Mood == Mood.Sleeping) return 0;
        // A slow, shallow breathing bob rather than the bouncy walk cycle.
        if (Resting) return (0.5 + 0.5 * Math.Sin(t * 1.3)) * 1.1 * Scale / 3;
        var (frequency, amplitude) = Mood switch
        {
            Mood.Happy => (6.0, 1.5),
            Mood.Worried => (11.0, 1.5),
            _ => (14.0, 2.4), // bouncy panic hops
        };
        return Math.Abs(Math.Sin(t * frequency)) * amplitude * Scale / 3;
    }

    private double Tremble(double t) =>
        Mood == Mood.Critical ? Math.Sin(t * 40) * 0.8 * Scale / 3 : 0;

    private static double Mod1(double value)
    {
        var r = value % 1.0;
        return r < 0 ? r + 1 : r;
    }
}
