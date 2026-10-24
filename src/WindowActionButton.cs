using System.Drawing.Drawing2D;

namespace MadNegar;

/// <summary>Compact title-bar action button with crisp vector-drawn window/tray symbols.</summary>
internal sealed class WindowActionButton : Button
{
    public WindowActionButton()
    {
        Text = string.Empty;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var palette = ThemeManager.ActivePalette;
        var isClose = string.Equals(Tag as string, "window-close", StringComparison.OrdinalIgnoreCase);
        var glyphColor = isClose && ClientRectangle.Contains(PointToClient(Cursor.Position))
            ? Color.White
            : palette.Text;
        using var pen = new Pen(glyphColor, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float cx = ClientRectangle.Left + ClientRectangle.Width / 2f;
        float cy = ClientRectangle.Top + ClientRectangle.Height / 2f;
        var tag = Tag as string ?? string.Empty;

        if (string.Equals(tag, "window-minimize", StringComparison.OrdinalIgnoreCase))
        {
            g.DrawLine(pen, cx - 5.5f, cy + 3f, cx + 5.5f, cy + 3f);
        }
        else if (string.Equals(tag, "window-tray", StringComparison.OrdinalIgnoreCase))
        {
            // A downward arrow landing in an open tray clearly signals "send to notification area".
            g.DrawLine(pen, cx, cy - 8f, cx, cy + 0.5f);
            g.DrawLine(pen, cx - 3.2f, cy - 2.5f, cx, cy + 0.8f);
            g.DrawLine(pen, cx + 3.2f, cy - 2.5f, cx, cy + 0.8f);
            g.DrawLine(pen, cx - 8f, cy + 1.5f, cx - 8f, cy + 6.5f);
            g.DrawLine(pen, cx - 8f, cy + 6.5f, cx + 8f, cy + 6.5f);
            g.DrawLine(pen, cx + 8f, cy + 6.5f, cx + 8f, cy + 1.5f);
            g.DrawLine(pen, cx - 4f, cy + 3.5f, cx + 4f, cy + 3.5f);
        }
        else if (string.Equals(tag, "window-close", StringComparison.OrdinalIgnoreCase))
        {
            g.DrawLine(pen, cx - 4.5f, cy - 4.5f, cx + 4.5f, cy + 4.5f);
            g.DrawLine(pen, cx + 4.5f, cy - 4.5f, cx - 4.5f, cy + 4.5f);
        }
    }
}
