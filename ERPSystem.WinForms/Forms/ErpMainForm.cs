using ERPSystem.WinForms.Controls;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Forms;

public class ErpMainForm : Form
{
    private readonly TabControl _sectionsTabs = new() { Dock = DockStyle.Fill };

    public ErpMainForm(
        QuoteRepository quoteRepository,
        ProductionRepository productionRepository,
        UserManagementRepository userRepository,
        AppSettingsService settingsService,
        InspectionService inspectionService,
        ArchiveService archiveService)
    {
        Text = "ERP Operations Console";
        Width = 1280;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;

        BuildNavigation();
        BuildSections(quoteRepository, productionRepository, userRepository, settingsService, inspectionService, archiveService);

        Controls.Add(_sectionsTabs);
    }

    private void BuildNavigation()
    {
        var menu = new MenuStrip();
        var sections = new ToolStripMenuItem("Sections");

        sections.DropDownItems.Add(CreateSectionMenuItem("Quoting", 0));
        sections.DropDownItems.Add(CreateSectionMenuItem("Production", 1));
        sections.DropDownItems.Add(CreateSectionMenuItem("Inspection", 2));
        sections.DropDownItems.Add(CreateSectionMenuItem("Archive", 3));
        sections.DropDownItems.Add(CreateSectionMenuItem("Settings", 4));

        menu.Items.Add(sections);
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    private ToolStripMenuItem CreateSectionMenuItem(string text, int targetIndex)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => _sectionsTabs.SelectedIndex = targetIndex;
        return item;
    }

    private void BuildSections(
        QuoteRepository quoteRepository,
        ProductionRepository productionRepository,
        UserManagementRepository userRepository,
        AppSettingsService settingsService,
        InspectionService inspectionService,
        ArchiveService archiveService)
    {
        var quotingHost = new Panel { Dock = DockStyle.Fill };
        var quoting = new QuotingBoardForm(quoteRepository)
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            Dock = DockStyle.Fill
        };
        quotingHost.Controls.Add(quoting);
        quoting.Show();

        _sectionsTabs.TabPages.Add(new TabPage("Quoting") { Controls = { quotingHost } });
        _sectionsTabs.TabPages.Add(new TabPage("Production") { Controls = { new ProductionControl(productionRepository) } });
        _sectionsTabs.TabPages.Add(new TabPage("Inspection") { Controls = { new InspectionControl(inspectionService) } });
        _sectionsTabs.TabPages.Add(new TabPage("Archive") { Controls = { new ArchiveControl(archiveService) } });
        _sectionsTabs.TabPages.Add(new TabPage("Settings") { Controls = { new SettingsControl(settingsService, userRepository) } });
    }
}
