using System.Drawing.Drawing2D;

namespace MadNegar;

/// <summary>A simple owner-painted checkbox so its box and background follow dark/light themes.</summary>
internal sealed class ThemedCheckBox : CheckBox
{
    public ThemedCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = ThemeManager.ActivePalette;
        var background = Parent?.BackColor ?? palette.Surface;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var backgroundBrush = new SolidBrush(background))
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

        const int boxSize = 14;
        var box = new Rectangle(1, Math.Max(1, (Height - boxSize) / 2), boxSize, boxSize);
        using (var boxBrush = new SolidBrush(Checked ? palette.Primary : palette.Input))
            e.Graphics.FillRectangle(boxBrush, box);
        using (var borderPen = new Pen(Checked ? palette.Primary : palette.Border))
            e.Graphics.DrawRectangle(borderPen, box);

        if (Checked)
        {
            using var tickPen = new Pen(Color.White, 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLines(tickPen,
            [
                new Point(box.Left + 3, box.Top + 7),
                new Point(box.Left + 6, box.Top + 10),
                new Point(box.Left + 11, box.Top + 4)
            ]);
        }

        var textBounds = new Rectangle(box.Right + 7, 0, Math.Max(0, Width - box.Right - 7), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : palette.MutedText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        if (Focused && ShowFocusCues)
        {
            var focusBounds = Rectangle.Inflate(textBounds, -1, -2);
            ControlPaint.DrawFocusRectangle(e.Graphics, focusBounds, ForeColor, background);
        }
    }
}
