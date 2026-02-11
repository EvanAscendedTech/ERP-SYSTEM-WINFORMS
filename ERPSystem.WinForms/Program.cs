using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Forms;

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

        var quoteRepository = new QuoteRepository(dbPath);
        await quoteRepository.InitializeDatabaseAsync();

        var productionRepository = new ProductionRepository(dbPath);
        await productionRepository.InitializeDatabaseAsync();

        var userRepository = new UserManagementRepository(dbPath);
        await userRepository.InitializeDatabaseAsync();

        using var loginForm = new LoginForm();
        if (loginForm.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        Application.Run(new ERPMainForm(quoteRepository, productionRepository, userRepository));
    }
}
