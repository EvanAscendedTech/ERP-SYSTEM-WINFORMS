using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using Microsoft.Data.Sqlite;

namespace ERPSystem.WinForms.Tests;

public class QuoteRepositoryTests
{
    [Fact]
    public async Task SaveQuoteAsync_PersistsExtendedLineItemFields()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var customer = Assert.Single(await repository.GetCustomersAsync());

        var quote = new Quote
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
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
        Assert.Equal(customer.Id, loaded!.CustomerId);
        Assert.Equal(customer.Name, loaded.CustomerName);

        var lineItem = Assert.Single(loaded.LineItems);
        Assert.Equal(125.50m, lineItem.UnitPrice);
        Assert.Equal(21, lineItem.LeadTimeDays);
        Assert.True(lineItem.RequiresGForce);
        Assert.True(lineItem.RequiresSecondaryProcessing);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task InitializeDatabaseAsync_BackfillsLegacyCustomerNameIntoCustomers()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-backfill-{Guid.NewGuid():N}.db");

        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE Quotes (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerName TEXT NOT NULL,
                    Status INTEGER NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    LastUpdatedUtc TEXT NOT NULL
                );

                INSERT INTO Quotes (CustomerName, Status, CreatedUtc, LastUpdatedUtc)
                VALUES ('Legacy Customer', 0, '2024-01-01T00:00:00.0000000Z', '2024-01-01T00:00:00.0000000Z');";
            await command.ExecuteNonQueryAsync();
        }

        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var customers = await repository.GetCustomersAsync();
        var customer = Assert.Single(customers);
        Assert.Equal("Legacy Customer", customer.Name);
        Assert.StartsWith("LEG-", customer.Code);

        var quote = await repository.GetQuoteAsync(1);
        Assert.NotNull(quote);
        Assert.Equal(customer.Id, quote!.CustomerId);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task ExpireStaleQuotesAsync_MarksOldInProgressQuotesAsExpired()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-expire-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var customer = Assert.Single(await repository.GetCustomersAsync());

        var quote = new Quote
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
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
}
