using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Forms;
using ERPSystem.WinForms.Models;
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
        await EnsureDefaultAdminAsync(userRepository);

        var appSettingsService = new AppSettingsService(settingsPath);
        var appSettings = await appSettingsService.LoadAsync();

        using var loginForm = new LoginForm(userRepository);
        if (loginForm.ShowDialog() != DialogResult.OK || loginForm.AuthenticatedUser is null)
        {
            return;
        }

        Application.Run(new ERPMainForm(
            quoteRepository,
            productionRepository,
            userRepository,
            appSettingsService,
            loginForm.AuthenticatedUser,
            appSettings.CompanyName));
    }

    private static async Task EnsureDefaultAdminAsync(UserManagementRepository userRepository)
    {
        var existingAdmin = await userRepository.FindByUsernameAsync("ASTECH");
        if (existingAdmin is not null)
        {
            return;
        }

        await userRepository.SaveUserAsync(new UserAccount
        {
            Username = "ASTECH",
            DisplayName = "Admin",
            PasswordHash = AuthorizationService.HashPassword("ASTECH123!"),
            IsActive = true,
            Roles =
            [
                new RoleDefinition
                {
                    Name = "Administrator",
                    Permissions = Enum.GetValues<UserPermission>().ToList()
                }
            ]
        });
    }
}
