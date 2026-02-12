namespace ERPSystem.WinForms;

public enum AppTheme
{
    Dark,
    Light
}

public sealed class ThemePalette
{
    public required Color Background { get; init; }
    public required Color Panel { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentHover { get; init; }
    public required Color AccentPressed { get; init; }
    public required Color TextPrimary { get; init; }
    public required Color TextSecondary { get; init; }
    public required Color Border { get; init; }
}

public sealed class ThemeManager
{
    private readonly Dictionary<AppTheme, ThemePalette> _palettes = new()
    {
        [AppTheme.Dark] = new ThemePalette
        {
            Background = ColorTranslator.FromHtml("#1E1E1E"),
            Panel = ColorTranslator.FromHtml("#252526"),
            Accent = ColorTranslator.FromHtml("#3A96DD"),
            AccentHover = ColorTranslator.FromHtml("#51A8EA"),
            AccentPressed = ColorTranslator.FromHtml("#2E7DB8"),
            TextPrimary = ColorTranslator.FromHtml("#FFFFFF"),
            TextSecondary = ColorTranslator.FromHtml("#C8C8C8"),
            Border = ColorTranslator.FromHtml("#38383A")
        },
        [AppTheme.Light] = new ThemePalette
        {
            Background = ColorTranslator.FromHtml("#F5F7FA"),
            Panel = ColorTranslator.FromHtml("#FFFFFF"),
            Accent = ColorTranslator.FromHtml("#3A96DD"),
            AccentHover = ColorTranslator.FromHtml("#4FA5E7"),
            AccentPressed = ColorTranslator.FromHtml("#2A7AB7"),
            TextPrimary = ColorTranslator.FromHtml("#1F2937"),
            TextSecondary = ColorTranslator.FromHtml("#6B7280"),
            Border = ColorTranslator.FromHtml("#D1D5DB")
        }
    };

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public ThemePalette CurrentPalette => _palettes[CurrentTheme];

    public event EventHandler<AppTheme>? ThemeChanged;

    public void ToggleTheme()
    {
        CurrentTheme = CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ThemeChanged?.Invoke(this, CurrentTheme);
    }

    public void ApplyTheme(Control root)
    {
        ApplyRecursive(root, CurrentPalette);
    }

    private static void ApplyRecursive(Control control, ThemePalette palette)
    {
        switch (control)
        {
            case Form form:
                form.BackColor = palette.Background;
                form.ForeColor = palette.TextPrimary;
                break;
            case ModernButton modernButton:
                modernButton.ApplyPalette(palette);
                break;
            case DataGridView grid:
                ApplyGridTheme(grid, palette);
                break;
            case Panel:
            case FlowLayoutPanel:
            case TableLayoutPanel:
            case GroupBox:
                control.BackColor = palette.Panel;
                control.ForeColor = palette.TextPrimary;
                break;
            case Label label:
                label.ForeColor = palette.TextPrimary;
                if (label.Tag?.ToString() == "secondary")
                {
                    label.ForeColor = palette.TextSecondary;
                }

                break;
            default:
                control.BackColor = palette.Background;
                control.ForeColor = palette.TextPrimary;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyRecursive(child, palette);
        }
    }

    private static void ApplyGridTheme(DataGridView grid, ThemePalette palette)
    {
        grid.BackgroundColor = palette.Panel;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;

        grid.ColumnHeadersDefaultCellStyle.BackColor = palette.Panel;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Panel;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.TextPrimary;

        grid.DefaultCellStyle.BackColor = palette.Background;
        grid.DefaultCellStyle.ForeColor = palette.TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = palette.AccentPressed;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;

        grid.RowHeadersDefaultCellStyle.BackColor = palette.Panel;
        grid.RowHeadersDefaultCellStyle.ForeColor = palette.TextPrimary;
        grid.GridColor = palette.Border;
    }
}
