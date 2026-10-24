namespace MadNegar;

/// <summary>
/// Applies a consistent palette to WinForms controls. Combo boxes, tab headers and
/// model lists are owner-drawn so Windows' default light-theme renderer cannot leave
/// white surfaces inside the dark theme.
/// </summary>
internal static class ThemeManager
{
    internal sealed record Palette(
        Color Window,
        Color Surface,
        Color Input,
        Color Text,
        Color MutedText,
        Color Border,
        Color Primary,
        Color PrimaryHover,
        Color Danger,
        Color DangerHover);

    private static string _activeMode = "Light";
    public static Palette ActivePalette => Current(_activeMode);

    public static Palette Current(string? mode) => IsDark(mode)
        ? new Palette(
            // Neutral charcoal palette: intentionally avoids the blue-tinted navy look.
            Color.FromArgb(18, 18, 18),   // Window
            Color.FromArgb(29, 29, 29),   // Surface
            Color.FromArgb(38, 38, 38),   // Inputs
            Color.FromArgb(245, 245, 245),// Text
            Color.FromArgb(174, 174, 174),// Muted text
            Color.FromArgb(59, 59, 59),   // Borders
            Color.FromArgb(78, 78, 78),   // Primary button / selection
            Color.FromArgb(96, 96, 96),   // Primary hover
            Color.FromArgb(190, 54, 54),  // Danger
            Color.FromArgb(153, 37, 37))  // Danger hover
        : new Palette(
            Color.FromArgb(243, 246, 252),
            Color.White,
            Color.White,
            Color.FromArgb(30, 41, 59),
            Color.FromArgb(100, 116, 139),
            Color.FromArgb(203, 213, 225),
            Color.FromArgb(37, 99, 235),
            Color.FromArgb(29, 78, 216),
            Color.FromArgb(220, 38, 38),
            Color.FromArgb(185, 28, 28));

    public static bool IsDark(string? mode) =>
        string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMode(string? mode) => IsDark(mode) ? "Dark" : "Light";

    public static void Apply(Control root, string? mode)
    {
        _activeMode = NormalizeMode(mode);
        Apply(root, Current(_activeMode));
    }

    public static void ApplyControl(Control control, string? mode)
    {
        _activeMode = NormalizeMode(mode);
        ApplyOne(control, Current(_activeMode));
    }

    private static void Apply(Control root, Palette palette)
    {
        ApplyOne(root, palette);
        foreach (Control child in root.Controls)
            Apply(child, palette);
    }

