using System.Drawing.Drawing2D;

namespace ERPSystem.WinForms;

public class ModernButton : Button
{
    private bool _isHovered;
    private bool _isPressed;

    public int CornerRadius { get; set; } = 5;

    public Color OverrideBaseColor { get; set; } = ColorTranslator.FromHtml("#3A96DD");

    public Color OverrideBorderColor { get; set; } = Color.Transparent;

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F, FontStyle.Bold);

        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    public void ApplyPalette(ThemePalette palette)
    {
        if (Equals(Tag, "active"))
        {
            OverrideBaseColor = palette.Accent;
            OverrideBorderColor = palette.Accent;
            ForeColor = Color.White;
        }
        else
        {
            OverrideBaseColor = palette.Panel;
            OverrideBorderColor = palette.Border;
            ForeColor = palette.TextPrimary;
        }

        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _isHovered = true;
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _isHovered = false;
        _isPressed = false;
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _isPressed = true;
        base.OnMouseDown(mevent);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _isPressed = false;
        base.OnMouseUp(mevent);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var currentColor = OverrideBaseColor;
        if (_isHovered)
        {
            currentColor = ChangeBrightness(currentColor, 0.08f);
        }

        if (_isPressed)
        {
            currentColor = ChangeBrightness(currentColor, -0.12f);
            rect = new Rectangle(rect.X, rect.Y + 1, rect.Width, rect.Height - 1);
        }

        using var path = CreateRoundedRectangle(rect, CornerRadius);
        using var background = new SolidBrush(currentColor);
        using var border = new Pen(OverrideBorderColor, 1F);

        pevent.Graphics.FillPath(background, path);
        pevent.Graphics.DrawPath(border, path);

        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            rect,
            ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(1, radius * 2);
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        var path = new GraphicsPath();

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        return path;
    }

    private static Color ChangeBrightness(Color color, float correctionFactor)
    {
        float red = color.R;
        float green = color.G;
        float blue = color.B;

        if (correctionFactor < 0)
        {
            correctionFactor = 1 + correctionFactor;
            red *= correctionFactor;
            green *= correctionFactor;
            blue *= correctionFactor;
        }
        else
        {
            red = (255 - red) * correctionFactor + red;
            green = (255 - green) * correctionFactor + green;
            blue = (255 - blue) * correctionFactor + blue;
        }

        return Color.FromArgb(color.A, (int)red, (int)green, (int)blue);
    }
}
