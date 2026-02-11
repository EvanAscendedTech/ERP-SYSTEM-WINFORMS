using System.Diagnostics;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using Microsoft.Data.Sqlite;

namespace ERPSystem.WinForms.Data;

public class QuoteRepository
{
    private readonly string _connectionString;

    public QuoteRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }

    public async Task InitializeDatabaseAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            CREATE TABLE IF NOT EXISTS Quotes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerName TEXT NOT NULL,
                Status INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                LastUpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS QuoteLineItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                QuoteId INTEGER NOT NULL,
                Description TEXT NOT NULL,
                Quantity REAL NOT NULL,
                UnitPrice REAL NOT NULL DEFAULT 0,
                LeadTimeDays INTEGER NOT NULL DEFAULT 0,
                RequiresGForce INTEGER NOT NULL DEFAULT 0,
                RequiresSecondaryProcessing INTEGER NOT NULL DEFAULT 0,
                RequiresPlating INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(QuoteId) REFERENCES Quotes(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS LineItemFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LineItemId INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                FOREIGN KEY(LineItemId) REFERENCES QuoteLineItems(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS QuoteAuditEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                QuoteId INTEGER,
                EventType TEXT NOT NULL,
                OperationMode TEXT NOT NULL,
                LineItemCount INTEGER NOT NULL DEFAULT 0,
                Details TEXT,
                CreatedUtc TEXT NOT NULL
            );";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();

        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "UnitPrice", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "LeadTimeDays", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresGForce", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresSecondaryProcessing", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresPlating", "INTEGER NOT NULL DEFAULT 0");
    }

    public async Task<int> SaveQuoteAsync(Quote quote)
    {
        var operationMode = quote.Id == 0 ? "create" : "update";
        var requestedId = quote.Id;
        var lineItemCount = quote.LineItems.Count;

        try
        {
            quote.LastUpdatedUtc = DateTime.UtcNow;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

            if (quote.Id == 0)
            {
                quote.CreatedUtc = quote.LastUpdatedUtc;
                await using var insertQuote = connection.CreateCommand();
                insertQuote.Transaction = transaction;
                insertQuote.CommandText = @"
                    INSERT INTO Quotes (CustomerName, Status, CreatedUtc, LastUpdatedUtc)
                    VALUES ($name, $status, $created, $updated);
                    SELECT last_insert_rowid();";
                insertQuote.Parameters.AddWithValue("$name", quote.CustomerName);
                insertQuote.Parameters.AddWithValue("$status", (int)quote.Status);
                insertQuote.Parameters.AddWithValue("$created", quote.CreatedUtc.ToString("O"));
                insertQuote.Parameters.AddWithValue("$updated", quote.LastUpdatedUtc.ToString("O"));

                quote.Id = Convert.ToInt32(await insertQuote.ExecuteScalarAsync());
            }
            else
            {
                await using var updateQuote = connection.CreateCommand();
                updateQuote.Transaction = transaction;
                updateQuote.CommandText = @"
                    UPDATE Quotes
                    SET CustomerName = $name,
                        Status = $status,
                        LastUpdatedUtc = $updated
                    WHERE Id = $id;";
                updateQuote.Parameters.AddWithValue("$id", quote.Id);
                updateQuote.Parameters.AddWithValue("$name", quote.CustomerName);
                updateQuote.Parameters.AddWithValue("$status", (int)quote.Status);
                updateQuote.Parameters.AddWithValue("$updated", quote.LastUpdatedUtc.ToString("O"));
                await updateQuote.ExecuteNonQueryAsync();

                await DeleteLineItemsForQuoteAsync(connection, transaction, quote.Id);
            }

            foreach (var lineItem in quote.LineItems)
            {
                await using var insertLineItem = connection.CreateCommand();
                insertLineItem.Transaction = transaction;
                insertLineItem.CommandText = @"
                    INSERT INTO QuoteLineItems (
                        QuoteId,
                        Description,
                        Quantity,
                        UnitPrice,
                        LeadTimeDays,
                        RequiresGForce,
                        RequiresSecondaryProcessing,
                        RequiresPlating)
                    VALUES ($quoteId, $description, $qty, $unitPrice, $leadTimeDays, $requiresGForce, $requiresSecondary, $requiresPlating);
                    SELECT last_insert_rowid();";
                insertLineItem.Parameters.AddWithValue("$quoteId", quote.Id);
                insertLineItem.Parameters.AddWithValue("$description", lineItem.Description);
                insertLineItem.Parameters.AddWithValue("$qty", lineItem.Quantity);
                insertLineItem.Parameters.AddWithValue("$unitPrice", lineItem.UnitPrice);
                insertLineItem.Parameters.AddWithValue("$leadTimeDays", lineItem.LeadTimeDays);
                insertLineItem.Parameters.AddWithValue("$requiresGForce", lineItem.RequiresGForce ? 1 : 0);
                insertLineItem.Parameters.AddWithValue("$requiresSecondary", lineItem.RequiresSecondaryProcessing ? 1 : 0);
                insertLineItem.Parameters.AddWithValue("$requiresPlating", lineItem.RequiresPlating ? 1 : 0);
                lineItem.Id = Convert.ToInt32(await insertLineItem.ExecuteScalarAsync());

                foreach (var filePath in lineItem.AssociatedFiles.Where(file => !string.IsNullOrWhiteSpace(file)))
                {
                    await using var insertFile = connection.CreateCommand();
                    insertFile.Transaction = transaction;
                    insertFile.CommandText = @"
                        INSERT INTO LineItemFiles (LineItemId, FilePath)
                        VALUES ($lineItemId, $filePath);";
                    insertFile.Parameters.AddWithValue("$lineItemId", lineItem.Id);
                    insertFile.Parameters.AddWithValue("$filePath", filePath.Trim());
                    await insertFile.ExecuteNonQueryAsync();
                }
            }

            await AppendAuditEventAsync(
                connection,
                transaction,
                quote.Id,
                eventType: "save",
                operationMode,
                lineItemCount,
                details: $"status={(int)quote.Status}");

            await transaction.CommitAsync();

            Trace.WriteLine($"[QuoteRepository.SaveQuoteAsync] success quoteId={quote.Id}, requestedQuoteId={requestedId}, mode={operationMode}, lineItemCount={lineItemCount}");
            return quote.Id;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[QuoteRepository.SaveQuoteAsync] failure quoteId={requestedId}, mode={operationMode}, lineItemCount={lineItemCount}, error={ex}");
            throw;
        }
    }

    public async Task<Quote?> GetQuoteAsync(int quoteId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var quote = await ReadQuoteHeaderAsync(connection, quoteId);
        if (quote is null)
        {
            return null;
        }

        quote.LineItems = await ReadLineItemsAsync(connection, quoteId);
        return quote;
    }

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync()
    {
        var results = new List<Quote>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Quotes ORDER BY LastUpdatedUtc DESC";

        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<int>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32(0));
        }

        foreach (var id in ids)
        {
            var quote = await GetQuoteAsync(id);
            if (quote is not null)
            {
                results.Add(quote);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<Quote>> GetQuotesByStatusAsync(QuoteStatus status)
    {
        var all = await GetQuotesAsync();
        return all.Where(q => q.Status == status).ToList();
    }

    public async Task<int> ExpireStaleQuotesAsync(TimeSpan maxAge)
    {
        var candidates = await GetQuotesByStatusAsync(QuoteStatus.InProgress);
        var cutoffUtc = DateTime.UtcNow.Subtract(maxAge);
        var expired = 0;

        foreach (var quote in candidates.Where(q => q.LastUpdatedUtc <= cutoffUtc))
        {
            quote.Status = QuoteStatus.Expired;
            await SaveQuoteAsync(quote);
            expired++;

            Trace.WriteLine($"[QuoteRepository.ExpireStaleQuotesAsync] expired quoteId={quote.Id}, mode=update, lineItemCount={quote.LineItems.Count}");
        }

        return expired;
    }

    public async Task<bool> UpdateStatusAsync(int quoteId, QuoteStatus nextStatus)
    {
        var quote = await GetQuoteAsync(quoteId);
        if (quote is null)
        {
            Trace.WriteLine($"[QuoteRepository.UpdateStatusAsync] quote not found quoteId={quoteId}, mode=update, lineItemCount=0, requestedStatus={nextStatus}");
            return false;
        }

        if (!QuoteWorkflowService.IsTransitionAllowed(quote.Status, nextStatus))
        {
            Trace.WriteLine($"[QuoteRepository.UpdateStatusAsync] invalid transition quoteId={quoteId}, mode=update, lineItemCount={quote.LineItems.Count}, from={quote.Status}, to={nextStatus}");
            return false;
        }

        var priorStatus = quote.Status;
        quote.Status = nextStatus;
        await SaveQuoteAsync(quote);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await AppendAuditEventAsync(
            connection,
            transaction,
            quoteId,
            eventType: "status_transition",
            operationMode: "update",
            lineItemCount: quote.LineItems.Count,
            details: $"from={(int)priorStatus};to={(int)nextStatus}");

        if (nextStatus is QuoteStatus.Won or QuoteStatus.Lost or QuoteStatus.Expired)
        {
            await AppendAuditEventAsync(
                connection,
                transaction,
                quoteId,
                eventType: "archive",
                operationMode: "update",
                lineItemCount: quote.LineItems.Count,
                details: $"terminalStatus={(int)nextStatus}");
        }

        await transaction.CommitAsync();

        Trace.WriteLine($"[QuoteRepository.UpdateStatusAsync] success quoteId={quoteId}, mode=update, lineItemCount={quote.LineItems.Count}, from={priorStatus}, to={nextStatus}");
        return true;
    }

    private static async Task DeleteLineItemsForQuoteAsync(SqliteConnection connection, SqliteTransaction transaction, int quoteId)
    {
        await using var deleteFiles = connection.CreateCommand();
        deleteFiles.Transaction = transaction;
        deleteFiles.CommandText = @"
            DELETE FROM LineItemFiles
            WHERE LineItemId IN (
                SELECT Id FROM QuoteLineItems WHERE QuoteId = $quoteId
            );";
        deleteFiles.Parameters.AddWithValue("$quoteId", quoteId);
        await deleteFiles.ExecuteNonQueryAsync();

        await using var deleteLineItems = connection.CreateCommand();
        deleteLineItems.Transaction = transaction;
        deleteLineItems.CommandText = "DELETE FROM QuoteLineItems WHERE QuoteId = $quoteId;";
        deleteLineItems.Parameters.AddWithValue("$quoteId", quoteId);
        await deleteLineItems.ExecuteNonQueryAsync();
    }

    private static async Task AppendAuditEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int quoteId,
        string eventType,
        string operationMode,
        int lineItemCount,
        string details)
    {
        await using var insertAudit = connection.CreateCommand();
        insertAudit.Transaction = transaction;
        insertAudit.CommandText = @"
            INSERT INTO QuoteAuditEvents (QuoteId, EventType, OperationMode, LineItemCount, Details, CreatedUtc)
            VALUES ($quoteId, $eventType, $operationMode, $lineItemCount, $details, $createdUtc);";
        insertAudit.Parameters.AddWithValue("$quoteId", quoteId);
        insertAudit.Parameters.AddWithValue("$eventType", eventType);
        insertAudit.Parameters.AddWithValue("$operationMode", operationMode);
        insertAudit.Parameters.AddWithValue("$lineItemCount", lineItemCount);
        insertAudit.Parameters.AddWithValue("$details", details);
        insertAudit.Parameters.AddWithValue("$createdUtc", DateTime.UtcNow.ToString("O"));
        await insertAudit.ExecuteNonQueryAsync();
    }

    private static async Task<Quote?> ReadQuoteHeaderAsync(SqliteConnection connection, int quoteId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CustomerName, Status, CreatedUtc, LastUpdatedUtc
            FROM Quotes
            WHERE Id = $id";
        command.Parameters.AddWithValue("$id", quoteId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new Quote
        {
            Id = reader.GetInt32(0),
            CustomerName = reader.GetString(1),
            Status = (QuoteStatus)reader.GetInt32(2),
            CreatedUtc = DateTime.Parse(reader.GetString(3)),
            LastUpdatedUtc = DateTime.Parse(reader.GetString(4))
        };
    }

    private static async Task<List<QuoteLineItem>> ReadLineItemsAsync(SqliteConnection connection, int quoteId)
    {
        var lineItems = new List<QuoteLineItem>();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                li.Id,
                li.Description,
                li.Quantity,
                li.UnitPrice,
                li.LeadTimeDays,
                li.RequiresGForce,
                li.RequiresSecondaryProcessing,
                li.RequiresPlating,
                f.FilePath
            FROM QuoteLineItems li
            LEFT JOIN LineItemFiles f ON f.LineItemId = li.Id
            WHERE li.QuoteId = $quoteId
            ORDER BY li.Id;";
        command.Parameters.AddWithValue("$quoteId", quoteId);

        await using var reader = await command.ExecuteReaderAsync();
        var cache = new Dictionary<int, QuoteLineItem>();
        while (await reader.ReadAsync())
        {
            var lineItemId = reader.GetInt32(0);
            if (!cache.TryGetValue(lineItemId, out var lineItem))
            {
                lineItem = new QuoteLineItem
                {
                    Id = lineItemId,
                    QuoteId = quoteId,
                    Description = reader.GetString(1),
                    Quantity = Convert.ToDecimal(reader.GetDouble(2)),
                    UnitPrice = Convert.ToDecimal(reader.GetDouble(3)),
                    LeadTimeDays = reader.GetInt32(4),
                    RequiresGForce = reader.GetInt32(5) == 1,
                    RequiresSecondaryProcessing = reader.GetInt32(6) == 1,
                    RequiresPlating = reader.GetInt32(7) == 1
                };
                cache[lineItemId] = lineItem;
                lineItems.Add(lineItem);
            }

            if (!reader.IsDBNull(8))
            {
                lineItem.AssociatedFiles.Add(reader.GetString(8));
            }
        }

        return lineItems;
    }

    private static async Task EnsureColumnExistsAsync(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await pragma.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        await alter.ExecuteNonQueryAsync();
    }
}
