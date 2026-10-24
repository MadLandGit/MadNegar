using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace MadNegar;

/// <summary>Owner-painted moon/sun button with a glyph centered independent of font metrics.</summary>
internal sealed class ThemeToggleButton : Button
{
    private bool _isDarkMode;

    public ThemeToggleButton()
    {
        Text = string.Empty;
        TextAlign = ContentAlignment.MiddleCenter;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode == value) return;
            _isDarkMode = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var palette = ThemeManager.ActivePalette;
        float cx = ClientRectangle.Left + ClientRectangle.Width / 2f;
        float cy = ClientRectangle.Top + ClientRectangle.Height / 2f;

        if (_isDarkMode)
        {
            // Sun: warm neutral gold, centered on the actual client rectangle.
            using var sunBrush = new SolidBrush(Color.FromArgb(232, 202, 123));
            using var rayPen = new Pen(Color.FromArgb(232, 202, 123), 1.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.FillEllipse(sunBrush, cx - 4.2f, cy - 4.2f, 8.4f, 8.4f);
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Math.PI / 4d;
                var inner = 7.0f;
                var outer = 9.5f;
                g.DrawLine(rayPen,
                    cx + (float)Math.Cos(angle) * inner,
                    cy + (float)Math.Sin(angle) * inner,
                    cx + (float)Math.Cos(angle) * outer,
                    cy + (float)Math.Sin(angle) * outer);
            }
        }
        else
        {
            // Crescent: overlap with the button surface, not the form background.
            using var moonBrush = new SolidBrush(palette.Text);
            using var cutoutBrush = new SolidBrush(BackColor);
            g.FillEllipse(moonBrush, cx - 7f, cy - 7f, 14f, 14f);
            g.FillEllipse(cutoutBrush, cx - 1.5f, cy - 9f, 12f, 12f);
        }
    }
}
