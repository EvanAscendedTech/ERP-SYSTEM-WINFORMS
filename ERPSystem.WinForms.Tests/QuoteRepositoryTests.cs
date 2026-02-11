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
    public async Task SaveQuoteAsync_WithUnsetId_CreatesThenUpdatesSameQuote()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-create-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var quote = new Quote
        {
            Id = 0,
            CustomerName = "Create Mode Customer",
            Status = QuoteStatus.InProgress,
            LineItems = [new QuoteLineItem { Description = "Panel", Quantity = 2 }]
        };

        var createdId = await repository.SaveQuoteAsync(quote);
        Assert.True(createdId > 0);

        quote.Id = createdId;
        quote.CustomerName = "Updated Customer";
        quote.Status = QuoteStatus.Won;

        var updatedId = await repository.SaveQuoteAsync(quote);
        var loaded = await repository.GetQuoteAsync(createdId);

        Assert.Equal(createdId, updatedId);
        Assert.NotNull(loaded);
        Assert.Equal("Updated Customer", loaded!.CustomerName);
        Assert.Equal(QuoteStatus.Won, loaded.Status);

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
