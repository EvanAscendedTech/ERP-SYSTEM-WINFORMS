using ERPSystem.WinForms.Controls;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Forms;

public class ERPMainForm : Form
{
    private readonly TabControl _sectionsTabs = new() { Dock = DockStyle.Fill };
    private readonly HomeControl _homeControl;

    public ERPMainForm(
        QuoteRepository quoteRepository,
        ProductionRepository productionRepository,
        UserManagementRepository userRepository,
        AppSettingsService appSettingsService,
        bool canManageSettings,
        string companyName)
    {
        Text = "ERP MainShell";
        Width = 1280;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;

        _homeControl = new HomeControl(companyName);
        BuildSections(quoteRepository, productionRepository, userRepository, appSettingsService, canManageSettings);

        Controls.Add(_sectionsTabs);
    }

    private void BuildSections(
        QuoteRepository quoteRepository,
        ProductionRepository productionRepository,
        UserManagementRepository userRepository,
        AppSettingsService appSettingsService,
        bool canManageSettings)
    {
        _sectionsTabs.TabPages.Add(new TabPage("Home") { Controls = { _homeControl } });
        _sectionsTabs.TabPages.Add(new TabPage("Quotes") { Controls = { new QuotesControl(quoteRepository) } });
        _sectionsTabs.TabPages.Add(new TabPage("Production") { Controls = { new ProductionControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Inspection") { Controls = { new InspectionControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Shipping") { Controls = { new ShippingControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Settings") { Controls = { new SettingsControl(appSettingsService, canManageSettings, _homeControl.UpdateCompanyName) } });
        _sectionsTabs.TabPages.Add(new TabPage("Users") { Controls = { new UsersControl() } });

        _sectionsTabs.SelectedIndex = 0;
    }
}
