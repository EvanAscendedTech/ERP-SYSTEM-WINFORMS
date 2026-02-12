using ERPSystem.WinForms.Controls;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Forms;

public class ERPMainForm : Form
{
    private readonly TabControl _sectionsTabs = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel _onlineUsersPanel = new() { Dock = DockStyle.Top, Height = 56, Padding = new Padding(8), AutoScroll = true, WrapContents = false };
    private readonly UserManagementRepository _userRepository;
    private readonly UserAccount _currentUser;
    private readonly HomeControl _homeControl;

    public ERPMainForm(
        QuoteRepository quoteRepository,
        ProductionRepository productionRepository,
        UserManagementRepository userRepository,
        AppSettingsService appSettingsService,
        UserAccount currentUser,
        string companyName)
    {
        _userRepository = userRepository;
        _currentUser = currentUser;

        Text = "ERP MainShell";
        Width = 1280;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;

        _homeControl = new HomeControl(companyName, SelectSection);
        BuildSections(quoteRepository, productionRepository, userRepository, appSettingsService, currentUser);

        Controls.Add(_sectionsTabs);
        Controls.Add(_onlineUsersPanel);

        _ = RefreshOnlineUsersAsync();
    }

    private void BuildSections(
        QuoteRepository quoteRepository,
        ProductionRepository productionRepository,
        UserManagementRepository userRepository,
        AppSettingsService appSettingsService,
        UserAccount currentUser)
    {
        _sectionsTabs.TabPages.Add(new TabPage("Home") { Controls = { _homeControl } });
        _sectionsTabs.TabPages.Add(new TabPage("Quotes") { Controls = { new QuotesControl(quoteRepository, productionRepository, SelectSection) } });
        _sectionsTabs.TabPages.Add(new TabPage("Production") { Controls = { new ProductionControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Inspection") { Controls = { new InspectionControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Shipping") { Controls = { new ShippingControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Quality") { Controls = { new QualityControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Performance") { Controls = { new PerformanceControl() } });
        _sectionsTabs.TabPages.Add(new TabPage("Settings") { Controls = { new SettingsControl(appSettingsService, AuthorizationService.HasPermission(currentUser, UserPermission.ManageSettings), _homeControl.UpdateCompanyName) } });
        _sectionsTabs.TabPages.Add(new TabPage("Users") { Controls = { new UsersControl(userRepository, currentUser, async () => await RefreshOnlineUsersAsync()) } });

        _sectionsTabs.SelectedIndex = 0;
    }

    private void SelectSection(string sectionName)
    {
        foreach (TabPage tab in _sectionsTabs.TabPages)
        {
            if (string.Equals(tab.Text, sectionName, StringComparison.OrdinalIgnoreCase))
            {
                _sectionsTabs.SelectedTab = tab;
                return;
            }
        }
    }

    private async Task RefreshOnlineUsersAsync()
    {
        var users = (await _userRepository.GetUsersAsync()).Where(x => x.IsActive).ToList();

        _onlineUsersPanel.Controls.Clear();
        foreach (var user in users)
        {
            _onlineUsersPanel.Controls.Add(BuildOnlineUserBadge(user));
        }
    }

    private static Control BuildOnlineUserBadge(UserAccount user)
    {
        var container = new FlowLayoutPanel { Width = 180, Height = 40, FlowDirection = FlowDirection.LeftToRight };
        var icon = new PictureBox { Width = 24, Height = 24, SizeMode = PictureBoxSizeMode.Zoom };
        if (!string.IsNullOrWhiteSpace(user.IconPath) && File.Exists(user.IconPath))
        {
            icon.Image = Image.FromFile(user.IconPath);
        }

        var statusDot = new Label { Text = "●", AutoSize = true, ForeColor = Color.LimeGreen, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(3, 5, 3, 0) };
        var name = new Label { Text = user.Username, AutoSize = true, Margin = new Padding(3, 6, 3, 0) };

        container.Controls.Add(icon);
        container.Controls.Add(statusDot);
        container.Controls.Add(name);
        return container;
    }
}
