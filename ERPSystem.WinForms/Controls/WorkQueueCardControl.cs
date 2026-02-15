using ERPSystem.WinForms;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public sealed record WorkQueueCardItem(string Summary, DashboardNavigationTarget Target, bool Highlight = false);

public sealed class WorkQueueCardControl : UserControl
{
    private readonly Action<DashboardNavigationTarget> _openTarget;
    private readonly string _sectionKey;
    private readonly Label _lblTitle;
    private readonly LinkLabel _lnkOpenModule;
    private readonly Panel _contentScrollPanel;
    private readonly FlowLayoutPanel _flpItems;
    private readonly Label _lblFooter;

    public WorkQueueCardControl(string title, string sectionKey, Color accentColor, Action<DashboardNavigationTarget> openTarget)
    {
        _openTarget = openTarget;
        _sectionKey = sectionKey;

        Dock = DockStyle.Fill;
        Margin = new Padding(6);
        Padding = new Padding(8);
        MinimumSize = new Size(200, 220);
        BackColor = Color.FromArgb(247, 249, 252);

        var tlpCard = new TableLayoutPanel
        {
            Name = "tlpCard",
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        tlpCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        tlpCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tlpCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));

        var headerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(8, 0, 8, 0),
            BackColor = Color.FromArgb(237, 242, 248)
        };

        var accentStrip = new Panel
        {
            Dock = DockStyle.Left,
            Width = 4,
            Margin = new Padding(0),
            BackColor = accentColor
        };

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(6, 0, 0, 0)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98F));

        _lblTitle = new Label
        {
            Name = "lblTitle",
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(42, 48, 60),
            Margin = new Padding(0)
        };

        _lnkOpenModule = new LinkLabel
        {
            Name = "lnkOpenModule",
            Text = "Open",
            Dock = DockStyle.Right,
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = false,
            Width = 92,
            MaximumSize = new Size(92, 18),
            Height = 18,
            Margin = new Padding(0, 11, 0, 0),
            LinkBehavior = LinkBehavior.HoverUnderline
        };
        _lnkOpenModule.LinkClicked += (_, _) => _openTarget(new DashboardNavigationTarget(_sectionKey));

        headerLayout.Controls.Add(_lblTitle, 0, 0);
        headerLayout.Controls.Add(_lnkOpenModule, 1, 0);

        headerPanel.Controls.Add(headerLayout);
        headerPanel.Controls.Add(accentStrip);

        _contentScrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.FromArgb(247, 249, 252)
        };

        _flpItems = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _contentScrollPanel.Controls.Add(_flpItems);

        _lblFooter = new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(98, 105, 118),
            Padding = new Padding(2, 0, 0, 0)
        };

        tlpCard.Controls.Add(headerPanel, 0, 0);
        tlpCard.Controls.Add(_contentScrollPanel, 0, 1);
        tlpCard.Controls.Add(_lblFooter, 0, 2);

        Controls.Add(tlpCard);

        Resize += (_, _) => ResizeItemCards();
    }

    public void SetItems(IReadOnlyList<WorkQueueCardItem> items)
    {
        _flpItems.SuspendLayout();
        _flpItems.Controls.Clear();

        if (items.Count == 0)
        {
            _flpItems.Controls.Add(CreateItemPanel("No items currently in queue.", null, false));
            _lblFooter.Text = "0 items";
        }
        else
        {
            foreach (var item in items)
            {
                _flpItems.Controls.Add(CreateItemPanel(item.Summary, item.Target, item.Highlight));
            }

            _lblFooter.Text = $"{items.Count} item{(items.Count == 1 ? string.Empty : "s")}";
        }

        _flpItems.ResumeLayout();
        ResizeItemCards();
    }

    private Control CreateItemPanel(string summary, DashboardNavigationTarget? target, bool highlight)
    {
        var itemPanel = new Panel
        {
            Height = 42,
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(8, 6, 8, 6),
            BackColor = Color.White,
            Cursor = target is null ? Cursors.Default : Cursors.Hand
        };

        var itemLabel = new Label
        {
            Text = summary,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F),
            ForeColor = highlight ? Color.FromArgb(166, 47, 54) : Color.FromArgb(56, 62, 72),
            Margin = new Padding(0)
        };

        itemPanel.Controls.Add(itemLabel);

        if (target is not null)
        {
            itemPanel.Click += (_, _) => _openTarget(target);
            itemLabel.Click += (_, _) => _openTarget(target);
        }

        return itemPanel;
    }

    private void ResizeItemCards()
    {
        var targetWidth = Math.Max(120, _contentScrollPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
        foreach (Control itemControl in _flpItems.Controls)
        {
            itemControl.Width = targetWidth;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle,
            Color.FromArgb(205, 212, 222), ButtonBorderStyle.Solid);
    }
}
