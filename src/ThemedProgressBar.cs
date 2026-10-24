using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace MadNegar;

/// <summary>A small rounded progress bar whose track and fill follow MadNegar's theme.</summary>
internal sealed class ThemedProgressBar : ProgressBar
{
    private readonly System.Windows.Forms.Timer _marqueeTimer = new() { Interval = 35 };
    private int _marqueeOffset;

    public ThemedProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _marqueeTimer.Tick += (_, _) =>
        {
            _marqueeOffset += 7;
            if (_marqueeOffset > Math.Max(Width, 1)) _marqueeOffset = 0;
            Invalidate();
        };
        Disposed += (_, _) => _marqueeTimer.Dispose();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new ProgressBarStyle Style
    {
        get => base.Style;
        set
        {
            base.Style = value;
            if (value == ProgressBarStyle.Marquee && IsHandleCreated)
                _marqueeTimer.Start();
            else
                _marqueeTimer.Stop();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new int Value
    {
        get => base.Value;
        set
        {
            base.Value = value;
            Invalidate();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (base.Style == ProgressBarStyle.Marquee) _marqueeTimer.Start();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _marqueeTimer.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var palette = ThemeManager.ActivePalette;
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        if (bounds.Width < 2 || bounds.Height < 2) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var trackPath = RoundedRectangle(bounds, Math.Min(6, bounds.Height / 2)))
        using (var trackBrush = new SolidBrush(palette.Input))
        using (var borderPen = new Pen(palette.Border))
        {
            e.Graphics.FillPath(trackBrush, trackPath);
            e.Graphics.DrawPath(borderPen, trackPath);
        }

        var inner = Rectangle.Inflate(bounds, -2, -2);
        if (inner.Width < 2 || inner.Height < 2) return;

        if (base.Style == ProgressBarStyle.Marquee)
        {
            var chunkWidth = Math.Max(22, inner.Width / 3);
            var x = inner.Left + _marqueeOffset - chunkWidth;
            var chunk = new Rectangle(x, inner.Top, chunkWidth, inner.Height);
            var oldClip = e.Graphics.Clip;
            e.Graphics.SetClip(inner);
            using (var brush = new SolidBrush(palette.Primary))
                e.Graphics.FillRectangle(brush, chunk);
            e.Graphics.Clip = oldClip;
            oldClip.Dispose();
        }
        else
        {
            var range = Math.Max(1, Maximum - Minimum);
            var fraction = Math.Clamp((Value - Minimum) / (double)range, 0d, 1d);
            var width = (int)Math.Round(inner.Width * fraction);
            if (width > 0)
            {
                var fill = new Rectangle(inner.Left, inner.Top, width, inner.Height);
                using var fillPath = RoundedRectangle(fill, Math.Min(5, inner.Height / 2));
                using var fillBrush = new SolidBrush(palette.Primary);
                e.Graphics.FillPath(fillBrush, fillPath);
            }
        }
    }

    private static GraphicsPath RoundedRectangle(Rectangle rect, int radius)
    {
        radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        var path = new GraphicsPath();
        if (radius <= 1)
        {
            path.AddRectangle(rect);
            return path;
        }

        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
