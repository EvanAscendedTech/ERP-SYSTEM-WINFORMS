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
                LastUpdatedUtc TEXT NOT NULL,
                WonUtc TEXT NULL,
                WonByUserId TEXT NULL,
                LostUtc TEXT NULL,
                LostByUserId TEXT NULL,
                ExpiredUtc TEXT NULL,
                ExpiredByUserId TEXT NULL
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
            );";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();

        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "UnitPrice", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "LeadTimeDays", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresGForce", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresSecondaryProcessing", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresPlating", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "Quotes", "WonUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "WonByUserId", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "LostUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "LostByUserId", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "ExpiredUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "ExpiredByUserId", "TEXT NULL");
    }

    public async Task<int> SaveQuoteAsync(Quote quote)
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
                INSERT INTO Quotes (CustomerName, Status, CreatedUtc, LastUpdatedUtc, WonUtc, WonByUserId, LostUtc, LostByUserId, ExpiredUtc, ExpiredByUserId)
                VALUES ($name, $status, $created, $updated, $wonUtc, $wonByUserId, $lostUtc, $lostByUserId, $expiredUtc, $expiredByUserId);
                SELECT last_insert_rowid();";
            insertQuote.Parameters.AddWithValue("$name", quote.CustomerName);
            insertQuote.Parameters.AddWithValue("$status", (int)quote.Status);
            insertQuote.Parameters.AddWithValue("$created", quote.CreatedUtc.ToString("O"));
            insertQuote.Parameters.AddWithValue("$updated", quote.LastUpdatedUtc.ToString("O"));
            AddNullableString(insertQuote, "$wonUtc", quote.WonUtc?.ToString("O"));
            AddNullableString(insertQuote, "$wonByUserId", quote.WonByUserId);
            AddNullableString(insertQuote, "$lostUtc", quote.LostUtc?.ToString("O"));
            AddNullableString(insertQuote, "$lostByUserId", quote.LostByUserId);
            AddNullableString(insertQuote, "$expiredUtc", quote.ExpiredUtc?.ToString("O"));
            AddNullableString(insertQuote, "$expiredByUserId", quote.ExpiredByUserId);

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
                    LastUpdatedUtc = $updated,
                    WonUtc = $wonUtc,
                    WonByUserId = $wonByUserId,
                    LostUtc = $lostUtc,
                    LostByUserId = $lostByUserId,
                    ExpiredUtc = $expiredUtc,
                    ExpiredByUserId = $expiredByUserId
                WHERE Id = $id;";
            updateQuote.Parameters.AddWithValue("$id", quote.Id);
            updateQuote.Parameters.AddWithValue("$name", quote.CustomerName);
            updateQuote.Parameters.AddWithValue("$status", (int)quote.Status);
            updateQuote.Parameters.AddWithValue("$updated", quote.LastUpdatedUtc.ToString("O"));
            AddNullableString(updateQuote, "$wonUtc", quote.WonUtc?.ToString("O"));
            AddNullableString(updateQuote, "$wonByUserId", quote.WonByUserId);
            AddNullableString(updateQuote, "$lostUtc", quote.LostUtc?.ToString("O"));
            AddNullableString(updateQuote, "$lostByUserId", quote.LostByUserId);
            AddNullableString(updateQuote, "$expiredUtc", quote.ExpiredUtc?.ToString("O"));
            AddNullableString(updateQuote, "$expiredByUserId", quote.ExpiredByUserId);
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

        await transaction.CommitAsync();
        return quote.Id;
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
        }

        return expired;
    }

    public async Task<bool> UpdateStatusAsync(int quoteId, QuoteStatus nextStatus)
    {
        var result = await UpdateStatusAsync(quoteId, nextStatus, "system");
        return result.Success;
    }

    public async Task<(bool Success, string Message)> UpdateStatusAsync(int quoteId, QuoteStatus nextStatus, string actorUserId)
    {
        var quote = await GetQuoteAsync(quoteId);
        if (quote is null)
        {
            return (false, $"Quote {quoteId} was not found.");
        }

        if (!QuoteWorkflowService.IsTransitionAllowed(quote.Status, nextStatus))
        {
            return (false, QuoteWorkflowService.BuildTransitionErrorMessage(quote.Status, nextStatus));
        }

        var now = DateTime.UtcNow;
        quote.Status = nextStatus;
        switch (nextStatus)
        {
            case QuoteStatus.Won:
                quote.WonUtc = now;
                quote.WonByUserId = actorUserId;
                break;
            case QuoteStatus.Lost:
                quote.LostUtc = now;
                quote.LostByUserId = actorUserId;
                break;
            case QuoteStatus.Expired:
                quote.ExpiredUtc = now;
                quote.ExpiredByUserId = actorUserId;
                break;
        }

        await SaveQuoteAsync(quote);
        return (true, $"Quote {quoteId} moved to {nextStatus}.");
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

    private static async Task<Quote?> ReadQuoteHeaderAsync(SqliteConnection connection, int quoteId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CustomerName, Status, CreatedUtc, LastUpdatedUtc
                , WonUtc, WonByUserId, LostUtc, LostByUserId, ExpiredUtc, ExpiredByUserId
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
            LastUpdatedUtc = DateTime.Parse(reader.GetString(4)),
            WonUtc = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
            WonByUserId = reader.IsDBNull(6) ? null : reader.GetString(6),
            LostUtc = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
            LostByUserId = reader.IsDBNull(8) ? null : reader.GetString(8),
            ExpiredUtc = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
            ExpiredByUserId = reader.IsDBNull(10) ? null : reader.GetString(10)
        };
    }

    private static void AddNullableString(SqliteCommand command, string parameterName, string? value)
    {
        command.Parameters.AddWithValue(parameterName, value ?? (object)DBNull.Value);
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
