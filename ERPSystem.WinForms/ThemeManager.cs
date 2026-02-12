using System.Drawing;
using System.Windows.Forms;

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
    public required Color NavHover { get; init; }
}

public sealed class ThemeManager
{
    private readonly Dictionary<AppTheme, ThemePalette> palettes = new()
    {
        [AppTheme.Dark] = new ThemePalette
        {
            Background = ColorTranslator.FromHtml("#1E1E1E"),
            Panel = ColorTranslator.FromHtml("#252526"),
            Accent = ColorTranslator.FromHtml("#3A96DD"),
            AccentHover = ColorTranslator.FromHtml("#4CA6EA"),
            AccentPressed = ColorTranslator.FromHtml("#2E7DB8"),
            TextPrimary = Color.White,
            TextSecondary = Color.FromArgb(205, 205, 205),
            Border = Color.FromArgb(55, 55, 57),
            NavHover = Color.FromArgb(45, 45, 48)
        },
        [AppTheme.Light] = new ThemePalette
        {
            Background = ColorTranslator.FromHtml("#F5F7FA"),
            Panel = Color.White,
            Accent = ColorTranslator.FromHtml("#3A96DD"),
            AccentHover = ColorTranslator.FromHtml("#55A7E6"),
            AccentPressed = ColorTranslator.FromHtml("#2877B4"),
            TextPrimary = ColorTranslator.FromHtml("#1F2937"),
            TextSecondary = ColorTranslator.FromHtml("#6B7280"),
            Border = ColorTranslator.FromHtml("#D1D5DB"),
            NavHover = ColorTranslator.FromHtml("#EEF2F7")
        }
    };

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public ThemePalette CurrentPalette => palettes[CurrentTheme];

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
            case StatusStrip statusStrip:
                statusStrip.BackColor = palette.Panel;
                statusStrip.ForeColor = palette.TextSecondary;
                break;
            case ToolStrip toolStrip:
                toolStrip.BackColor = palette.Panel;
                toolStrip.ForeColor = palette.TextPrimary;
                break;
            case ModernButton modernButton:
                modernButton.ApplyPalette(palette);
                break;
            case Label label:
                label.ForeColor = palette.TextPrimary;
                break;
            case Panel panel:
                panel.BackColor = palette.Panel;
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
}
