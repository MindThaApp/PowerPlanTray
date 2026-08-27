using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using PowerPlanTray.Core.Models;

namespace PowerPlanTray;

internal static class DynamicTrayIconRenderer
{
    private const int Size = 32;

    public static Icon Render(TrayIconMode mode, double cpuLoad, string planName, Color? gaugeColor = null, double gaugeValue = 0)
    {
        using var bitmap = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Color tileColor = mode == TrayIconMode.Gauge && gaugeColor.HasValue
                ? gaugeColor.Value
                : Color.FromArgb(255, 24, 104, 183);
            using var background = new SolidBrush(tileColor);
            graphics.FillRoundedRectangle(background, new Rectangle(1, 1, 30, 30), 6);

            if (mode == TrayIconMode.Gauge)
                DrawGauge(graphics, gaugeValue);
            else if (mode == TrayIconMode.CpuBarChart)
                DrawBar(graphics, cpuLoad);
            else
                DrawText(graphics, mode == TrayIconMode.CpuPercentText
                    ? Math.Round(Math.Clamp(cpuLoad, 0, 100)).ToString("0")
                    : Abbreviate(planName));
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    internal static string Abbreviate(string? name)
    {
        string[] words = (name ?? string.Empty).Split(
            new[] { ' ', '\t', '-', '_', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
            return string.Concat(words.Take(3).Select(word => char.ToUpperInvariant(word[0])));
        string word = words.FirstOrDefault() ?? "?";
        return word[..Math.Min(3, word.Length)].ToUpperInvariant();
    }

    private static void DrawText(Graphics graphics, string text)
    {
        float size = text.Length >= 3 ? 13 : 16;
        using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(text, font, brush, new RectangleF(0, 0, Size, Size), format);
    }

    private static void DrawBar(Graphics graphics, double cpuLoad)
    {
        using var border = new Pen(Color.White, 2);
        graphics.DrawRectangle(border, 7, 5, 18, 22);
        int height = (int)Math.Round(18 * Math.Clamp(cpuLoad, 0, 100) / 100d);
        using var fill = new SolidBrush(cpuLoad >= 80 ? Color.FromArgb(255, 255, 193, 7) : Color.White);
        graphics.FillRectangle(fill, 10, 24 - height, 13, height);
    }

    // Speedometer-style 270-degree arc open at the bottom (a 90-degree gap centered on
    // straight down). GDI+ angles here are measured clockwise from the positive x-axis with
    // the y-axis pointing down, matching AddArc's convention, so the same start/sweep values
    // used for DrawArc also work directly for the needle-angle trig below.
    private static void DrawGauge(Graphics graphics, double value)
    {
        const float centerX = Size / 2f;
        const float centerY = Size / 2f + 1f;
        const float radius = 11f;
        const float startAngleDegrees = 135f;
        const float sweepAngleDegrees = 270f;

        using (var arcPen = new Pen(Color.White, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            graphics.DrawArc(arcPen, centerX - radius, centerY - radius, radius * 2, radius * 2, startAngleDegrees, sweepAngleDegrees);

        double t = Math.Clamp(value, 0, 100) / 100d;
        double needleAngleRadians = (startAngleDegrees + sweepAngleDegrees * t) * Math.PI / 180d;
        const float needleLength = radius - 1.5f;
        float needleX = centerX + needleLength * (float)Math.Cos(needleAngleRadians);
        float needleY = centerY + needleLength * (float)Math.Sin(needleAngleRadians);
        const float needleHalfWidth = 2f;
        float perpendicularX = -(float)Math.Sin(needleAngleRadians) * needleHalfWidth;
        float perpendicularY = (float)Math.Cos(needleAngleRadians) * needleHalfWidth;
        PointF[] needleCorners =
        {
            new(centerX + perpendicularX, centerY + perpendicularY),
            new(needleX + perpendicularX, needleY + perpendicularY),
            new(needleX - perpendicularX, needleY - perpendicularY),
            new(centerX - perpendicularX, centerY - perpendicularY)
        };
        using var needleBrush = new SolidBrush(Color.White);
        graphics.FillPolygon(needleBrush, needleCorners);
    }

    private static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        int diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
