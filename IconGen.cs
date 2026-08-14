using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace TokenNotchWin;

/// <summary>
/// One-off icon baker: draws Clawd throwing a victory sign and packs it into a
/// multi-resolution .ico, so the tray/taskbar icon matches the widget instead
/// of the generic system default. Not part of the running app — invoked via
/// `TokenNotchWin.exe --gen-icon`.
/// </summary>
internal static class IconGen
{
    private static readonly Color Body = Color.FromArgb(215, 119, 87);
    private static readonly Color Dark = Color.FromArgb(20, 12, 10);

    public static void Run(string outDir)
    {
        Directory.CreateDirectory(outDir);

        var pngPaths = new List<(int size, string path)>();
        foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            var path = Path.Combine(outDir, $"preview_{size}.png");
            using var bmp = Draw(size);
            bmp.Save(path, ImageFormat.Png);
            pngPaths.Add((size, path));
        }

        WriteIco(pngPaths, Path.Combine(outDir, "app.ico"));
        Console.WriteLine($"wrote {pngPaths.Count} previews + app.ico to {outDir}");
    }

    private static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float s = size;
        using var bodyBrush = new SolidBrush(Body);
        using var darkBrush = new SolidBrush(Dark);
        using var eyeBrush = new SolidBrush(Color.Black);

        // Body: a rounded square sitting in the lower two-thirds, leaving
        // headroom above for the raised claws.
        var bodyRect = new RectangleF(s * 0.14f, s * 0.40f, s * 0.72f, s * 0.50f);
        FillRounded(g, bodyBrush, bodyRect, s * 0.14f);

        // Legs — three short stubs under the body, crab-style.
        float legW = s * 0.07f, legH = s * 0.09f, legY = bodyRect.Bottom - s * 0.015f;
        foreach (var fx in new[] { 0.24f, 0.5f, 0.76f })
        {
            g.FillRectangle(bodyBrush, s * fx - legW / 2, legY, legW, legH);
        }

        // Claws thrown up in a V, each arm ending in a round pincer — a
        // "lollipop" shape reads as a claw even at 16px, where a forked tip
        // just blurs into a smudge.
        using var armPen = new Pen(Body, s * 0.125f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var shoulderL = new PointF(bodyRect.Left + bodyRect.Width * 0.10f, bodyRect.Top + bodyRect.Height * 0.10f);
        var shoulderR = new PointF(bodyRect.Right - bodyRect.Width * 0.10f, bodyRect.Top + bodyRect.Height * 0.10f);
        var pincerL = new PointF(s * 0.135f, s * 0.145f);
        var pincerR = new PointF(s * 0.865f, s * 0.145f);
        g.DrawLine(armPen, shoulderL, pincerL);
        g.DrawLine(armPen, shoulderR, pincerR);

        float pincerR2 = s * 0.105f;
        DrawPincer(g, pincerL, pincerR2, dirX: 1f);  // bite faces the head
        DrawPincer(g, pincerR, pincerR2, dirX: -1f);

        // Happy eyes.
        float eyeW = bodyRect.Width * 0.16f, eyeH = bodyRect.Height * 0.30f;
        float eyeY = bodyRect.Top + bodyRect.Height * 0.26f;
        FillRounded(g, eyeBrush, new RectangleF(bodyRect.Left + bodyRect.Width * 0.20f, eyeY, eyeW, eyeH), eyeW * 0.4f);
        FillRounded(g, eyeBrush, new RectangleF(bodyRect.Right - bodyRect.Width * 0.20f - eyeW, eyeY, eyeW, eyeH), eyeW * 0.4f);

        _ = darkBrush; // reserved for a future outline pass
        return bmp;
    }

    /// A round pincer with a wedge clipped out of its inner edge — the "open
    /// claw" shape reads clearly even at 16px, where an actual forked tip
    /// would just blur into a smudge.
    private static void DrawPincer(Graphics g, PointF center, float r, float dirX)
    {
        using var wedge = new GraphicsPath();
        wedge.AddPolygon(new[]
        {
            new PointF(center.X + dirX * r * 0.15f, center.Y - r * 0.9f),
            new PointF(center.X + dirX * r * 1.4f, center.Y),
            new PointF(center.X + dirX * r * 0.15f, center.Y + r * 0.9f),
        });

        var saved = g.Save();
        using (var region = new Region(wedge))
            g.SetClip(region, CombineMode.Exclude);
        using (var brush = new SolidBrush(Body))
            g.FillEllipse(brush, center.X - r, center.Y - r, r * 2, r * 2);
        g.Restore(saved);
    }

    private static void FillRounded(Graphics g, Brush brush, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    /// Modern ICO directory entries can hold PNG-compressed images directly —
    /// no need for legacy uncompressed DIB encoding.
    private static void WriteIco(List<(int size, string path)> pngs, string icoPath)
    {
        using var fs = new FileStream(icoPath, FileMode.Create);
        using var w = new BinaryWriter(fs);

        w.Write((short)0); // reserved
        w.Write((short)1); // type: icon
        w.Write((short)pngs.Count);

        var offset = 6 + 16 * pngs.Count;
        var blobs = new List<byte[]>();
        foreach (var (size, path) in pngs)
        {
            var bytes = File.ReadAllBytes(path);
            blobs.Add(bytes);

            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); // palette
            w.Write((byte)0); // reserved
            w.Write((short)1); // color planes
            w.Write((short)32); // bits per pixel
            w.Write(bytes.Length);
            w.Write(offset);
            offset += bytes.Length;
        }
        foreach (var blob in blobs) w.Write(blob);
    }
}
