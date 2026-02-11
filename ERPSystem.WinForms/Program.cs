using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Forms;

namespace ERPSystem.WinForms;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        ApplicationConfiguration.Initialize();

        var dbPath = Path.Combine(AppContext.BaseDirectory, "erp_quotes.db");
        var quoteRepository = new QuoteRepository(dbPath);
        await quoteRepository.InitializeDatabaseAsync();

        Application.Run(new QuotingBoardForm(quoteRepository));
    }
}
