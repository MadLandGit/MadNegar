namespace MadNegar;

/// <summary>TabControl that paints the unused strip to the right of the last tab with the active theme.</summary>
internal sealed class ThemedTabControl : TabControl
{
    private const int WmPaint = 0x000F;

    public ThemedTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(112, 30);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WmPaint || !IsHandleCreated || TabPages.Count == 0) return;

        try
        {
            var lastTab = GetTabRect(TabPages.Count - 1);
            if (lastTab.Right >= ClientSize.Width) return;
            var palette = ThemeManager.ActivePalette;
            using var graphics = Graphics.FromHwnd(Handle);
            using var brush = new SolidBrush(palette.Window);
            var stripHeight = Math.Min(ClientSize.Height, lastTab.Bottom + 2);
            graphics.FillRectangle(brush,
                new Rectangle(lastTab.Right, 0, ClientSize.Width - lastTab.Right, stripHeight));
        }
        catch
        {
            // Keep the standard WinForms rendering if owner painting fails.
        }
    }
}
