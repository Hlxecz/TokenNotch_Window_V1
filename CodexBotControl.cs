using System.Windows;
using System.Windows.Media;

namespace TokenNotchWin;

/// <summary>
/// A tiny teal robot sharing Clawd's mood language. The official Codex pet
/// spritesheet is a WebP that WPF can't decode, so the widget draws its own.
/// </summary>
public sealed class CodexBotControl : FrameworkElement
{
    public static readonly DependencyProperty MoodProperty =
        DependencyProperty.Register(nameof(Mood), typeof(Mood), typeof(CodexBotControl),
            new FrameworkPropertyMetadata(TokenNotchWin.Mood.Happy, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScaleProperty =
        DependencyProperty.Register(nameof(Scale), typeof(double), typeof(CodexBotControl),
            new FrameworkPropertyMetadata(1.6, FrameworkPropertyMetadataOptions.AffectsMeasure
                                             | FrameworkPropertyMetadataOptions.AffectsRender));

    public Mood Mood
    {
        get => (Mood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public double Time { get; set; }

    private static readonly Brush BodyBrush = Frozen(Color.FromRgb(89, 199, 184));
    private static readonly Brush DarkBrush = Frozen(Colors.Black);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    protected override Size MeasureOverride(Size availableSize) => new(24 * Scale, 20 * Scale);

    protected override void OnRender(DrawingContext dc)
    {
        var t = Time;
        var s = Scale;
        var speed = Mood switch
        {
            Mood.Happy => 4.0,
            Mood.Worried => 9.0,
            Mood.Critical => 13.0,
            _ => 0.0,
        };
        var bob = Mood == Mood.Sleeping ? 0 : Math.Sin(t * speed) * 1.0 * s;
        var tremble = Mood == Mood.Critical ? Math.Sin(t * 40) * 0.8 * s : 0;

        var cx = RenderSize.Width / 2 + tremble;
        var cy = RenderSize.Height / 2 + bob;

        // Antenna with a tip that pulses in the mood colour.
        dc.DrawRectangle(BodyBrush, null, new Rect(cx - 0.6 * s, cy - 9 * s, 1.2 * s, 3.5 * s));
        var tip = Format.StatusBrush(Mood switch
        {
            Mood.Happy => 80.0,
            Mood.Worried => 35.0,
            Mood.Critical => 10.0,
            _ => (double?)null,
        });
        dc.DrawEllipse(tip, null, new Point(cx, cy - 9.6 * s), 1.5 * s, 1.5 * s);

        // Head
        dc.DrawRoundedRectangle(BodyBrush, null,
            new Rect(cx - 7 * s, cy - 5.5 * s, 14 * s, 11 * s), 4 * s, 4 * s);

        // Eyes — a blink flattens them, sleeping keeps them shut.
        var blinking = Mood == Mood.Sleeping
                       || Math.Sin(t * 1.9 + 1) * Math.Sin(t * 2.7) > 0.93;
        foreach (var sign in new[] { -1.0, 1.0 })
        {
            var h = blinking ? 0.8 * s : 3.6 * s;
            dc.DrawRoundedRectangle(DarkBrush, null,
                new Rect(cx + sign * 3.2 * s - 1.2 * s, cy - h / 2, 2.4 * s, h), 1.0 * s, 1.0 * s);
        }

        // Mouth pinches shut when the quota gets tight.
        var mouthWidth = (Mood == Mood.Critical ? 3.0 : 5.0) * s;
        dc.DrawRoundedRectangle(DarkBrush, null,
            new Rect(cx - mouthWidth / 2, cy + 3.0 * s, mouthWidth, 1.4 * s), 0.7 * s, 0.7 * s);
    }
}