    private static void ApplyOne(Control control, Palette palette)
    {
        switch (control)
        {
            case Form:
                control.BackColor = palette.Window;
                control.ForeColor = palette.Text;
                break;
            case TabControl tabs:
                tabs.BackColor = palette.Window;
                tabs.ForeColor = palette.Text;
                tabs.SizeMode = TabSizeMode.Fixed;
                tabs.ItemSize = new Size(112, 30);
                if (tabs.DrawMode != TabDrawMode.OwnerDrawFixed)
                    tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.DrawItem -= DrawTab;
                tabs.DrawItem += DrawTab;
                break;
            case TabPage:
                control.BackColor = palette.Surface;
                control.ForeColor = palette.Text;
                break;
            case Panel panel:
                panel.BackColor = string.Equals(panel.Tag as string, "separator", StringComparison.OrdinalIgnoreCase)
                    ? palette.Border
                    : string.Equals(panel.Tag as string, "titlebar", StringComparison.OrdinalIgnoreCase)
                        ? palette.Window
                        : palette.Surface;
                panel.ForeColor = palette.Text;
                break;
            case GroupBox:
                control.BackColor = palette.Surface;
                control.ForeColor = palette.Text;
                break;
            case Label label:
                label.BackColor = Color.Transparent;
                label.ForeColor = string.Equals(label.Tag as string, "muted", StringComparison.OrdinalIgnoreCase)
                    ? palette.MutedText
                    : palette.Text;
                break;
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                var buttonTag = button.Tag as string ?? string.Empty;
                var isWindowChromeButton = buttonTag.StartsWith("window-", StringComparison.OrdinalIgnoreCase);
                button.FlatAppearance.BorderSize = isWindowChromeButton ||
                    string.Equals(buttonTag, "primary", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(buttonTag, "danger", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                button.FlatAppearance.BorderColor = palette.Border;
                if (isWindowChromeButton)
                {
                    button.BackColor = palette.Window;
                    button.ForeColor = palette.Text;
                    button.FlatAppearance.MouseOverBackColor =
                        string.Equals(buttonTag, "window-close", StringComparison.OrdinalIgnoreCase)
                            ? palette.Danger
                            : palette.Input;
                }
                else if (string.Equals(buttonTag, "theme-toggle", StringComparison.OrdinalIgnoreCase))
                {
                    button.BackColor = palette.Surface;
                    button.ForeColor = palette.Text;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.BorderColor = palette.Border;
                    button.FlatAppearance.MouseOverBackColor = palette.Input;
                }
                else if (string.Equals(buttonTag, "nav-active", StringComparison.OrdinalIgnoreCase))
                {
                    button.BackColor = palette.Primary;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderSize = 0;
                    button.FlatAppearance.MouseOverBackColor = palette.PrimaryHover;
                }
                else if (string.Equals(buttonTag, "nav", StringComparison.OrdinalIgnoreCase))
                {
                    button.BackColor = palette.Window;
                    button.ForeColor = palette.MutedText;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.BorderColor = palette.Border;
                    button.FlatAppearance.MouseOverBackColor = palette.Input;
                }
                else if (string.Equals(buttonTag, "primary", StringComparison.OrdinalIgnoreCase))
                {
                    button.BackColor = palette.Primary;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.MouseOverBackColor = palette.PrimaryHover;
                }
                else if (string.Equals(buttonTag, "danger", StringComparison.OrdinalIgnoreCase))
                {
                    button.BackColor = palette.Danger;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.MouseOverBackColor = palette.DangerHover;
                }
                else
                {
                    button.BackColor = palette.Surface;
                    button.ForeColor = palette.Text;
                    button.FlatAppearance.MouseOverBackColor = palette.Input;
                }
                break;
            case ComboBox comboBox:
                comboBox.BackColor = palette.Input;
                comboBox.ForeColor = palette.Text;
                comboBox.FlatStyle = FlatStyle.Flat;
                if (comboBox.DrawMode != DrawMode.OwnerDrawFixed)
                    comboBox.DrawMode = DrawMode.OwnerDrawFixed;
                comboBox.ItemHeight = Math.Max(comboBox.ItemHeight, 21);
                comboBox.DrawItem -= DrawComboItem;
                comboBox.DrawItem += DrawComboItem;
                break;
            case TextBox textBox:
                textBox.BackColor = palette.Input;
                textBox.ForeColor = palette.Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ListView listView:
                listView.BackColor = palette.Input;
                listView.ForeColor = palette.Text;
                listView.OwnerDraw = true;
                listView.GridLines = false;
                listView.DrawColumnHeader -= DrawListColumnHeader;
                listView.DrawColumnHeader += DrawListColumnHeader;
                listView.DrawItem -= DrawListItem;
                listView.DrawItem += DrawListItem;
                listView.DrawSubItem -= DrawListSubItem;
                listView.DrawSubItem += DrawListSubItem;
                break;
            case CheckBox checkBox:
                checkBox.BackColor = Color.Transparent;
                checkBox.ForeColor = palette.Text;
                break;
            case TrackBar trackBar:
                trackBar.BackColor = palette.Surface;
                trackBar.ForeColor = palette.Text;
                break;
            case ProgressBar progressBar:
                progressBar.BackColor = palette.Surface;
                break;
            default:
                control.ForeColor = palette.Text;
                if (control is not ProgressBar) control.BackColor = palette.Surface;
                break;
        }

        if (control is Button b) b.Cursor = Cursors.Hand;
    }

    private static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        var palette = Current(_activeMode);
        var isEditField = (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit;
        var isSelected = !isEditField && (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var background = isEditField ? palette.Input : isSelected ? palette.Primary : palette.Surface;
        var foreground = isSelected ? Color.White : palette.Text;

        using (var brush = new SolidBrush(background))
            e.Graphics.FillRectangle(brush, e.Bounds);

        var text = e.Index >= 0 && e.Index < combo.Items.Count
            ? combo.GetItemText(combo.Items[e.Index])
            : combo.Text;
        var textBounds = Rectangle.Inflate(e.Bounds, -8, 0);
        TextRenderer.DrawText(e.Graphics, text, combo.Font, textBounds, foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        if ((e.State & DrawItemState.Focus) == DrawItemState.Focus &&
            (e.State & DrawItemState.NoFocusRect) != DrawItemState.NoFocusRect)
            e.DrawFocusRectangle();
    }

    private static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
        var palette = Current(_activeMode);
        var selected = e.Index == tabs.SelectedIndex;
        var background = selected ? palette.Surface : palette.Window;
        using (var brush = new SolidBrush(background))
            e.Graphics.FillRectangle(brush, e.Bounds);

        var textBounds = Rectangle.Inflate(e.Bounds, -2, -1);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, textBounds,
            selected ? palette.Text : palette.MutedText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (selected)
        {
            using var accent = new SolidBrush(palette.Primary);
            e.Graphics.FillRectangle(accent, e.Bounds.Left + 12, e.Bounds.Bottom - 3, e.Bounds.Width - 24, 3);
        }
    }

    private static void DrawListColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        if (sender is not ListView list) return;
        var palette = Current(_activeMode);
        using (var brush = new SolidBrush(palette.Surface))
            e.Graphics.FillRectangle(brush, e.Bounds);
        using (var pen = new Pen(palette.Border))
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        var bounds = Rectangle.Inflate(e.Bounds, -8, 0);
        using var headerFont = new Font(list.Font, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? string.Empty,
            headerFont, bounds, palette.MutedText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    private static void DrawListItem(object? sender, DrawListViewItemEventArgs e)
    {
        // Detail-view content is drawn at subitem level; other views can use normal rendering.
        e.DrawDefault = sender is ListView list && list.View != View.Details;
    }

    private static void DrawListSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (sender is not ListView list || e.Item is null || e.SubItem is null) return;
        var palette = Current(_activeMode);
        var selected = e.Item.Selected;
        var subItemText = e.SubItem.Text ?? string.Empty;
        var background = selected
            ? palette.Primary
            : e.ItemIndex % 2 == 0 ? palette.Input : palette.Window;
        var foreground = selected ? Color.White : palette.Text;

        using (var brush = new SolidBrush(background))
            e.Graphics.FillRectangle(brush, e.Bounds);
        using (var pen = new Pen(palette.Border))
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        if (!selected && e.ColumnIndex == 3 &&
            (string.Equals(subItemText, "Downloaded", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(subItemText, "Ready", StringComparison.OrdinalIgnoreCase)))
            foreground = Color.FromArgb(22, 163, 74);
        else if (!selected && e.ColumnIndex == 3 && string.Equals(subItemText, "Not downloaded", StringComparison.OrdinalIgnoreCase))
            foreground = palette.MutedText;

        var bounds = Rectangle.Inflate(e.Bounds, -8, 0);
        TextRenderer.DrawText(e.Graphics, subItemText, list.Font, bounds, foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
}
