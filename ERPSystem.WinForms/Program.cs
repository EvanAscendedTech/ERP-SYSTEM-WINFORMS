using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Forms;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        ApplicationConfiguration.Initialize();

        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ERPSystem.WinForms");
        Directory.CreateDirectory(appDataFolder);

        var dbPath = Path.Combine(appDataFolder, "erp_system.db");
        var settingsPath = Path.Combine(appDataFolder, "appsettings.json");

        var quoteRepository = new QuoteRepository(dbPath);
        await quoteRepository.InitializeDatabaseAsync();

        var productionRepository = new ProductionRepository(dbPath);
        await productionRepository.InitializeDatabaseAsync();

        var userRepository = new UserManagementRepository(dbPath);
        await userRepository.InitializeDatabaseAsync();

        var appSettingsService = new AppSettingsService(settingsPath);
        var appSettings = await appSettingsService.LoadAsync();

        using var loginForm = new LoginForm();
        if (loginForm.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var canManageSettings = string.Equals(loginForm.EnteredUsername, "admin", StringComparison.OrdinalIgnoreCase);

        Application.Run(new ERPMainForm(
            quoteRepository,
            productionRepository,
            userRepository,
            appSettingsService,
            canManageSettings,
            appSettings.CompanyName));
    }
}
