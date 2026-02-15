using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using Microsoft.Data.Sqlite;

namespace ERPSystem.WinForms.Tests;

public class QuoteRepositoryTests
{

    [Fact]
    public async Task SaveQuoteAsync_PersistsLifecycleIdAndBlobAttachments()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-blob-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var customer = Assert.Single(await repository.GetCustomersAsync());

        var quote = new Quote
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            LifecycleQuoteId = "Q-TEST-001",
            Status = QuoteStatus.InProgress,
            LineItems =
            [
                new QuoteLineItem
                {
                    Description = "Bracket",
                    Quantity = 1,
                    BlobAttachments =
                    [
                        new QuoteBlobAttachment
                        {
                            BlobType = QuoteBlobType.Technical,
                            FileName = "spec.pdf",
                            ContentType = ".pdf",
                            BlobData = [1,2,3],
                            UploadedUtc = DateTime.UtcNow
                        }
                    ]
                }
            ]
        };

        var id = await repository.SaveQuoteAsync(quote);
        var loaded = await repository.GetQuoteAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("Q-TEST-001", loaded!.LifecycleQuoteId);
        var line = Assert.Single(loaded.LineItems);
        var blob = Assert.Single(line.BlobAttachments);
        Assert.Equal(QuoteBlobType.Technical, blob.BlobType);

        File.Delete(dbPath);
    }

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
                    Notes = "Need anodized finish and marked revision B.",
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
        Assert.Equal("Need anodized finish and marked revision B.", lineItem.Notes);

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
    public async Task PassToPurchasingAsync_RequiresWonAndSetsPurchasingFields()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-purchasing-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var customer = Assert.Single(await repository.GetCustomersAsync());
        var quote = new Quote
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            LifecycleQuoteId = "Q-PUR-001",
            Status = QuoteStatus.Won,
            LineItems = [new QuoteLineItem { Description = "Part", Quantity = 1 }]
        };

        var id = await repository.SaveQuoteAsync(quote);
        var result = await repository.PassToPurchasingAsync(id, "buyer.user");

        Assert.True(result.Success);

        var loaded = await repository.GetQuoteAsync(id);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.PassedToPurchasingUtc);
        Assert.Equal("buyer.user", loaded.PassedToPurchasingByUserId);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task PassToPurchasingAsync_FailsWhenQuoteNotWon()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-purchasing-invalid-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var customer = Assert.Single(await repository.GetCustomersAsync());
        var quote = new Quote
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            LifecycleQuoteId = "Q-PUR-002",
            Status = QuoteStatus.Completed,
            LineItems = [new QuoteLineItem { Description = "Part", Quantity = 1 }]
        };

        var id = await repository.SaveQuoteAsync(quote);
        var result = await repository.PassToPurchasingAsync(id, "buyer.user");

        Assert.False(result.Success);

        var loaded = await repository.GetQuoteAsync(id);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.PassedToPurchasingUtc);

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

    [Fact]
    public async Task DeleteQuoteAsync_ArchivesWonQuoteThenDeletesActiveRecords()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-delete-won-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var customer = Assert.Single(await repository.GetCustomersAsync());
        var quote = new Quote
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            LifecycleQuoteId = "Q-WON-DELETE",
            Status = QuoteStatus.Won,
            CreatedUtc = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow,
            WonUtc = DateTime.UtcNow,
            WonByUserId = "tester",
            LineItems =
            [
                new QuoteLineItem
                {
                    Description = "Won assembly",
                    Quantity = 1,
                    AssociatedFiles = ["drawing.pdf"],
                    BlobAttachments =
                    [
                        new QuoteBlobAttachment
                        {
                            BlobType = QuoteBlobType.Technical,
                            FileName = "spec.pdf",
                            ContentType = ".pdf",
                            BlobData = [10,20,30],
                            UploadedUtc = DateTime.UtcNow
                        }
                    ]
                }
            ]
        };

        var id = await repository.SaveQuoteAsync(quote);
        await repository.DeleteQuoteAsync(id);

        var deleted = await repository.GetQuoteAsync(id);
        Assert.Null(deleted);

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        Assert.Equal(0, await CountByQuoteIdAsync(connection, "QuoteLineItems", id));
        Assert.Equal(0, await CountByQuoteIdAsync(connection, "QuoteBlobFiles", id));
        Assert.Equal(0, await CountByQuoteIdAsync(connection, "QuoteAuditEvents", id));

        await using (var archived = connection.CreateCommand())
        {
            archived.CommandText = @"
                SELECT COUNT(*)
                FROM ArchivedQuotes
                WHERE OriginalQuoteId = $quoteId AND Status = $status;";
            archived.Parameters.AddWithValue("$quoteId", id);
            archived.Parameters.AddWithValue("$status", (int)QuoteStatus.Won);
            var archiveCount = Convert.ToInt32(await archived.ExecuteScalarAsync());
            Assert.Equal(1, archiveCount);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                SELECT COUNT(*)
                FROM LineItemFiles
                WHERE LineItemId IN (SELECT Id FROM QuoteLineItems WHERE QuoteId = $quoteId);";
            command.Parameters.AddWithValue("$quoteId", id);
            var lineItemFileCount = Convert.ToInt32(await command.ExecuteScalarAsync());
            Assert.Equal(0, lineItemFileCount);
        }

        File.Delete(dbPath);
    }

    [Fact]
    public async Task DeleteQuoteAsync_WhenQuoteMissing_ThrowsClearError()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-delete-missing-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteQuoteAsync(123456));
        Assert.Equal("Quote 123456 was not found.", exception.Message);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task DeleteQuoteAsync_DeletesInProgressQuoteWithoutArchiving()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-delete-active-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var customer = Assert.Single(await repository.GetCustomersAsync());
        var quote = new Quote
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            LifecycleQuoteId = "Q-ACTIVE-DELETE",
            Status = QuoteStatus.InProgress,
            LineItems = [new QuoteLineItem { Description = "Active assembly", Quantity = 2 }]
        };

        var id = await repository.SaveQuoteAsync(quote);
        await repository.DeleteQuoteAsync(id);

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var archived = connection.CreateCommand();
        archived.CommandText = "SELECT COUNT(*) FROM ArchivedQuotes WHERE OriginalQuoteId = $quoteId;";
        archived.Parameters.AddWithValue("$quoteId", id);
        var archiveCount = Convert.ToInt32(await archived.ExecuteScalarAsync());
        Assert.Equal(0, archiveCount);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task ArchivedQuoteReadback_ReturnsArchivedLineItemsAndBlobs()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-archive-read-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var customer = Assert.Single(await repository.GetCustomersAsync());
        var quote = new Quote
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            LifecycleQuoteId = "Q-COMPLETE-ARCHIVE",
            Status = QuoteStatus.Completed,
            CompletedUtc = DateTime.UtcNow,
            CompletedByUserId = "qa",
            LineItems =
            [
                new QuoteLineItem
                {
                    Description = "Completed assembly",
                    Quantity = 1,
                    AssociatedFiles = ["complete.pdf"],
                    BlobAttachments =
                    [
                        new QuoteBlobAttachment
                        {
                            BlobType = QuoteBlobType.Technical,
                            FileName = "complete-spec.pdf",
                            ContentType = ".pdf",
                            BlobData = [1,2,3],
                            UploadedUtc = DateTime.UtcNow
                        }
                    ]
                }
            ]
        };

        var id = await repository.SaveQuoteAsync(quote);
        await repository.DeleteQuoteAsync(id);

        var archivedSummaries = await repository.GetArchivedQuotesAsync();
        var archivedSummary = Assert.Single(archivedSummaries.Where(q => q.OriginalQuoteId == id));
        var archivedQuote = await repository.GetArchivedQuoteAsync(archivedSummary.ArchiveId);

        Assert.NotNull(archivedQuote);
        Assert.Equal(QuoteStatus.Completed, archivedQuote!.Status);
        var line = Assert.Single(archivedQuote.LineItems);
        Assert.Contains("complete.pdf", line.AssociatedFiles);
        Assert.Single(line.BlobAttachments);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task InitializeDatabaseAsync_BackfillSkipsUsedLegacyCodes()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-backfill-codes-{Guid.NewGuid():N}.db");

        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE Customers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE Quotes (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerName TEXT NOT NULL,
                    Status INTEGER NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    LastUpdatedUtc TEXT NOT NULL
                );

                INSERT INTO Customers (Code, Name, IsActive)
                VALUES ('LEG-00001', 'Existing Legacy Customer', 1);

                INSERT INTO Quotes (CustomerName, Status, CreatedUtc, LastUpdatedUtc)
                VALUES ('New Legacy Customer', 0, '2024-01-01T00:00:00.0000000Z', '2024-01-01T00:00:00.0000000Z');";
            await command.ExecuteNonQueryAsync();
        }

        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var customers = await repository.GetCustomersAsync(activeOnly: false);
        var created = Assert.Single(customers.Where(c => c.Name == "New Legacy Customer"));
        Assert.Equal("LEG-00002", created.Code);

        File.Delete(dbPath);
    }


    [Fact]
    public async Task UpdateStatusAsync_ToCompleted_SetsCompletedAuditFields()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-quote-status-completed-{Guid.NewGuid():N}.db");
        var repository = new QuoteRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var quote = new Quote
        {
            CustomerName = "Completed Customer",
            Status = QuoteStatus.InProgress,
            LineItems = [new QuoteLineItem { Description = "Bracket", Quantity = 1 }]
        };

        var id = await repository.SaveQuoteAsync(quote);
        var update = await repository.UpdateStatusAsync(id, QuoteStatus.Completed, "qa.user");
        var loaded = await repository.GetQuoteAsync(id);

        Assert.True(update.Success);
        Assert.NotNull(loaded);
        Assert.Equal(QuoteStatus.Completed, loaded!.Status);
        Assert.NotNull(loaded.CompletedUtc);
        Assert.Equal("qa.user", loaded.CompletedByUserId);

        File.Delete(dbPath);
    }

    private static async Task<int> CountByQuoteIdAsync(SqliteConnection connection, string tableName, int quoteId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE QuoteId = $quoteId;";
        command.Parameters.AddWithValue("$quoteId", quoteId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

}
