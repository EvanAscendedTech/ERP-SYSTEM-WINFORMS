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
    public async Task SaveQuoteAsync_CreatesAuditEvent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-audit-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var quote = new Quote
        {
            CustomerName = "Audit Customer",
            Status = QuoteStatus.InProgress,
            LineItems =
            [
                new QuoteLineItem { Description = "Part A", Quantity = 2 },
                new QuoteLineItem { Description = "Part B", Quantity = 1 }
            ]
        };

        var id = await repository.SaveQuoteAsync(quote);

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT EventType, OperationMode, LineItemCount
            FROM QuoteAuditEvents
            WHERE QuoteId = $id
            ORDER BY Id DESC
            LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("save", reader.GetString(0));
        Assert.Equal("create", reader.GetString(1));
        Assert.Equal(2, reader.GetInt32(2));

        File.Delete(dbPath);
    }

    [Fact]
    public async Task UpdateStatusAsync_CreatesStatusTransitionAndArchiveEvents()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-status-audit-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var quote = new Quote
        {
            CustomerName = "Transition Customer",
            Status = QuoteStatus.InProgress,
            LineItems = [new QuoteLineItem { Description = "Bracket", Quantity = 1 }]
        };

        var id = await repository.SaveQuoteAsync(quote);
        var updated = await repository.UpdateStatusAsync(id, QuoteStatus.Won);

        Assert.True(updated);

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        var transitionCount = await CountEventsAsync(connection, id, "status_transition");
        var archiveCount = await CountEventsAsync(connection, id, "archive");

        Assert.Equal(1, transitionCount);
        Assert.Equal(1, archiveCount);

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

    private static async Task<int> CountEventsAsync(SqliteConnection connection, int quoteId, string eventType)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM QuoteAuditEvents
            WHERE QuoteId = $id AND EventType = $eventType;";
        command.Parameters.AddWithValue("$id", quoteId);
        command.Parameters.AddWithValue("$eventType", eventType);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
