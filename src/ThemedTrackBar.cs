using System.Drawing.Drawing2D;

namespace MadNegar;

/// <summary>Owner-painted trackbar with a dark-mode-safe track, ticks, and thumb.</summary>
internal sealed class ThemedTrackBar : TrackBar
{
    public ThemedTrackBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TickStyle = System.Windows.Forms.TickStyle.BottomRight;
        ValueChanged += (_, _) => Invalidate();
        Scroll += (_, _) => Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = ThemeManager.ActivePalette;
        var background = Parent?.BackColor ?? palette.Surface;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var backgroundBrush = new SolidBrush(background))
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

        var left = 10;
        var right = Math.Max(left + 1, Width - 10);
        var trackY = Math.Clamp(11, 5, Math.Max(5, Height - 5));
        var track = new Rectangle(left, trackY - 2, right - left, 4);
        using (var trackBrush = new SolidBrush(palette.Border))
            e.Graphics.FillRectangle(trackBrush, track);

        var range = Math.Max(1, Maximum - Minimum);
        var fraction = Math.Clamp((Value - Minimum) / (double)range, 0d, 1d);
        var thumbX = left + (int)Math.Round((right - left) * fraction);
        using (var activeBrush = new SolidBrush(palette.Primary))
            e.Graphics.FillRectangle(activeBrush, new Rectangle(left, trackY - 2, Math.Max(0, thumbX - left), 4));

        if (TickStyle is System.Windows.Forms.TickStyle.BottomRight or System.Windows.Forms.TickStyle.Both)
        {
            using var tickPen = new Pen(palette.MutedText, 1F);
            var increment = Math.Max(1, TickFrequency);
            for (var value = Minimum; value <= Maximum; value += increment)
            {
                var tickX = left + (int)Math.Round((right - left) * ((value - Minimum) / (double)range));
                e.Graphics.DrawLine(tickPen, tickX, trackY + 5, tickX, Math.Min(Height - 3, trackY + 11));
            }
        }

        var thumb = new Rectangle(thumbX - 6, trackY - 9, 12, 22);
        using (var thumbPath = RoundedRectangle(thumb, 4))
        using (var thumbBrush = new SolidBrush(palette.Primary))
        using (var thumbBorder = new Pen(palette.PrimaryHover))
        {
            e.Graphics.FillPath(thumbBrush, thumbPath);
            e.Graphics.DrawPath(thumbBorder, thumbPath);
        }

        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2), palette.Text, background);
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
