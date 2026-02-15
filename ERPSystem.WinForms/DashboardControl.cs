using ERPSystem.WinForms.Controls;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms;

public sealed record DashboardNavigationTarget(string SectionKey, int? QuoteId = null, string? JobNumber = null, bool OpenDetails = false);

public sealed class DashboardControl : UserControl, IRealtimeDataControl
{
    private const int QuoteExpiryDays = 30;
    private const int QuoteExpiringSoonDays = 7;
    private const int ProductionDueSoonDays = 2;

    private readonly QuoteRepository _quoteRepository;
    private readonly ProductionRepository _productionRepository;
    private readonly JobFlowService _jobFlowService;
    private readonly Action<DashboardNavigationTarget> _openTarget;

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
        : this(quoteRepository, productionRepository, jobFlowService, target => openSection(target.SectionKey))
    {
    }

    public DashboardControl(QuoteRepository quoteRepository, ProductionRepository productionRepository, JobFlowService jobFlowService, Action<DashboardNavigationTarget> openTarget)
    {
        _quoteRepository = quoteRepository;
        _productionRepository = productionRepository;
        _jobFlowService = jobFlowService;
        _openTarget = openTarget;

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
        var completedQuotes = quotes.Where(q => q.Status == QuoteStatus.Completed).OrderByDescending(q => q.LastUpdatedUtc).ToList();
        var quoteExpiringSoon = inProgressQuotes.Where(IsQuoteExpiringSoon).ToList();

        var productionInProgress = jobs
            .Where(j => _jobFlowService.IsInModule(j.JobNumber, JobFlowService.WorkflowModule.Production) && j.Status != ProductionJobStatus.Completed)
            .OrderBy(j => j.DueDateUtc)
            .ToList();
        var productionNearDue = productionInProgress.Where(IsJobNearDueDate).ToList();
        var qualityQueue = jobs.Where(j => _jobFlowService.IsInModule(j.JobNumber, JobFlowService.WorkflowModule.Quality)).OrderBy(j => j.JobNumber).ToList();
        var inspectionQueue = jobs.Where(j => _jobFlowService.IsInModule(j.JobNumber, JobFlowService.WorkflowModule.Inspection)).OrderBy(j => j.JobNumber).ToList();
        var shippingQueue = jobs.Where(j => _jobFlowService.IsInModule(j.JobNumber, JobFlowService.WorkflowModule.Shipping)).OrderBy(j => j.JobNumber).ToList();
        _glanceCards.SuspendLayout();
        _glanceCards.Controls.Clear();
        _glanceCards.Controls.Add(CreateGlanceCard("Quotes in progress", inProgressQuotes.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Quotes completed (awaiting customer)", completedQuotes.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Quotes expiring soon", quoteExpiringSoon.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Production in progress", productionInProgress.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Jobs near/over due", productionNearDue.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Quality queue", qualityQueue.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Inspection queue", inspectionQueue.Count.ToString()));
        _glanceCards.Controls.Add(CreateGlanceCard("Shipping queue", shippingQueue.Count.ToString()));
        _glanceCards.ResumeLayout();

        _workQueues.SuspendLayout();
        _workQueues.Controls.Clear();
        _workQueues.Controls.Add(CreateOpenQuotesSnapshotPanel(inProgressQuotes));
        _workQueues.Controls.Add(CreateJobFlowSnapshotPanel(inProgressQuotes, productionInProgress, inspectionQueue, shippingQueue));
        _workQueues.Controls.Add(CreateWorkSnapshotPanel(inProgressQuotes, productionInProgress, qualityQueue, inspectionQueue, shippingQueue));
        _workQueues.Controls.Add(CreateModuleSnapshotTable("Production in-progress snapshot", productionInProgress, "Working production orders currently in progress."));
        _workQueues.Controls.Add(CreateModuleSnapshotTable("Quality in-progress snapshot", qualityQueue, "Orders waiting in quality review."));
        _workQueues.Controls.Add(CreateModuleSnapshotTable("Inspection in-progress snapshot", inspectionQueue, "Orders currently in inspection.", showInspectionState: true));
        _workQueues.Controls.Add(CreateModuleSnapshotTable("Shipping in-progress snapshot", shippingQueue, "Orders staged in shipping."));
        _workQueues.Controls.Add(CreateQuoteQueuePanel("In-progress quotes", inProgressQuotes, includeExpiryWarning: false));
        _workQueues.Controls.Add(CreateQuoteQueuePanel("Completed quotes awaiting confirmation", completedQuotes, includeExpiryWarning: false));
        _workQueues.Controls.Add(CreateQuoteQueuePanel("Quotes about to expire", quoteExpiringSoon, includeExpiryWarning: true));
        _workQueues.Controls.Add(CreateProductionQueuePanel("In-progress production orders", productionInProgress));
        _workQueues.Controls.Add(CreateQualityQueuePanel("Quality queue", qualityQueue));
        _workQueues.Controls.Add(CreateInspectionQueuePanel("Inspection queue", inspectionQueue));
        _workQueues.Controls.Add(CreateShippingQueuePanel("Shipping queue", shippingQueue));
        _workQueues.Controls.Add(CreateStatusBarPanel("Job status distribution", jobs));
        _workQueues.ResumeLayout();

        _lastUpdatedLabel.Text = $"Updated {DateTime.Now:g}";
    }

