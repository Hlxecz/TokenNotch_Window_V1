using System.Windows.Media;

namespace TokenNotchWin;

/// <summary>
/// Faithful reproduction of the Clawd sprite shipped inside Claude Code's CLI.
/// The original is terminal art built from quadrant block characters, three
/// cells tall, with poses that only change the claw glyphs:
///
///      ▐▛███▜▌        ▗▟▛███▜▙▖
///     ▝▜█████▛▘        ▜█████▛      feet:  ▘▘ ▝▝
///
/// Each character decodes into a 2×2 quadrant grid, rendered as rectangles —
/// identical geometry to the terminal original.
/// </summary>
public static class ClawdSprite
{
    /// clawd_body in every Claude Code theme: rgb(215,119,87)
    public static readonly Color BodyColor = Color.FromRgb(215, 119, 87);

    public enum Pose { Standing, LookLeft, LookRight, ArmsUp }

    public enum Pixel : byte { Clear, Body, Dark }

    public const int GridWidth = 18;
    public const int GridHeight = 6;

    // Poses are immutable, and the widget re-renders every frame — building
    // them from the block-character strings each time was the bulk of the
    // per-frame cost, so they're decoded once.
    private static readonly Dictionary<Pose, Pixel[,]> Cache =
        Enum.GetValues<Pose>().ToDictionary(pose => pose, Build);

    public static Pixel[,] Grid(Pose pose) => Cache[pose];

    private static Pixel[,] Build(Pose pose)
    {
        var (r1L, r1R, r2L, r2R) = pose == Pose.ArmsUp
            ? ("▗▟", "▙▖", " ▜", "▛ ")
            : (" ▐", "▌", "▝▜", "▛▘");

        var rows = new[]
        {
            new[] { (r1L, false), ("█████", true), (r1R, false) },
            new[] { (r2L, false), ("█████", true), (r2R, false) },
            new[] { ("  ▘▘ ▝▝  ", false) },
        };

        var grid = new Pixel[GridHeight, GridWidth];

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var segments = rows[rowIndex];
            // Poses differ in cell width (standing is 8 cells, arms-up 9), so
            // centering each row keeps the sprite from hopping sideways when
            // the pose changes.
            var cells = segments.Sum(s => s.Item1.Length);
            var col = (GridWidth / 2 - cells) / 2;

            foreach (var (text, onBlack) in segments)
            {
                foreach (var ch in text)
                {
                    var q = Quads(ch);
                    var quadrants = new[]
                    {
                        (q.Tl, 0, 0), (q.Tr, 0, 1), (q.Bl, 1, 0), (q.Br, 1, 1),
                    };
                    foreach (var (filled, dr, dc) in quadrants)
                    {
                        var r = rowIndex * 2 + dr;
                        var c = col * 2 + dc;
                        if (r < 0 || r >= GridHeight || c < 0 || c >= GridWidth) continue;
                        grid[r, c] = filled ? Pixel.Body : (onBlack ? Pixel.Dark : Pixel.Clear);
                    }
                    col++;
                }
            }
        }

        return grid;
    }

    private static (bool Tl, bool Tr, bool Bl, bool Br) Quads(char c) => c switch
    {
        '█' => (true, true, true, true),
        '▛' => (true, true, true, false),
        '▜' => (true, true, false, true),
        '▙' => (true, false, true, true),
        '▟' => (false, true, true, true),
        '▐' => (false, true, false, true),
        '▌' => (true, false, true, false),
        '▀' => (true, true, false, false),
        '▄' => (false, false, true, true),
        '▘' => (true, false, false, false),
        '▝' => (false, true, false, false),
        '▖' => (false, false, true, false),
        '▗' => (false, false, false, true),
        _ => (false, false, false, false),
    };
}
