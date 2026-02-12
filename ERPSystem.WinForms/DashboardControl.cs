using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms;

public sealed class DashboardControl : UserControl
{
    private const int QuoteExpiryDays = 30;
    private const int QuoteExpiringSoonDays = 7;
    private const int ProductionDueSoonDays = 2;

    private readonly QuoteRepository _quoteRepository;
    private readonly ProductionRepository _productionRepository;
    private readonly JobFlowService _jobFlowService;
    private readonly Action<string> _openSection;

    private readonly FlowLayoutPanel _glanceCards = new()
    {
        Dock = DockStyle.Fill,
        WrapContents = false,
        AutoScroll = true,
        Margin = new Padding(0, 0, 0, 12)
    };

    private readonly FlowLayoutPanel _workQueues = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(0, 0, 8, 0)
    };

    private readonly Label _lastUpdatedLabel = new()
    {
        Text = "Loading dashboard queues...",
        Dock = DockStyle.Bottom,
        Height = 24,
        TextAlign = ContentAlignment.MiddleLeft,
        Tag = "secondary"
    };

    public DashboardControl(QuoteRepository quoteRepository, ProductionRepository productionRepository, JobFlowService jobFlowService, Action<string> openSection)
    {
        _quoteRepository = quoteRepository;
        _productionRepository = productionRepository;
        _jobFlowService = jobFlowService;
        _openSection = openSection;

        DoubleBuffered = true;
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Dashboard",
            Font = new Font("Segoe UI", 19F, FontStyle.Bold),
            AutoSize = true
        };

        var subtitle = new Label
        {
            Text = "At-a-glance management view for quotes, production, quality and inspection.",
            Font = new Font("Segoe UI", 10F),
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 12),
            Tag = "secondary"
        };

        var glancePanel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 130,
            Padding = new Padding(0, 0, 0, 10)
        };
        glancePanel.Controls.Add(_glanceCards);

        var queueTitle = new Label
        {
            Text = "In-progress work queues",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 30
        };

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(glancePanel, 0, 2);
        root.Controls.Add(queueTitle, 0, 3);
        root.Controls.Add(_lastUpdatedLabel, 0, 5);

        var queueHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 28, 0, 0) };
        queueHost.Controls.Add(_workQueues);
        root.Controls.Add(queueHost, 0, 4);

        Controls.Add(root);

        Load += async (_, _) => await LoadDashboardAsync();
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Background;
        ForeColor = palette.TextPrimary;
    }

    private async Task LoadDashboardAsync()
    {
        IReadOnlyList<Quote> quotes;
        IReadOnlyList<ProductionJob> jobs;

        try
        {
            quotes = await _quoteRepository.GetQuotesAsync();
            jobs = await _productionRepository.GetJobsAsync();
        }
        catch (Exception ex)
        {
            _lastUpdatedLabel.Text = $"Unable to load dashboard data: {ex.Message}";
            return;
        }

        var inProgressQuotes = quotes.Where(q => q.Status == QuoteStatus.InProgress).OrderBy(q => q.LastUpdatedUtc).ToList();
        var quoteExpiringSoon = inProgressQuotes.Where(IsQuoteExpiringSoon).ToList();

        var productionInProgress = jobs.Where(j => j.Status == ProductionJobStatus.InProgress).OrderBy(j => j.DueDateUtc).ToList();
        var productionNearDue = productionInProgress.Where(IsJobNearDueDate).ToList();
        var qualityQueue = jobs.Where(j => j.Status == ProductionJobStatus.Completed && !_jobFlowService.IsQualityApproved(j.JobNumber)).OrderBy(j => j.JobNumber).ToList();
        var inspectionQueue = jobs.Where(j => _jobFlowService.IsQualityApproved(j.JobNumber)).OrderBy(j => j.JobNumber).ToList();
        _glanceCards.SuspendLayout();
        _glanceCards.Controls.Clear();
        _glanceCards.Controls.Add(CreateGlanceCard("Quotes in progress", inProgressQuotes.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Quotes expiring soon", quoteExpiringSoon.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Production in progress", productionInProgress.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Jobs near/over due", productionNearDue.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Quality queue", qualityQueue.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Inspection queue", inspectionQueue.Count.ToString()));
        _glanceCards.ResumeLayout();

        _workQueues.SuspendLayout();
        _workQueues.Controls.Clear();
        _workQueues.Controls.Add(CreateQuoteQueuePanel("In-progress quotes", inProgressQuotes, includeExpiryWarning: false));
        _workQueues.Controls.Add(CreateQuoteQueuePanel("Quotes about to expire", quoteExpiringSoon, includeExpiryWarning: true));
        _workQueues.Controls.Add(CreateProductionQueuePanel("In-progress production orders", productionInProgress));
        _workQueues.Controls.Add(CreateQualityQueuePanel("Quality queue", qualityQueue));
        _workQueues.Controls.Add(CreateInspectionQueuePanel("Inspection queue", inspectionQueue));
        _workQueues.Controls.Add(CreateStatusBarPanel("Job status distribution", jobs));
        _workQueues.ResumeLayout();

        _lastUpdatedLabel.Text = $"Updated {DateTime.Now:g}";
    }

    private Panel CreateQuoteQueuePanel(string title, IReadOnlyCollection<Quote> quotes, bool includeExpiryWarning)
    {
        var details = new List<string>();
        foreach (var quote in quotes.Take(20))
        {
            var ageDays = (DateTime.UtcNow - quote.LastUpdatedUtc).TotalDays;
            var expiryText = includeExpiryWarning ? $" • est. expires in {Math.Max(0, QuoteExpiryDays - (int)ageDays)}d" : string.Empty;
            details.Add($"Q{quote.Id} • {quote.CustomerName} • updated {quote.LastUpdatedUtc:g}{expiryText}");
        }

        return CreateQueueCard(title, details, "Quotes", quotes.Count > 0);
    }

    private Panel CreateProductionQueuePanel(string title, IReadOnlyCollection<ProductionJob> jobs)
    {
        var details = new List<string>();
        foreach (var job in jobs.Take(20))
        {
            var dueNote = job.DueDateUtc < DateTime.UtcNow ? "⚠ Overdue" : (job.DueDateUtc - DateTime.UtcNow).TotalDays <= ProductionDueSoonDays ? "⚑ Due soon" : "On track";
            details.Add($"{job.JobNumber} • {job.ProductName} • due {job.DueDateUtc:g} • {dueNote}");
        }

        return CreateQueueCard(title, details, "Production", jobs.Count > 0);
    }

    private Panel CreateQualityQueuePanel(string title, IReadOnlyCollection<ProductionJob> jobs)
    {
        var details = jobs.Take(20)
            .Select(job => $"{job.JobNumber} • {job.ProductName} • Waiting for quality approval")
            .ToList();

        return CreateQueueCard(title, details, "Quality", jobs.Count > 0);
    }

    private Panel CreateInspectionQueuePanel(string title, IReadOnlyCollection<ProductionJob> jobs)
    {
        var details = jobs.Take(20)
            .Select(job => $"{job.JobNumber} • {job.ProductName} • {(_jobFlowService.IsInspectionPassed(job.JobNumber) ? "Passed" : "Pending")}")
            .ToList();

        return CreateQueueCard(title, details, "Inspection", jobs.Count > 0);
    }

    private Panel CreateStatusBarPanel(string title, IReadOnlyCollection<ProductionJob> jobs)
    {
        var panel = CreateBasePanel();
        panel.Height = 200;

        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 28
        };

        var bars = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0, 8, 0, 0)
        };

        bars.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
        bars.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
        bars.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
        bars.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));

        var grouped = jobs.GroupBy(job => job.Status).ToDictionary(group => group.Key, group => group.Count());
        var total = Math.Max(1, jobs.Count);

        bars.Controls.Add(CreateBarRow("Planned", grouped.GetValueOrDefault(ProductionJobStatus.Planned), total, Color.SteelBlue), 0, 0);
        bars.Controls.Add(CreateBarRow("In Progress", grouped.GetValueOrDefault(ProductionJobStatus.InProgress), total, Color.SeaGreen), 0, 1);
        bars.Controls.Add(CreateBarRow("Completed", grouped.GetValueOrDefault(ProductionJobStatus.Completed), total, Color.DarkViolet), 0, 2);
        bars.Controls.Add(CreateBarRow("On Hold", grouped.GetValueOrDefault(ProductionJobStatus.OnHold), total, Color.IndianRed), 0, 3);

        panel.Controls.Add(bars);
        panel.Controls.Add(titleLabel);
        return panel;
    }

    private static Panel CreateBarRow(string label, int value, int total, Color color)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = new Padding(0, 2, 0, 2)
        };

        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45));

        var labelControl = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var barHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 8, 4) };
        var bar = new Panel
        {
            Dock = DockStyle.Left,
            Width = (int)Math.Max(6, Math.Round((double)value / total * 240)),
            BackColor = color
        };
        barHost.Controls.Add(bar);

        var valueControl = new Label { Text = value.ToString(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };

        row.Controls.Add(labelControl, 0, 0);
        row.Controls.Add(barHost, 1, 0);
        row.Controls.Add(valueControl, 2, 0);
        return new Panel { Dock = DockStyle.Fill, Controls = { row } };
    }

    private Panel CreateQueueCard(string title, IReadOnlyList<string> items, string sectionKey, bool hasItems)
    {
        var panel = CreateBasePanel();
        panel.Height = 190;

        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Height = 32
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var openButton = new LinkLabel
        {
            Text = "Open module",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline
        };
        openButton.LinkClicked += (_, _) => _openSection(sectionKey);

        titleRow.Controls.Add(titleLabel, 0, 0);
        titleRow.Controls.Add(openButton, 1, 0);

        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            HorizontalScrollbar = true,
            IntegralHeight = false
        };

        if (hasItems)
        {
            foreach (var item in items)
            {
                list.Items.Add(item);
            }
        }
        else
        {
            list.Items.Add("No items to show.");
        }

        list.DoubleClick += (_, _) => _openSection(sectionKey);

        panel.Controls.Add(list);
        panel.Controls.Add(titleRow);
        return panel;
    }

    private static Panel CreateGlanceCard(string metric, string value)
    {
        var panel = CreateBasePanel();
        panel.Width = 230;
        panel.Height = 92;
        panel.Margin = new Padding(0, 0, 10, 0);

        var valueLabel = new Label
        {
            Text = value,
            Font = new Font("Segoe UI", 19F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 44
        };

        var metricLabel = new Label
        {
            Text = metric,
            Font = new Font("Segoe UI", 9.5F),
            Dock = DockStyle.Fill,
            Tag = "secondary"
        };

        panel.Controls.Add(metricLabel);
        panel.Controls.Add(valueLabel);
        return panel;
    }

    private static Panel CreateBasePanel()
    {
        return new Panel
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12),
            BorderStyle = BorderStyle.FixedSingle,
            Width = 900
        };
    }

    private static bool IsQuoteExpiringSoon(Quote quote)
    {
        var age = DateTime.UtcNow - quote.LastUpdatedUtc;
        return age.TotalDays >= (QuoteExpiryDays - QuoteExpiringSoonDays);
    }

    private static bool IsJobNearDueDate(ProductionJob job)
    {
        var remaining = job.DueDateUtc - DateTime.UtcNow;
        return remaining.TotalDays <= ProductionDueSoonDays;
    }
}