    private Panel CreateOpenQuotesSnapshotPanel(IReadOnlyCollection<Quote> inProgressQuotes)
    {
        var panel = CreateBasePanel();
        panel.Height = 235;

        var title = new Label
        {
            Text = "Open quote snapshot",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 28
        };

        var subtitle = new Label
        {
            Text = "Shows only active quotes (completed / won / lost / expired quotes are hidden).",
            Font = new Font("Segoe UI", 9F),
            Dock = DockStyle.Top,
            Height = 22,
            Tag = "secondary"
        };

        var grid = CreateSnapshotGrid();
        grid.Columns.Add("QuoteId", "Quote");
        grid.Columns.Add("Customer", "Customer");
        grid.Columns.Add("Progress", "Quote progress");
        grid.Columns.Add("Age", "Time since start");
        grid.Columns[0].Width = 90;
        grid.Columns[1].Width = 220;
        grid.Columns[2].Width = 140;
        grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        foreach (var quote in inProgressQuotes.OrderByDescending(q => q.CreatedUtc).Take(25))
        {
            var progress = BuildQuoteProgressText(quote);
            var startedAgo = BuildDurationText(DateTime.UtcNow - quote.CreatedUtc);
            grid.Rows.Add($"Q{quote.Id}", quote.CustomerName, progress, startedAgo);
        }

        if (grid.Rows.Count == 0)
        {
            grid.Rows.Add("-", "No active quotes", "-", "-");
        }

        panel.Controls.Add(grid);
        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        return panel;
    }

    private Panel CreateJobFlowSnapshotPanel(
        IReadOnlyCollection<Quote> inProgressQuotes,
        IReadOnlyCollection<ProductionJob> productionInProgress,
        IReadOnlyCollection<ProductionJob> inspectionQueue,
        IReadOnlyCollection<ProductionJob> shippingQueue)
    {
        var panel = CreateBasePanel();
        panel.Height = 235;

        var title = new Label
        {
            Text = "Operations flow snapshot",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 28
        };

        var subtitle = new Label
        {
            Text = "Current jobs categorized by operational stage: Quote / Production / Inspection / Shipping.",
            Font = new Font("Segoe UI", 9F),
            Dock = DockStyle.Top,
            Height = 22,
            Tag = "secondary"
        };

        var grid = CreateSnapshotGrid();
        grid.Columns.Add("Operation", "Operation");
        grid.Columns.Add("Count", "Active jobs");
        grid.Columns.Add("Examples", "Snapshot");
        grid.Columns[0].Width = 140;
        grid.Columns[1].Width = 100;
        grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        grid.Rows.Add("Quote", inProgressQuotes.Count, BuildQuoteExamplePreview(inProgressQuotes));
        grid.Rows.Add("Production", productionInProgress.Count, BuildJobExamplePreview(productionInProgress));
        grid.Rows.Add("Inspection", inspectionQueue.Count, BuildJobExamplePreview(inspectionQueue));
        grid.Rows.Add("Shipping", shippingQueue.Count, BuildJobExamplePreview(shippingQueue));

        panel.Controls.Add(grid);
        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        return panel;
    }

