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
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        Application.ThreadException += OnApplicationThreadException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
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
            _ = await appSettingsService.LoadAsync();

            using var loginForm = new LoginForm(userRepository);
            if (loginForm.ShowDialog() != DialogResult.OK || loginForm.AuthenticatedUser is null)
            {
                return;
            }

            var inspectionService = new InspectionService();
            var archiveService = new ArchiveService();

            Application.Run(new ERPMainForm(
                quoteRepository,
                productionRepository,
                userRepository,
                appSettingsService,
                inspectionService,
                archiveService));
        }
        catch (Exception ex)
        {
            ShowFatalStartupError("Startup failure", ex);
            throw;
        }
    }

    private static void OnApplicationThreadException(object sender, ThreadExceptionEventArgs args)
        => ShowFatalStartupError("UI thread exception", args.Exception);

    private static void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs args)
    {
        var ex = args.ExceptionObject as Exception
                 ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown unhandled exception.");

        ShowFatalStartupError("Unhandled domain exception", ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        ShowFatalStartupError("Background task exception", args.Exception);
        args.SetObserved();
    }

    private static void ShowFatalStartupError(string title, Exception ex)
    {
        MessageBox.Show(
            $"{title}.{Environment.NewLine}{Environment.NewLine}{ex}",
            "ERP System Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
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
