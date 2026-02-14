using System.Diagnostics;
using System.Globalization;
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
            CREATE TABLE IF NOT EXISTS Customers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                LastInteractionUtc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS CustomerContacts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                Email TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                FOREIGN KEY(CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Quotes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER,
                CustomerName TEXT NOT NULL,
                LifecycleQuoteId TEXT NOT NULL DEFAULT '',
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
                Notes TEXT NOT NULL DEFAULT '',
                FOREIGN KEY(QuoteId) REFERENCES Quotes(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS LineItemFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LineItemId INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                FOREIGN KEY(LineItemId) REFERENCES QuoteLineItems(Id) ON DELETE CASCADE
            );



            CREATE TABLE IF NOT EXISTS QuoteBlobFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                QuoteId INTEGER NOT NULL DEFAULT 0,
                LineItemId INTEGER NOT NULL,
                LifecycleId TEXT NOT NULL DEFAULT '',
                BlobType INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                Extension TEXT NOT NULL DEFAULT '',
                ContentType TEXT NOT NULL DEFAULT '',
                FileSizeBytes INTEGER NOT NULL DEFAULT 0,
                Sha256 BLOB NOT NULL DEFAULT X'',
                UploadedBy TEXT NOT NULL DEFAULT '',
                BlobData BLOB NOT NULL,
                UploadedUtc TEXT NOT NULL,
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

        await EnsureColumnExistsAsync(connection, "Customers", "LastInteractionUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "CustomerId", "INTEGER");
        await EnsureColumnExistsAsync(connection, "Quotes", "LifecycleQuoteId", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "Quotes", "WonUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "WonByUserId", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "LostUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "LostByUserId", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "ExpiredUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "ExpiredByUserId", "TEXT NULL");

        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "UnitPrice", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "LeadTimeDays", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresGForce", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresSecondaryProcessing", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresPlating", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "Notes", "TEXT NOT NULL DEFAULT ''");

        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "QuoteId", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "LifecycleId", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "Extension", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "FileSizeBytes", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "Sha256", "BLOB NOT NULL DEFAULT X''");
        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "UploadedBy", "TEXT NOT NULL DEFAULT ''");

        await EnsureCustomerIndexesAsync(connection);
        await EnsureQuoteIndexesAsync(connection);
        await BackfillCustomersAsync(connection);
        await EnsureDefaultCustomerAsync(connection);
    }

    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(bool activeOnly = true)
    {
        var customers = new List<Customer>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Code, Name, IsActive, LastInteractionUtc
            FROM Customers
            WHERE $activeOnly = 0 OR IsActive = 1
            ORDER BY Name;";
        command.Parameters.AddWithValue("$activeOnly", activeOnly ? 1 : 0);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            customers.Add(new Customer
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                IsActive = reader.GetInt32(3) == 1,
                LastInteractionUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Contacts = new List<CustomerContact>()
            });
        }

        foreach (var customer in customers)
        {
            customer.Contacts = await GetCustomerContactsAsync(connection, customer.Id);
        }

        return customers;
    }

    private static async Task<List<CustomerContact>> GetCustomerContactsAsync(SqliteConnection connection, int customerId)
    {
        var contacts = new List<CustomerContact>();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, CustomerId, Name, Email, Phone, Notes
            FROM CustomerContacts
            WHERE CustomerId = $customerId
            ORDER BY Name;";
        command.Parameters.AddWithValue("$customerId", customerId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            contacts.Add(new CustomerContact
            {
                Id = reader.GetInt32(0),
                CustomerId = reader.GetInt32(1),
                Name = reader.GetString(2),
                Email = reader.GetString(3),
                Phone = reader.GetString(4),
                Notes = reader.GetString(5)
            });
        }

        return contacts;
    }

    public async Task<int> SaveQuoteAsync(Quote quote)
    {
        var operationMode = quote.Id == 0 ? "create" : "update";
        var requestedId = quote.Id;
        var lineItemCount = quote.LineItems.Count;

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

            if (quote.Id == 0)
            {
                quote.CreatedUtc = quote.LastUpdatedUtc;
                await using var insertQuote = connection.CreateCommand();
                insertQuote.Transaction = transaction;
                insertQuote.CommandText = @"
                    INSERT INTO Quotes (
                        CustomerId,
                        CustomerName,
                        LifecycleQuoteId,
                        Status,
                        CreatedUtc,
                        LastUpdatedUtc,
                        WonUtc,
                        WonByUserId,
                        LostUtc,
                        LostByUserId,
                        ExpiredUtc,
                        ExpiredByUserId)
                    VALUES (
                        $customerId,
                        $name,
                        $lifecycleQuoteId,
                        $status,
                        $created,
                        $updated,
                        $wonUtc,
                        $wonByUserId,
                        $lostUtc,
                        $lostByUserId,
                        $expiredUtc,
                        $expiredByUserId);
                    SELECT last_insert_rowid();";
                insertQuote.Parameters.AddWithValue("$customerId", quote.CustomerId == 0 ? DBNull.Value : quote.CustomerId);
                insertQuote.Parameters.AddWithValue("$name", quote.CustomerName);
                insertQuote.Parameters.AddWithValue("$lifecycleQuoteId", quote.LifecycleQuoteId);
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
                var quoteExists = await QuoteExistsAsync(connection, transaction, quote.Id);
                if (!quoteExists)
                {
                    throw new InvalidOperationException($"Quote {quote.Id} not found. Load an existing quote or create a new one.");
                }

                await using var updateQuote = connection.CreateCommand();
                updateQuote.Transaction = transaction;
                updateQuote.CommandText = @"
                    UPDATE Quotes
                    SET CustomerId = $customerId,
                        CustomerName = $name,
                        LifecycleQuoteId = $lifecycleQuoteId,
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
                updateQuote.Parameters.AddWithValue("$customerId", quote.CustomerId == 0 ? DBNull.Value : quote.CustomerId);
                updateQuote.Parameters.AddWithValue("$name", quote.CustomerName);
                updateQuote.Parameters.AddWithValue("$lifecycleQuoteId", quote.LifecycleQuoteId);
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
                        RequiresPlating,
                        Notes)
                    VALUES ($quoteId, $description, $qty, $unitPrice, $leadTimeDays, $requiresGForce, $requiresSecondary, $requiresPlating, $notes);
                    SELECT last_insert_rowid();";
                insertLineItem.Parameters.AddWithValue("$quoteId", quote.Id);
                insertLineItem.Parameters.AddWithValue("$description", lineItem.Description);
                insertLineItem.Parameters.AddWithValue("$qty", lineItem.Quantity);
                insertLineItem.Parameters.AddWithValue("$unitPrice", lineItem.UnitPrice);
                insertLineItem.Parameters.AddWithValue("$leadTimeDays", lineItem.LeadTimeDays);
                insertLineItem.Parameters.AddWithValue("$requiresGForce", lineItem.RequiresGForce ? 1 : 0);
                insertLineItem.Parameters.AddWithValue("$requiresSecondary", lineItem.RequiresSecondaryProcessing ? 1 : 0);
                insertLineItem.Parameters.AddWithValue("$requiresPlating", lineItem.RequiresPlating ? 1 : 0);
                insertLineItem.Parameters.AddWithValue("$notes", lineItem.Notes ?? string.Empty);
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

                foreach (var blob in lineItem.BlobAttachments)
                {
                    await using var insertBlob = connection.CreateCommand();
                    insertBlob.Transaction = transaction;
                    insertBlob.CommandText = @"
                        INSERT INTO QuoteBlobFiles (
                            QuoteId,
                            LineItemId,
                            LifecycleId,
                            BlobType,
                            FileName,
                            Extension,
                            ContentType,
                            FileSizeBytes,
                            Sha256,
                            UploadedBy,
                            BlobData,
                            UploadedUtc)
                        VALUES (
                            $quoteId,
                            $lineItemId,
                            $lifecycleId,
                            $blobType,
                            $fileName,
                            $extension,
                            $contentType,
                            $fileSizeBytes,
                            $sha256,
                            $uploadedBy,
                            $blobData,
                            $uploadedUtc);";
                    insertBlob.Parameters.AddWithValue("$quoteId", quote.Id);
                    insertBlob.Parameters.AddWithValue("$lineItemId", lineItem.Id);
                    insertBlob.Parameters.AddWithValue("$lifecycleId", quote.LifecycleQuoteId);
                    insertBlob.Parameters.AddWithValue("$blobType", (int)blob.BlobType);
                    insertBlob.Parameters.AddWithValue("$fileName", blob.FileName);
                    insertBlob.Parameters.AddWithValue("$extension", blob.Extension);
                    insertBlob.Parameters.AddWithValue("$contentType", blob.ContentType);
                    insertBlob.Parameters.AddWithValue("$fileSizeBytes", blob.FileSizeBytes);
                    insertBlob.Parameters.AddWithValue("$sha256", blob.Sha256);
                    insertBlob.Parameters.AddWithValue("$uploadedBy", blob.UploadedBy);
                    insertBlob.Parameters.AddWithValue("$blobData", blob.BlobData);
                    insertBlob.Parameters.AddWithValue("$uploadedUtc", blob.UploadedUtc.ToString("O"));
                    await insertBlob.ExecuteNonQueryAsync();
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

    public async Task<IReadOnlyList<Quote>> GetActiveQuotesAsync()
    {
        var terminalStatuses = new[] { QuoteStatus.Won, QuoteStatus.Lost, QuoteStatus.Expired };
        var all = await GetQuotesAsync();
        return all.Where(q => !terminalStatuses.Contains(q.Status)).ToList();
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
        var result = await UpdateStatusAsync(quoteId, nextStatus, "system");
        return result.Success;
    }

    public async Task<(bool Success, string Message)> UpdateStatusAsync(int quoteId, QuoteStatus nextStatus, string actorUserId)
    {
        var quote = await GetQuoteAsync(quoteId);
        if (quote is null)
        {
            var notFoundMessage = $"Quote {quoteId} does not exist.";
            Trace.WriteLine($"[QuoteRepository.UpdateStatusAsync] quote not found quoteId={quoteId}, mode=update, lineItemCount=0, requestedStatus={nextStatus}");
            return (false, notFoundMessage);
        }

        if (!QuoteWorkflowService.IsTransitionAllowed(quote.Status, nextStatus))
        {
            var invalidMessage = $"Cannot move quote {quoteId} from {quote.Status} to {nextStatus}.";
            Trace.WriteLine($"[QuoteRepository.UpdateStatusAsync] invalid transition quoteId={quoteId}, mode=update, lineItemCount={quote.LineItems.Count}, from={quote.Status}, to={nextStatus}");
            return (false, invalidMessage);
        }

        var priorStatus = quote.Status;
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

    private static async Task<bool> QuoteExistsAsync(SqliteConnection connection, SqliteTransaction transaction, int quoteId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM Quotes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", quoteId);

        var result = await command.ExecuteScalarAsync();
        return result is not null;
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
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO QuoteAuditEvents (
                QuoteId,
                EventType,
                OperationMode,
                LineItemCount,
                Details,
                CreatedUtc)
            VALUES (
                $quoteId,
                $eventType,
                $operationMode,
                $lineItemCount,
                $details,
                $createdUtc);";

        command.Parameters.AddWithValue("$quoteId", quoteId);
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$operationMode", operationMode);
        command.Parameters.AddWithValue("$lineItemCount", lineItemCount);
        command.Parameters.AddWithValue("$details", details);
        command.Parameters.AddWithValue("$createdUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Quote?> ReadQuoteHeaderAsync(SqliteConnection connection, int quoteId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT q.Id,
                   q.CustomerId,
                   q.CustomerName,
                   q.LifecycleQuoteId,
                   q.Status,
                   q.CreatedUtc,
                   q.LastUpdatedUtc,
                   q.WonUtc,
                   q.WonByUserId,
                   q.LostUtc,
                   q.LostByUserId,
                   q.ExpiredUtc,
                   q.ExpiredByUserId,
                   c.Name
            FROM Quotes q
            LEFT JOIN Customers c ON c.Id = q.CustomerId
            WHERE q.Id = $id";
        command.Parameters.AddWithValue("$id", quoteId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var customerName = reader.IsDBNull(13) ? reader.GetString(2) : reader.GetString(13);
        return new Quote
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            CustomerName = customerName,
            LifecycleQuoteId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            Status = (QuoteStatus)reader.GetInt32(4),
            CreatedUtc = DateTime.Parse(reader.GetString(5)),
            LastUpdatedUtc = DateTime.Parse(reader.GetString(6)),
            WonUtc = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
            WonByUserId = reader.IsDBNull(8) ? null : reader.GetString(8),
            LostUtc = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
            LostByUserId = reader.IsDBNull(10) ? null : reader.GetString(10),
            ExpiredUtc = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)),
            ExpiredByUserId = reader.IsDBNull(12) ? null : reader.GetString(12)
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
                li.Notes,
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
                    RequiresPlating = reader.GetInt32(7) == 1,
                    Notes = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
                };
                cache[lineItemId] = lineItem;
                lineItems.Add(lineItem);
            }

            if (!reader.IsDBNull(9))
            {
                lineItem.AssociatedFiles.Add(reader.GetString(9));
            }
        }

        foreach (var lineItem in lineItems)
        {
            lineItem.BlobAttachments = await ReadLineItemBlobFilesAsync(connection, lineItem.Id);
        }

        return lineItems;
    }

    public async Task<int> SaveCustomerAsync(Customer customer)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Customers (Code, Name, IsActive, LastInteractionUtc)
            VALUES ($code, $name, $isActive, $lastInteractionUtc)
            ON CONFLICT(Code) DO UPDATE SET
                Name = excluded.Name,
                IsActive = excluded.IsActive,
                LastInteractionUtc = excluded.LastInteractionUtc;

            SELECT Id FROM Customers WHERE Code = $code;";
        command.Parameters.AddWithValue("$code", customer.Code);
        command.Parameters.AddWithValue("$name", customer.Name);
        command.Parameters.AddWithValue("$isActive", customer.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$lastInteractionUtc", customer.LastInteractionUtc?.ToString("O") ?? (object)DBNull.Value);
        var customerId = Convert.ToInt32(await command.ExecuteScalarAsync());

        foreach (var contact in customer.Contacts)
        {
            await using var contactCommand = connection.CreateCommand();
            contactCommand.CommandText = @"
                INSERT INTO CustomerContacts (CustomerId, Name, Email, Phone, Notes)
                VALUES ($customerId, $name, $email, $phone, $notes);";
            contactCommand.Parameters.AddWithValue("$customerId", customerId);
            contactCommand.Parameters.AddWithValue("$name", contact.Name);
            contactCommand.Parameters.AddWithValue("$email", contact.Email);
            contactCommand.Parameters.AddWithValue("$phone", contact.Phone);
            contactCommand.Parameters.AddWithValue("$notes", contact.Notes);
            await contactCommand.ExecuteNonQueryAsync();
        }

        return customerId;
    }

    public async Task ResetLastInteractionOnQuoteAsync(int customerId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Customers
            SET LastInteractionUtc = $lastInteractionUtc
            WHERE Id = $customerId;";
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$lastInteractionUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<QuoteBlobAttachment>> ReadLineItemBlobFilesAsync(SqliteConnection connection, int lineItemId)
    {
        var blobs = new List<QuoteBlobAttachment>();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, QuoteId, LineItemId, LifecycleId, BlobType, FileName, Extension, ContentType, FileSizeBytes, Sha256, UploadedBy, BlobData, UploadedUtc
            FROM QuoteBlobFiles
            WHERE LineItemId = $lineItemId
            ORDER BY Id;";
        command.Parameters.AddWithValue("$lineItemId", lineItemId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            blobs.Add(new QuoteBlobAttachment
            {
                Id = reader.GetInt32(0),
                QuoteId = reader.GetInt32(1),
                LineItemId = reader.GetInt32(2),
                LifecycleId = reader.GetString(3),
                BlobType = (QuoteBlobType)reader.GetInt32(4),
                FileName = reader.GetString(5),
                Extension = reader.GetString(6),
                ContentType = reader.GetString(7),
                FileSizeBytes = reader.GetInt64(8),
                Sha256 = reader.IsDBNull(9) ? Array.Empty<byte>() : (byte[])reader[9],
                UploadedBy = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                BlobData = (byte[])reader[11],
                UploadedUtc = DateTime.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }

        return blobs;
    }

    public async Task<QuoteBlobAttachment> InsertQuoteLineItemFileAsync(
        int quoteId,
        int lineItemId,
        string lifecycleId,
        QuoteBlobType blobType,
        string fileName,
        string extension,
        long fileSizeBytes,
        byte[] sha256,
        string uploadedBy,
        DateTime uploadedUtc,
        byte[] blobData)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO QuoteBlobFiles (
                QuoteId,
                LineItemId,
                LifecycleId,
                BlobType,
                FileName,
                Extension,
                ContentType,
                FileSizeBytes,
                Sha256,
                UploadedBy,
                BlobData,
                UploadedUtc)
            VALUES (
                $quoteId,
                $lineItemId,
                $lifecycleId,
                $blobType,
                $fileName,
                $extension,
                $contentType,
                $fileSizeBytes,
                $sha256,
                $uploadedBy,
                $blobData,
                $uploadedUtc);
            SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$quoteId", quoteId);
        command.Parameters.AddWithValue("$lineItemId", lineItemId);
        command.Parameters.AddWithValue("$lifecycleId", lifecycleId);
        command.Parameters.AddWithValue("$blobType", (int)blobType);
        command.Parameters.AddWithValue("$fileName", fileName);
        command.Parameters.AddWithValue("$extension", extension);
        command.Parameters.AddWithValue("$contentType", extension);
        command.Parameters.AddWithValue("$fileSizeBytes", fileSizeBytes);
        command.Parameters.AddWithValue("$sha256", sha256);
        command.Parameters.AddWithValue("$uploadedBy", uploadedBy);
        command.Parameters.AddWithValue("$blobData", blobData);
        command.Parameters.AddWithValue("$uploadedUtc", uploadedUtc.ToString("O"));

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());

        return new QuoteBlobAttachment
        {
            Id = id,
            QuoteId = quoteId,
            LineItemId = lineItemId,
            LifecycleId = lifecycleId,
            BlobType = blobType,
            FileName = fileName,
            Extension = extension,
            ContentType = extension,
            FileSizeBytes = fileSizeBytes,
            Sha256 = sha256,
            UploadedBy = uploadedBy,
            BlobData = blobData,
            UploadedUtc = uploadedUtc
        };
    }

    public async Task DeleteQuoteAsync(int quoteId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Quotes WHERE Id = $id AND Status = $inProgressStatus;";
        command.Parameters.AddWithValue("$id", quoteId);
        command.Parameters.AddWithValue("$inProgressStatus", (int)QuoteStatus.InProgress);

        var affected = await command.ExecuteNonQueryAsync();
        if (affected == 0)
        {
            throw new InvalidOperationException("Only in-process quotes can be deleted.");
        }
    }

    public async Task DeleteQuoteLineItemFileAsync(int fileId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM QuoteBlobFiles WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", fileId);
        await command.ExecuteNonQueryAsync();
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

    private static async Task EnsureCustomerIndexesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Customers_Code ON Customers(Code);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Customers_Name ON Customers(Name);";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureQuoteIndexesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE INDEX IF NOT EXISTS IX_Quotes_CustomerId ON Quotes(CustomerId);
            CREATE INDEX IF NOT EXISTS IX_QuoteLineItems_QuoteId ON QuoteLineItems(QuoteId);
            CREATE INDEX IF NOT EXISTS IX_QuoteBlobFiles_QuoteId_LineItemId ON QuoteBlobFiles(QuoteId, LineItemId);";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureDefaultCustomerAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Customers (Code, Name, IsActive)
            SELECT 'CUST-00001', 'Default Customer', 1
            WHERE NOT EXISTS (SELECT 1 FROM Customers);";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task BackfillCustomersAsync(SqliteConnection connection)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using var insertCustomers = connection.CreateCommand();
        insertCustomers.Transaction = transaction;
        insertCustomers.CommandText = @"
            WITH MissingCustomers AS (
                SELECT DISTINCT TRIM(CustomerName) AS CustomerName
                FROM Quotes
                WHERE CustomerName IS NOT NULL
                  AND TRIM(CustomerName) <> ''
                  AND NOT EXISTS (
                      SELECT 1
                      FROM Customers c
                      WHERE c.Name = TRIM(Quotes.CustomerName)
                  )
            ),
            LegacyCodeBase AS (
                SELECT COALESCE(MAX(CAST(SUBSTR(Code, 5) AS INTEGER)), 0) AS CurrentMax
                FROM Customers
                WHERE Code LIKE 'LEG-%'
            )
            INSERT INTO Customers (Code, Name, IsActive)
            SELECT
                'LEG-' || printf('%05d', LegacyCodeBase.CurrentMax + ROW_NUMBER() OVER (ORDER BY MissingCustomers.CustomerName)),
                MissingCustomers.CustomerName,
                1
            FROM MissingCustomers
            CROSS JOIN LegacyCodeBase;";
        await insertCustomers.ExecuteNonQueryAsync();

        await using var updateQuotes = connection.CreateCommand();
        updateQuotes.Transaction = transaction;
        updateQuotes.CommandText = @"
            UPDATE Quotes
            SET CustomerId = (
                SELECT c.Id
                FROM Customers c
                WHERE c.Name = TRIM(Quotes.CustomerName)
            )
            WHERE (CustomerId IS NULL OR CustomerId = 0)
              AND CustomerName IS NOT NULL
              AND TRIM(CustomerName) <> '';";
        await updateQuotes.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
    }
}