    private static DataGridView CreateSnapshotGrid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            MultiSelect = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                WrapMode = DataGridViewTriState.True,
                Padding = new Padding(2)
            }
        };
    }

    private static string BuildQuoteProgressText(Quote quote)
    {
        if (quote.Status == QuoteStatus.Completed)
        {
            return "Completed - Awaiting customer confirmation";
        }

        if (quote.LineItems.Count == 0)
        {
            return "Draft";
        }

        var incompleteItems = quote.LineItems.Count(line => line.Quantity <= 0 || line.UnitPrice <= 0m);
        return incompleteItems == 0
            ? $"{quote.LineItems.Count}/{quote.LineItems.Count} priced"
            : $"{quote.LineItems.Count - incompleteItems}/{quote.LineItems.Count} priced";
    }

    private static string BuildDurationText(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{Math.Max(0, (int)duration.TotalMinutes)}m";
    }


    private Panel CreateModuleSnapshotTable(string titleText, IReadOnlyCollection<ProductionJob> jobs, string subtitleText, bool showInspectionState = false)
    {
        var panel = CreateBasePanel();
        panel.Height = 220;

        var title = new Label
        {
            Text = titleText,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 28
        };

        var subtitle = new Label
        {
            Text = subtitleText,
            Font = new Font("Segoe UI", 9F),
            Dock = DockStyle.Top,
            Height = 20,
            Tag = "secondary"
        };

        var grid = CreateSnapshotGrid();
        grid.Columns.Add("JobNumber", "Job");
        grid.Columns.Add("Product", "Product");
        grid.Columns.Add("DueDate", "Due");
        grid.Columns.Add("Status", showInspectionState ? "Inspection" : "Status");
        grid.Columns[0].Width = 120;
        grid.Columns[1].Width = 240;
        grid.Columns[2].Width = 150;
        grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        foreach (var job in jobs.Take(25))
        {
            var statusText = showInspectionState
                ? (_jobFlowService.IsInspectionPassed(job.JobNumber) ? "Passed" : "Pending")
                : job.Status.ToString();
            grid.Rows.Add(job.JobNumber, job.ProductName, job.DueDateUtc.ToLocalTime().ToString("g"), statusText);
        }

        if (grid.Rows.Count == 0)
        {
            grid.Rows.Add("-", "No in-progress items", "-", "-");
        }

        panel.Controls.Add(grid);
        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        return panel;
    }

    private static string BuildJobExamplePreview(IReadOnlyCollection<ProductionJob> jobs)
    {
        var examples = jobs.Take(3).Select(job => $"{job.JobNumber} ({job.ProductName})").ToList();
        return examples.Count == 0 ? "No active jobs" : string.Join(" • ", examples);
    }

    private static string BuildQuoteExamplePreview(IReadOnlyCollection<Quote> quotes)
    {
        var examples = quotes
            .OrderByDescending(q => q.CreatedUtc)
            .Take(3)
            .Select(q => $"Q{q.Id} ({q.CustomerName})")
            .ToList();

        return examples.Count == 0 ? "No active quotes" : string.Join(" • ", examples);
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

    private Panel CreateShippingQueuePanel(string title, IReadOnlyCollection<ProductionJob> jobs)
    {
        var details = jobs.Take(20)
            .Select(job => $"{job.JobNumber} • {job.ProductName} • Ready to ship")
            .ToList();

        return CreateQueueCard(title, details, "Shipping", jobs.Count > 0);
    }

    private Panel CreateWorkSnapshotPanel(
        IReadOnlyCollection<Quote> inProgressQuotes,
        IReadOnlyCollection<ProductionJob> productionInProgress,
        IReadOnlyCollection<ProductionJob> qualityQueue,
        IReadOnlyCollection<ProductionJob> inspectionQueue,
        IReadOnlyCollection<ProductionJob> shippingQueue)
    {
        var snapshotItems = new List<(string Text, DashboardNavigationTarget Target)>();

        snapshotItems.AddRange(inProgressQuotes.Take(3).Select(quote =>
            ($"QUOTES • Q{quote.Id} • {quote.CustomerName}", new DashboardNavigationTarget("Quotes", quote.Id, OpenDetails: true))));

        snapshotItems.AddRange(productionInProgress.Take(3).Select(job =>
            ($"PRODUCTION • {job.JobNumber} • {job.ProductName}", new DashboardNavigationTarget("Production", JobNumber: job.JobNumber, OpenDetails: true))));

        snapshotItems.AddRange(qualityQueue.Take(3).Select(job =>
            ($"QUALITY • {job.JobNumber} • {job.ProductName}", new DashboardNavigationTarget("Quality", JobNumber: job.JobNumber, OpenDetails: true))));

        snapshotItems.AddRange(inspectionQueue.Take(3).Select(job =>
            ($"INSPECTION • {job.JobNumber} • {job.ProductName}", new DashboardNavigationTarget("Inspection", JobNumber: job.JobNumber, OpenDetails: true))));

        snapshotItems.AddRange(shippingQueue.Take(3).Select(job =>
            ($"SHIPPING • {job.JobNumber} • {job.ProductName}", new DashboardNavigationTarget("Shipping", JobNumber: job.JobNumber, OpenDetails: true))));

        var card = CreateBasePanel();
        card.Height = 220;

        var title = new Label
        {
            Text = "WIP Snapshot (Quotes / Production / Quality / Inspection / Shipping)",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold)
        };

        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        if (snapshotItems.Count == 0)
        {
            list.Items.Add("No active work in progress.");
        }
        else
        {
            foreach (var item in snapshotItems)
            {
                list.Items.Add(new SnapshotListItem(item.Text, item.Target));
            }
        }

        list.DisplayMember = nameof(SnapshotListItem.Text);
        list.DoubleClick += (_, _) => OpenSnapshotItem(list);
        list.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter)
            {
                return;
            }

            OpenSnapshotItem(list);
            args.Handled = true;
        };
        list.MouseDown += (_, args) => SelectSnapshotItemFromMouse(list, args);

        var contextMenu = new ContextMenuStrip();
        var openMenuItem = contextMenu.Items.Add("Open");
        openMenuItem.Click += (_, _) => OpenSnapshotItem(list);
        contextMenu.Opening += (_, args) =>
        {
            var hasItem = list.SelectedItem is SnapshotListItem;
            openMenuItem.Enabled = hasItem;
            args.Cancel = !hasItem;
        };
        list.ContextMenuStrip = contextMenu;

        card.Controls.Add(list);
        card.Controls.Add(title);
        return card;
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
        openButton.LinkClicked += (_, _) => _openTarget(new DashboardNavigationTarget(sectionKey));

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

        list.DoubleClick += (_, _) => _openTarget(new DashboardNavigationTarget(sectionKey));

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

    public Task RefreshDataAsync(bool fromFailSafeCheckpoint) => LoadDashboardAsync();

    private void OpenSnapshotItem(ListBox list)
    {
        if (list.SelectedItem is SnapshotListItem item)
        {
            _openTarget(item.Target);
        }
    }

    private static void SelectSnapshotItemFromMouse(ListBox list, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Right)
        {
            return;
        }

        var index = list.IndexFromPoint(args.Location);
        if (index < 0)
        {
            return;
        }

        list.SelectedIndex = index;
    }

    private sealed class SnapshotListItem
    {
        public SnapshotListItem(string text, DashboardNavigationTarget target)
        {
            Text = text;
            Target = target;
        }

        public string Text { get; }

        public DashboardNavigationTarget Target { get; }
    }
}
