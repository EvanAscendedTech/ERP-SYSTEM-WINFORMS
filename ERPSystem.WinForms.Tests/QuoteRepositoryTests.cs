using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Tests;

public class QuoteRepositoryTests
{
    [Fact]
    public async Task SaveQuoteAsync_PersistsExtendedLineItemFields()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var quote = new Quote
        {
            CustomerName = "Acme Aerospace",
            Status = QuoteStatus.InProgress,
            LineItems =
            [
                new QuoteLineItem
                {
                    Description = "Housing",
                    Quantity = 5,
                    UnitPrice = 125.50m,
                    LeadTimeDays = 21,
                    RequiresGForce = true,
                    RequiresSecondaryProcessing = true,
                    RequiresPlating = false,
                    AssociatedFiles = ["drawing-1.pdf", "model-1.step"]
                }
            ]
        };

        var id = await repository.SaveQuoteAsync(quote);
        var loaded = await repository.GetQuoteAsync(id);

        Assert.NotNull(loaded);
        var lineItem = Assert.Single(loaded!.LineItems);
        Assert.Equal(125.50m, lineItem.UnitPrice);
        Assert.Equal(21, lineItem.LeadTimeDays);
        Assert.True(lineItem.RequiresGForce);
        Assert.True(lineItem.RequiresSecondaryProcessing);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task ExpireStaleQuotesAsync_MarksOldInProgressQuotesAsExpired()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-expire-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var quote = new Quote
        {
            CustomerName = "Legacy Customer",
            Status = QuoteStatus.InProgress,
            CreatedUtc = DateTime.UtcNow.AddDays(-90),
            LastUpdatedUtc = DateTime.UtcNow.AddDays(-90),
            LineItems = [new QuoteLineItem { Description = "Bracket", Quantity = 1 }]
        };

        var id = await repository.SaveQuoteAsync(quote);

        var stale = await repository.GetQuoteAsync(id);
        stale!.LastUpdatedUtc = DateTime.UtcNow.AddDays(-90);
        await repository.SaveQuoteAsync(stale);

        var affected = await repository.ExpireStaleQuotesAsync(TimeSpan.FromDays(60));
        var expired = await repository.GetQuoteAsync(id);

        Assert.Equal(1, affected);
        Assert.Equal(QuoteStatus.Expired, expired!.Status);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task SaveQuoteAsync_WithNonExistentNonZeroId_ThrowsClearValidationError()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-missing-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var quote = new Quote
        {
            Id = 999999,
            CustomerName = "Unknown Customer",
            Status = QuoteStatus.InProgress,
            LineItems = [new QuoteLineItem { Description = "Bracket", Quantity = 1 }]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveQuoteAsync(quote));
        Assert.Equal("Quote 999999 not found. Load an existing quote or create a new one.", exception.Message);

        File.Delete(dbPath);
    }
}
