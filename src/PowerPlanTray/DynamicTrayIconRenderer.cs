using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using PowerPlanTray.Core.Models;

namespace PowerPlanTray;

internal static class DynamicTrayIconRenderer
{
    private const int Size = 32;

    public static Icon Render(TrayIconMode mode, double cpuLoad, string planName)
    {
        using var bitmap = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var background = new SolidBrush(Color.FromArgb(255, 24, 104, 183));
            graphics.FillRoundedRectangle(background, new Rectangle(1, 1, 30, 30), 6);

            if (mode == TrayIconMode.CpuBarChart)
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
