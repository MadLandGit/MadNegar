using System.Drawing.Drawing2D;

namespace MadNegar;

/// <summary>ComboBox whose closed-state arrow area is painted using MadNegar's active palette.</summary>
internal sealed class ThemedComboBox : ComboBox
{
    private const int WmPaint = 0x000F;

    public ThemedComboBox()
    {
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WmPaint || !IsHandleCreated || Width < 8 || Height < 8 || DropDownStyle == ComboBoxStyle.Simple)
            return;

        try
        {
            var palette = ThemeManager.ActivePalette;
            using var graphics = Graphics.FromHwnd(Handle);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var arrowWidth = Math.Min(Math.Max(SystemInformation.VerticalScrollBarWidth, 16), Width / 3);
            var arrowArea = new Rectangle(Width - arrowWidth - 1, 1, arrowWidth, Height - 2);
            using (var fill = new SolidBrush(palette.Input))
                graphics.FillRectangle(fill, arrowArea);
            using (var border = new Pen(palette.Border))
            {
                graphics.DrawLine(border, arrowArea.Left, arrowArea.Top + 2, arrowArea.Left, arrowArea.Bottom - 2);
                graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }

            var cx = arrowArea.Left + arrowArea.Width / 2;
            var cy = arrowArea.Top + arrowArea.Height / 2;
            var points = new[] { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 2) };
            using var arrow = new SolidBrush(palette.Text);
            graphics.FillPolygon(arrow, points);
        }
        catch
        {
            // Native painting should remain a safe fallback if custom painting is unavailable.
        }
    }
}
