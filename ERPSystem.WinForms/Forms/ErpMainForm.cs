using ERPSystem.WinForms.Controls;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Forms;

public class ErpMainForm : Form
{
    private readonly TabControl _sectionsTabs = new() { Dock = DockStyle.Fill };

    public ErpMainForm(
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
        BuildSections(productionRepository, userRepository, settingsService, inspectionService, archiveService);

        Controls.Add(_sectionsTabs);
    }

    private void BuildNavigation()
    {
        var menu = new MenuStrip();
        var sections = new ToolStripMenuItem("Sections");

        sections.DropDownItems.Add(CreateSectionMenuItem("Production", 0));
        sections.DropDownItems.Add(CreateSectionMenuItem("Inspection", 1));
        sections.DropDownItems.Add(CreateSectionMenuItem("Archive", 2));
        sections.DropDownItems.Add(CreateSectionMenuItem("Settings", 3));

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
        ProductionRepository productionRepository,
        UserManagementRepository userRepository,
        AppSettingsService settingsService,
        InspectionService inspectionService,
        ArchiveService archiveService)
    {
        _sectionsTabs.TabPages.Add(new TabPage("Production") { Controls = { new ProductionControl(productionRepository) } });
        _sectionsTabs.TabPages.Add(new TabPage("Inspection") { Controls = { new InspectionControl(inspectionService) } });
        _sectionsTabs.TabPages.Add(new TabPage("Archive") { Controls = { new ArchiveControl(archiveService) } });
        _sectionsTabs.TabPages.Add(new TabPage("Settings") { Controls = { new SettingsControl(settingsService, userRepository) } });
    }
}
