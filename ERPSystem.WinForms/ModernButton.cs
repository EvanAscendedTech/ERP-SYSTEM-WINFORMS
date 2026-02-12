using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ERPSystem.WinForms;

public class ModernButton : Button
{
    private readonly Timer animationTimer;
    private float hoverProgress;
    private bool isHovered;
    private bool isPressed;

    public int CornerRadius { get; set; } = 6;

    public Color AccentColor { get; private set; } = ColorTranslator.FromHtml("#3A96DD");

    public Color HoverColor { get; private set; } = ColorTranslator.FromHtml("#4CA6EA");

    public Color PressedColor { get; private set; } = ColorTranslator.FromHtml("#2E7DB8");

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9F, FontStyle.Semibold, GraphicsUnit.Point);
        ForeColor = Color.White;
        BackColor = AccentColor;

        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);

        animationTimer = new Timer { Interval = 15 };
        animationTimer.Tick += (_, _) =>
        {
            const float step = 0.12f;
            hoverProgress = isHovered
                ? Math.Min(1f, hoverProgress + step)
                : Math.Max(0f, hoverProgress - step);

            Invalidate();

            if (hoverProgress is 0f or 1f)
            {
                animationTimer.Stop();
            }
        };
    }

    public void ApplyPalette(ThemePalette palette)
    {
        AccentColor = palette.Accent;
        HoverColor = palette.AccentHover;
        PressedColor = palette.AccentPressed;
        ForeColor = palette.TextPrimary;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        isHovered = true;
        animationTimer.Start();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        isHovered = false;
        animationTimer.Start();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        isPressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        isPressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        pevent.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var baseColor = isPressed
            ? PressedColor
            : BlendColors(AccentColor, HoverColor, hoverProgress);

        using var path = CreateRoundedRectanglePath(rect, CornerRadius);
        using var brush = new SolidBrush(baseColor);

        pevent.Graphics.FillPath(brush, path);

        if (isPressed)
        {
            var pressRect = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
            using var pressPath = CreateRoundedRectanglePath(pressRect, CornerRadius);
            using var pressBrush = new SolidBrush(Color.FromArgb(25, Color.Black));
            pevent.Graphics.FillPath(pressBrush, pressPath);
        }

        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            rect,
            ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
    {
        var diameter = Math.Max(1, radius * 2);
        var arc = new Rectangle(rect.Location, new Size(diameter, diameter));
        var path = new GraphicsPath();

        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        return path;
    }

    private static Color BlendColors(Color from, Color to, float amount)
    {
        var a = (byte)(from.A + ((to.A - from.A) * amount));
        var r = (byte)(from.R + ((to.R - from.R) * amount));
        var g = (byte)(from.G + ((to.G - from.G) * amount));
        var b = (byte)(from.B + ((to.B - from.B) * amount));
        return Color.FromArgb(a, r, g, b);
    }
}
