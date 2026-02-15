using ERPSystem.WinForms.Controls;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using System.Drawing.Drawing2D;

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

    private readonly TableLayoutPanel _glanceCards = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 4,
        RowCount = 2,
        Margin = new Padding(0, 0, 0, 12)
    };

    private readonly TableLayoutPanel _workflowStageCards = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 11,
        RowCount = 1,
        Margin = new Padding(0, 0, 0, 12)
    };

    private readonly TableLayoutPanel _workQueues = new()
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
        Padding = new Padding(0),
        AutoScroll = true,
        ColumnCount = 3,
        RowCount = 2
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

        ConfigureGlanceCardsLayout();
        ConfigureWorkflowStageLayout();
        ConfigureWorkQueueLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
            Text = "At-a-glance workflow progression from quote through customer follow-up.",
            Font = new Font("Segoe UI", 10F),
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 8),
            Tag = "secondary"
        };

        var flowTitle = new Label
        {
            Text = "Workflow progression",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 28
        };

        var glancePanel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 220,
            Padding = new Padding(0, 0, 0, 8)
        };
        glancePanel.Controls.Add(_glanceCards);

        var stagePanel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 84,
            Padding = new Padding(0, 0, 0, 8)
        };
        stagePanel.Controls.Add(_workflowStageCards);

        var queueTitle = new Label
        {
            Text = "In-progress work queues",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 30
        };

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(flowTitle, 0, 2);
        root.Controls.Add(stagePanel, 0, 3);
        root.Controls.Add(glancePanel, 0, 4);
        root.Controls.Add(_lastUpdatedLabel, 0, 6);

        var queueHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        queueHost.Controls.Add(queueTitle);
        queueHost.Controls.Add(_workQueues);
        root.Controls.Add(queueHost, 0, 5);

        Controls.Add(root);

        Load += async (_, _) => await LoadDashboardAsync();
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Background;
        ForeColor = palette.TextPrimary;
    }

    private void ConfigureGlanceCardsLayout()
    {
        _glanceCards.ColumnStyles.Clear();
        _glanceCards.RowStyles.Clear();
        for (var index = 0; index < _glanceCards.ColumnCount; index++)
        {
            _glanceCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        }

        _glanceCards.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        _glanceCards.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
    }

    private void ConfigureWorkflowStageLayout()
    {
        _workflowStageCards.ColumnStyles.Clear();
        _workflowStageCards.RowStyles.Clear();
        for (var index = 0; index < _workflowStageCards.ColumnCount; index++)
        {
            var isArrowColumn = index % 2 == 1;
            _workflowStageCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, isArrowColumn ? 3F : 13.666F));
        }

        _workflowStageCards.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
    }

    private void ConfigureWorkQueueLayout()
    {
        _workQueues.ColumnStyles.Clear();
        _workQueues.RowStyles.Clear();
        _workQueues.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        _workQueues.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        _workQueues.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        _workQueues.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        _workQueues.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
    }

    private void PopulateGlanceCards(params (string Metric, string Value)[] cards)
    {
        _glanceCards.SuspendLayout();
        _glanceCards.Controls.Clear();

        for (var index = 0; index < cards.Length; index++)
        {
            var card = CreateGlanceCard(cards[index].Metric, cards[index].Value);
            _glanceCards.Controls.Add(card, index % 4, index / 4);
        }

        _glanceCards.ResumeLayout();
    }

    private void PopulateWorkflowStages(params Control[] stageCards)
    {
        _workflowStageCards.SuspendLayout();
        _workflowStageCards.Controls.Clear();

        for (var index = 0; index < stageCards.Length; index++)
        {
            var column = index * 2;
            _workflowStageCards.Controls.Add(stageCards[index], column, 0);
            if (index < stageCards.Length - 1)
            {
                _workflowStageCards.Controls.Add(CreateStageArrow(), column + 1, 0);
            }
        }

        _workflowStageCards.ResumeLayout();
    }

    private void PopulateQueueGrid(params Control[] cards)
    {
        _workQueues.SuspendLayout();
        _workQueues.Controls.Clear();
        for (var index = 0; index < cards.Length; index++)
        {
            var row = index / _workQueues.ColumnCount;
            var col = index % _workQueues.ColumnCount;
            _workQueues.Controls.Add(cards[index], col, row);
        }

        _workQueues.ResumeLayout();
    }

    private Panel CreateStageCard(string title, string subtitle, int count, Color color, string sectionKey)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(2, 0, 2, 0),
            Padding = new Padding(12, 10, 12, 10),
            BackColor = ControlPaint.Light(color, 0.9f)
        };

        var header = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = color
        };

        var detail = new Label
        {
            Text = $"{subtitle}: {count}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F)
        };

        panel.Click += (_, _) => _openTarget(new DashboardNavigationTarget(sectionKey));
        header.Click += (_, _) => _openTarget(new DashboardNavigationTarget(sectionKey));
        detail.Click += (_, _) => _openTarget(new DashboardNavigationTarget(sectionKey));

        panel.Controls.Add(detail);
        panel.Controls.Add(header);
        return panel;
    }

    private static Label CreateStageArrow()
    {
        return new Label
        {
            Text = "➜",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = Color.FromArgb(92, 102, 122),
            Margin = new Padding(0)
        };
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
        var purchasingQueue = BuildPurchasingQueue(quotes, jobs);
        var qualityQueue = jobs.Where(j => _jobFlowService.IsInModule(j.JobNumber, JobFlowService.WorkflowModule.Quality)).OrderBy(j => j.JobNumber).ToList();
        var inspectionQueue = jobs.Where(j => _jobFlowService.IsInModule(j.JobNumber, JobFlowService.WorkflowModule.Inspection)).OrderBy(j => j.JobNumber).ToList();
        var qualityAndInspectionQueue = qualityQueue.Concat(inspectionQueue).OrderBy(j => j.JobNumber).ToList();
        var shippingQueue = jobs.Where(j => _jobFlowService.IsInModule(j.JobNumber, JobFlowService.WorkflowModule.Shipping)).OrderBy(j => j.JobNumber).ToList();
        PopulateGlanceCards(
            ("Quotes in progress", inProgressQuotes.Count.ToString()),
            ("Quotes completed", completedQuotes.Count.ToString()),
            ("Expiring soon", quoteExpiringSoon.Count.ToString()),
            ("Purchasing queue", purchasingQueue.Count.ToString()),
            ("Production in progress", productionInProgress.Count.ToString()),
            ("Near/over due", productionNearDue.Count.ToString()),
            ("Inspection queue", qualityAndInspectionQueue.Count.ToString()),
            ("Shipping queue", shippingQueue.Count.ToString()));

        PopulateWorkflowStages(
            CreateStageCard("Quotes", "In progress", inProgressQuotes.Count, Color.FromArgb(45, 125, 255), "Quotes"),
            CreateStageCard("Purchasing", "Pending", purchasingQueue.Count, Color.FromArgb(176, 131, 72), "Purchasing"),
            CreateStageCard("Production", "Active jobs", productionInProgress.Count, Color.FromArgb(83, 143, 94), "Production"),
            CreateStageCard("Inspection", "Queued", qualityAndInspectionQueue.Count, Color.FromArgb(205, 98, 184), "Inspection"),
            CreateStageCard("Shipping", "Staged", shippingQueue.Count, Color.FromArgb(95, 175, 193), "Shipping"),
            CreateStageCard("CRM", "Follow-up", completedQuotes.Count, Color.FromArgb(121, 111, 214), "CRM"));

        PopulateQueueGrid(
            CreateQuoteQueuePanel("Quotes", inProgressQuotes, includeExpiryWarning: false),
            CreatePurchasingQueuePanel("Purchasing", purchasingQueue),
            CreateProductionQueuePanel("Production", productionInProgress),
            CreateInspectionQueuePanel("Inspection", qualityAndInspectionQueue),
            CreateShippingQueuePanel("Shipping", shippingQueue));

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

    private static List<Quote> BuildPurchasingQueue(IReadOnlyCollection<Quote> quotes, IReadOnlyCollection<ProductionJob> jobs)
    {
        var sourcedQuoteIds = jobs
            .Where(job => job.SourceQuoteId.HasValue)
            .Select(job => job.SourceQuoteId!.Value)
            .ToHashSet();

        return quotes
            .Where(quote => quote.Status == QuoteStatus.Won && !sourcedQuoteIds.Contains(quote.Id))
            .OrderBy(quote => quote.LastUpdatedUtc)
            .ToList();
    }

    private Panel CreatePurchasingQueuePanel(string title, IReadOnlyCollection<Quote> quotes)
    {
        var details = quotes
            .Take(20)
            .Select(quote => new StageTaskItem(
                $"Q{quote.Id} • {quote.CustomerName} • Purchasing incomplete",
                new DashboardNavigationTarget("Quotes", quote.Id, OpenDetails: true)))
            .ToList();

        return CreateQueueCard(title, details, "Purchasing", Color.FromArgb(176, 131, 72));
    }

    private Panel CreateProductionQueuePanel(string title, IReadOnlyCollection<ProductionJob> jobs)
    {
        var details = new List<StageTaskItem>();
        foreach (var job in jobs.Take(20))
        {
            var dueNote = job.DueDateUtc < DateTime.UtcNow ? "⚠ Overdue" : (job.DueDateUtc - DateTime.UtcNow).TotalDays <= ProductionDueSoonDays ? "⚑ Due soon" : "On track";
            var isLagging = dueNote != "On track";
            details.Add(new StageTaskItem(
                $"{job.JobNumber} • {job.ProductName} • due {job.DueDateUtc:g} • {dueNote}",
                new DashboardNavigationTarget("Production", JobNumber: job.JobNumber, OpenDetails: true),
                isLagging));
        }

        return CreateQueueCard(title, details, "Production", Color.FromArgb(83, 143, 94));
    }

    private Panel CreateInspectionQueuePanel(string title, IReadOnlyCollection<ProductionJob> jobs)
    {
        var details = jobs.Take(20)
            .Select(job => new StageTaskItem(
                $"{job.JobNumber} • {job.ProductName} • {(_jobFlowService.IsInspectionPassed(job.JobNumber) ? "Passed" : "Pending")}",
                new DashboardNavigationTarget("Inspection", JobNumber: job.JobNumber, OpenDetails: true)))
            .ToList();

        return CreateQueueCard(title, details, "Inspection", Color.FromArgb(205, 98, 184));
    }

    private Panel CreateShippingQueuePanel(string title, IReadOnlyCollection<ProductionJob> jobs)
    {
        var details = jobs.Take(20)
            .Select(job => new StageTaskItem(
                $"{job.JobNumber} • {job.ProductName} • Ready to ship",
                new DashboardNavigationTarget("Shipping", JobNumber: job.JobNumber, OpenDetails: true)))
            .ToList();

        return CreateQueueCard(title, details, "Shipping", Color.FromArgb(95, 175, 193));
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
            ($"QUALITY • {job.JobNumber} • {job.ProductName}", new DashboardNavigationTarget("Inspection", JobNumber: job.JobNumber, OpenDetails: true))));

        snapshotItems.AddRange(inspectionQueue.Take(3).Select(job =>
            ($"INSPECTION • {job.JobNumber} • {job.ProductName}", new DashboardNavigationTarget("Inspection", JobNumber: job.JobNumber, OpenDetails: true))));

        snapshotItems.AddRange(shippingQueue.Take(3).Select(job =>
            ($"SHIPPING • {job.JobNumber} • {job.ProductName}", new DashboardNavigationTarget("Shipping", JobNumber: job.JobNumber, OpenDetails: true))));

        var card = CreateBasePanel();
        card.Height = 220;

        var title = new Label
        {
            Text = "WIP Snapshot (Quotes / Purchasing / Production / Inspection / Shipping)",
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

    private Panel CreateQueueCard(string title, IReadOnlyList<StageTaskItem> items, string sectionKey, Color stageColor)
    {
        var panel = CreateBasePanel();
        panel.Height = 198;
        panel.BackColor = ControlPaint.Light(stageColor, 0.92f);
        panel.Margin = new Padding(0, 0, 8, 8);
        panel.Paint += (_, args) => PaintBeveledCard(args.Graphics, panel.ClientRectangle, stageColor);

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
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = stageColor
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
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed
        };

        if (items.Count > 0)
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

        list.DrawItem += (_, args) => DrawStageTask(list, args);
        list.DoubleClick += (_, _) => OpenStageTask(list, sectionKey);
        list.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter)
            {
                return;
            }

            OpenStageTask(list, sectionKey);
            args.Handled = true;
        };

        panel.Controls.Add(list);
        panel.Controls.Add(titleRow);
        return panel;
    }

    private Panel CreateQuoteQueuePanel(string title, IReadOnlyCollection<Quote> quotes, bool includeExpiryWarning)
    {
        var details = quotes
            .Take(20)
            .Select(quote =>
            {
                var ageDays = (DateTime.UtcNow - quote.LastUpdatedUtc).TotalDays;
                var expiryText = includeExpiryWarning ? $" • est. expires in {Math.Max(0, QuoteExpiryDays - (int)ageDays)}d" : string.Empty;
                return new StageTaskItem(
                    $"Q{quote.Id} • {quote.CustomerName} • updated {quote.LastUpdatedUtc:g}{expiryText}",
                    new DashboardNavigationTarget("Quotes", quote.Id, OpenDetails: true));
            })
            .ToList();

        return CreateQueueCard(title, details, "Quotes", Color.FromArgb(45, 125, 255));
    }

    private void OpenStageTask(ListBox list, string sectionKey)
    {
        if (list.SelectedItem is StageTaskItem task)
        {
            _openTarget(task.Target);
            return;
        }

        _openTarget(new DashboardNavigationTarget(sectionKey));
    }

    private static void DrawStageTask(ListBox list, DrawItemEventArgs args)
    {
        args.DrawBackground();
        if (args.Index < 0 || args.Index >= list.Items.Count)
        {
            return;
        }

        var item = list.Items[args.Index];
        var text = item is StageTaskItem task ? task.Text : item?.ToString() ?? string.Empty;
        var color = item is StageTaskItem highlightedTask && highlightedTask.Highlight
            ? Color.FromArgb(181, 54, 57)
            : args.ForeColor;

        TextRenderer.DrawText(args.Graphics, text, args.Font, args.Bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        args.DrawFocusRectangle();
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
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 8),
            Padding = new Padding(12),
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private static void PaintBeveledCard(Graphics graphics, Rectangle bounds, Color baseColor)
    {
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            return;
        }

        var fillArea = Rectangle.Inflate(bounds, -1, -1);
        using var brush = new LinearGradientBrush(fillArea, ControlPaint.Light(baseColor, 0.35f), ControlPaint.Dark(baseColor, 0.18f), LinearGradientMode.Vertical);
        graphics.FillRectangle(brush, fillArea);
        using var topHighlight = new Pen(Color.FromArgb(170, Color.White));
        using var outerBorder = new Pen(ControlPaint.Dark(baseColor, 0.35f));
        using var innerShadow = new Pen(Color.FromArgb(120, ControlPaint.Dark(baseColor, 0.1f)));
        graphics.DrawLine(topHighlight, fillArea.Left, fillArea.Top, fillArea.Right, fillArea.Top);
        graphics.DrawLine(topHighlight, fillArea.Left, fillArea.Top, fillArea.Left, fillArea.Bottom);
        graphics.DrawRectangle(outerBorder, fillArea);
        graphics.DrawRectangle(innerShadow, Rectangle.Inflate(fillArea, -1, -1));
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

    private sealed class StageTaskItem
    {
        public StageTaskItem(string text, DashboardNavigationTarget target, bool highlight = false)
        {
            Text = text;
            Target = target;
            Highlight = highlight;
        }

        public string Text { get; }

        public DashboardNavigationTarget Target { get; }

        public bool Highlight { get; }

        public override string ToString() => Text;
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
