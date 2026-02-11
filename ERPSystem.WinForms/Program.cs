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

        var dbPath = Path.Combine(AppContext.BaseDirectory, "erp_system.db");
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings", "appsettings.json");

        var productionRepository = new ProductionRepository(dbPath);
        await productionRepository.InitializeDatabaseAsync();

        var userRepository = new UserManagementRepository(dbPath);
        await userRepository.InitializeDatabaseAsync();

        var settingsService = new AppSettingsService(settingsPath);
        var inspectionService = new InspectionService();
        var archiveService = new ArchiveService();

        Application.Run(new ErpMainForm(productionRepository, userRepository, settingsService, inspectionService, archiveService));
    }
}
