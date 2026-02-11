using ERPSystem.WinForms.Controls;
using ERPSystem.WinForms.Data;

namespace ERPSystem.WinForms.Forms;

public class ERPMainForm : Form
{
    private readonly TabControl _sectionsTabs = new() { Dock = DockStyle.Fill };

    public ERPMainForm(
        QuoteRepository quoteRepository,
        ProductionRepository productionRepository,
        UserManagementRepository userRepository)
    {
        Text = "ERP MainShell";
        Width = 1280;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;

        BuildSections(quoteRepository, productionRepository, userRepository);

        Controls.Add(_sectionsTabs);
    }

    private void BuildSections(
        QuoteRepository quoteRepository,
        ProductionRepository productionRepository,
        UserManagementRepository userRepository)
    {
        _sectionsTabs.TabPages.Add(new TabPage("Quotes") { Controls = { new QuotesControl(quoteRepository) } });
        _sectionsTabs.TabPages.Add(new TabPage("Production") { Controls = { new ProductionControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Inspection") { Controls = { new InspectionControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Archive") { Controls = { new ArchiveControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Settings") { Controls = { new SettingsControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Users") { Controls = { new UsersControl() } });

        _sectionsTabs.SelectedIndex = 0;
    }
}
