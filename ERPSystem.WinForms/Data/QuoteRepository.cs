using System.Diagnostics;
using System.Globalization;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using Microsoft.Data.Sqlite;

namespace ERPSystem.WinForms.Data;

public class QuoteRepository
{
    private readonly string _connectionString;
    private readonly RealtimeDataService? _realtimeDataService;
    public string DatabasePath { get; }

    public QuoteRepository(string databasePath, RealtimeDataService? realtimeDataService = null)
    {
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
        _realtimeDataService = realtimeDataService;
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
                CustomerId INTEGER,
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
                ShopHourlyRateSnapshot REAL NOT NULL DEFAULT 0,
                MasterTotal REAL NOT NULL DEFAULT 0,
                LifecycleQuoteId TEXT NOT NULL DEFAULT '',
                Status INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                LastUpdatedUtc TEXT NOT NULL,
                WonUtc TEXT NULL,
                WonByUserId TEXT NULL,
                LostUtc TEXT NULL,
                LostByUserId TEXT NULL,
                ExpiredUtc TEXT NULL,
                ExpiredByUserId TEXT NULL,
                CompletedUtc TEXT NULL,
                CompletedByUserId TEXT NULL,
                PassedToPurchasingUtc TEXT NULL,
                PassedToPurchasingByUserId TEXT NULL,
                FOREIGN KEY(CustomerId) REFERENCES Customers(Id) ON UPDATE CASCADE ON DELETE CASCADE,
                FOREIGN KEY(WonByUserId) REFERENCES Users(Username) ON UPDATE CASCADE ON DELETE SET NULL,
                FOREIGN KEY(LostByUserId) REFERENCES Users(Username) ON UPDATE CASCADE ON DELETE SET NULL,
                FOREIGN KEY(ExpiredByUserId) REFERENCES Users(Username) ON UPDATE CASCADE ON DELETE SET NULL,
                FOREIGN KEY(CompletedByUserId) REFERENCES Users(Username) ON UPDATE CASCADE ON DELETE SET NULL,
                FOREIGN KEY(PassedToPurchasingByUserId) REFERENCES Users(Username) ON UPDATE CASCADE ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS QuoteLineItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                QuoteId INTEGER NOT NULL,
                Description TEXT NOT NULL,
                DrawingNumber TEXT NOT NULL DEFAULT '',
                DrawingName TEXT NOT NULL DEFAULT '',
                Revision TEXT NOT NULL DEFAULT '',
                Quantity REAL NOT NULL,
                UnitPrice REAL NOT NULL DEFAULT 0,
                ProductionHours REAL NOT NULL DEFAULT 0,
                SetupHours REAL NOT NULL DEFAULT 0,
                MaterialCost REAL NOT NULL DEFAULT 0,
                ToolingCost REAL NOT NULL DEFAULT 0,
                SecondaryOperationsCost REAL NOT NULL DEFAULT 0,
                LineItemTotal REAL NOT NULL DEFAULT 0,
                LeadTimeDays INTEGER NOT NULL DEFAULT 0,
                RequiresGForce INTEGER NOT NULL DEFAULT 0,
                RequiresSecondaryProcessing INTEGER NOT NULL DEFAULT 0,
                RequiresPlating INTEGER NOT NULL DEFAULT 0,
                RequiresDfars INTEGER NOT NULL DEFAULT 0,
                RequiresMaterialTestReport INTEGER NOT NULL DEFAULT 0,
                RequiresCertificateOfConformance INTEGER NOT NULL DEFAULT 0,
                RequiresSecondaryOperations INTEGER NOT NULL DEFAULT 0,
                Notes TEXT NOT NULL DEFAULT '',
                FOREIGN KEY(QuoteId) REFERENCES Quotes(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS LineItemFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LineItemId INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                FOREIGN KEY(LineItemId) REFERENCES QuoteLineItems(Id) ON UPDATE CASCADE ON DELETE CASCADE
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
                StorageRelativePath TEXT NOT NULL DEFAULT '',
                BlobData BLOB NOT NULL,
                UploadedUtc TEXT NOT NULL,
                FOREIGN KEY(LineItemId) REFERENCES QuoteLineItems(Id) ON UPDATE CASCADE ON DELETE CASCADE,
                FOREIGN KEY(QuoteId) REFERENCES Quotes(Id) ON UPDATE CASCADE ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS QuoteAuditEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                QuoteId INTEGER,
                EventType TEXT NOT NULL,
                OperationMode TEXT NOT NULL,
                LineItemCount INTEGER NOT NULL DEFAULT 0,
                Details TEXT,
                CreatedUtc TEXT NOT NULL,
                FOREIGN KEY(QuoteId) REFERENCES Quotes(Id) ON UPDATE CASCADE ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ArchivedQuotes (
                ArchiveId INTEGER PRIMARY KEY AUTOINCREMENT,
                OriginalQuoteId INTEGER NOT NULL,
                CustomerId INTEGER,
                CustomerName TEXT NOT NULL,
                ShopHourlyRateSnapshot REAL NOT NULL DEFAULT 0,
                MasterTotal REAL NOT NULL DEFAULT 0,
                LifecycleQuoteId TEXT NOT NULL DEFAULT '',
                Status INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                LastUpdatedUtc TEXT NOT NULL,
                WonUtc TEXT NULL,
                WonByUserId TEXT NULL,
                LostUtc TEXT NULL,
                LostByUserId TEXT NULL,
                ExpiredUtc TEXT NULL,
                ExpiredByUserId TEXT NULL,
                CompletedUtc TEXT NULL,
                CompletedByUserId TEXT NULL,
                ArchivedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ArchivedPurchasingQuotes (
                ArchiveId INTEGER PRIMARY KEY AUTOINCREMENT,
                OriginalQuoteId INTEGER NOT NULL,
                LifecycleQuoteId TEXT NOT NULL DEFAULT '',
                CustomerName TEXT NOT NULL,
                Status INTEGER NOT NULL,
                ArchivedByUserId TEXT NOT NULL,
                ArchivedUtc TEXT NOT NULL
            );


            CREATE TABLE IF NOT EXISTS ArchivedQuoteLineItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ArchiveId INTEGER NOT NULL,
                OriginalLineItemId INTEGER NOT NULL,
                Description TEXT NOT NULL,
                DrawingNumber TEXT NOT NULL DEFAULT '',
                DrawingName TEXT NOT NULL DEFAULT '',
                Revision TEXT NOT NULL DEFAULT '',
                Quantity REAL NOT NULL,
                UnitPrice REAL NOT NULL DEFAULT 0,
                ProductionHours REAL NOT NULL DEFAULT 0,
                SetupHours REAL NOT NULL DEFAULT 0,
                MaterialCost REAL NOT NULL DEFAULT 0,
                ToolingCost REAL NOT NULL DEFAULT 0,
                SecondaryOperationsCost REAL NOT NULL DEFAULT 0,
                LineItemTotal REAL NOT NULL DEFAULT 0,
                LeadTimeDays INTEGER NOT NULL DEFAULT 0,
                RequiresGForce INTEGER NOT NULL DEFAULT 0,
                RequiresSecondaryProcessing INTEGER NOT NULL DEFAULT 0,
                RequiresPlating INTEGER NOT NULL DEFAULT 0,
                RequiresDfars INTEGER NOT NULL DEFAULT 0,
                RequiresMaterialTestReport INTEGER NOT NULL DEFAULT 0,
                RequiresCertificateOfConformance INTEGER NOT NULL DEFAULT 0,
                RequiresSecondaryOperations INTEGER NOT NULL DEFAULT 0,
                Notes TEXT NOT NULL DEFAULT '',
                AssociatedFiles TEXT NOT NULL DEFAULT '',
                FOREIGN KEY(ArchiveId) REFERENCES ArchivedQuotes(ArchiveId) ON DELETE CASCADE
            );




            CREATE TABLE IF NOT EXISTS QuoteSettings (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                ShopHourlyRate REAL NOT NULL DEFAULT 75
            );

            CREATE TABLE IF NOT EXISTS IntegrityValidationRuns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Scope TEXT NOT NULL,
                Success INTEGER NOT NULL,
                IssueCount INTEGER NOT NULL,
                Details TEXT NOT NULL,
                ExecutedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RelationshipChangeLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TableName TEXT NOT NULL,
                ChangeType TEXT NOT NULL,
                RelationshipKey TEXT NOT NULL,
                Details TEXT NOT NULL,
                ChangedUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ArchivedQuoteBlobFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ArchiveId INTEGER NOT NULL,
                OriginalBlobId INTEGER NOT NULL,
                OriginalLineItemId INTEGER NOT NULL,
                LifecycleId TEXT NOT NULL DEFAULT '',
                BlobType INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                Extension TEXT NOT NULL DEFAULT '',
                ContentType TEXT NOT NULL DEFAULT '',
                FileSizeBytes INTEGER NOT NULL DEFAULT 0,
                Sha256 BLOB NOT NULL DEFAULT X'',
                UploadedBy TEXT NOT NULL DEFAULT '',
                StorageRelativePath TEXT NOT NULL DEFAULT '',
                BlobData BLOB NOT NULL,
                UploadedUtc TEXT NOT NULL,
                FOREIGN KEY(ArchiveId) REFERENCES ArchivedQuotes(ArchiveId) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS StepGlbCache (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LineItemId INTEGER NOT NULL,
                StepSha256 TEXT NOT NULL,
                SourceFileName TEXT NOT NULL DEFAULT '',
                SourcePath TEXT NOT NULL DEFAULT '',
                GlbData BLOB NOT NULL,
                CreatedUtc TEXT NOT NULL,
                LastAccessedUtc TEXT NOT NULL,
                UNIQUE(LineItemId, StepSha256),
                FOREIGN KEY(LineItemId) REFERENCES QuoteLineItems(Id) ON UPDATE CASCADE ON DELETE CASCADE
            );";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();

        await EnsureColumnExistsAsync(connection, "Customers", "LastInteractionUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "CustomerId", "INTEGER");
        await EnsureColumnExistsAsync(connection, "Quotes", "LifecycleQuoteId", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "Quotes", "ShopHourlyRateSnapshot", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "Quotes", "MasterTotal", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "Quotes", "WonUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "WonByUserId", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "LostUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "LostByUserId", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "ExpiredUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "ExpiredByUserId", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "CompletedUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "CompletedByUserId", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "PassedToPurchasingUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "Quotes", "PassedToPurchasingByUserId", "TEXT NULL");

        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "UnitPrice", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "DrawingNumber", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "DrawingName", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "Revision", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "ProductionHours", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "SetupHours", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "MaterialCost", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "ToolingCost", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "SecondaryOperationsCost", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "LineItemTotal", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "LeadTimeDays", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresGForce", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresSecondaryProcessing", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresPlating", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresDfars", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresMaterialTestReport", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresCertificateOfConformance", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "RequiresSecondaryOperations", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteLineItems", "Notes", "TEXT NOT NULL DEFAULT ''");

        await EnsureColumnExistsAsync(connection, "ArchivedQuoteLineItems", "RequiresDfars", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "ArchivedQuoteLineItems", "RequiresMaterialTestReport", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "ArchivedQuoteLineItems", "RequiresCertificateOfConformance", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "ArchivedQuoteLineItems", "RequiresSecondaryOperations", "INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "QuoteId", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "LifecycleId", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "Extension", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "FileSizeBytes", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "Sha256", "BLOB NOT NULL DEFAULT X''");
        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "UploadedBy", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "QuoteBlobFiles", "StorageRelativePath", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "StepGlbCache", "SourceFileName", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "StepGlbCache", "SourcePath", "TEXT NOT NULL DEFAULT ''");

        await EnsureColumnExistsAsync(connection, "Customers", "Address", "TEXT NOT NULL DEFAULT ''");

        await EnsureDefaultQuoteSettingsAsync(connection);

        await EnsureCustomerIndexesAsync(connection);
        await EnsureQuoteIndexesAsync(connection);
        await EnsureArchiveIndexesAsync(connection);
        await EnsureRelationshipTriggersAsync(connection);
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
            SELECT Id, Code, Name, Address, IsActive, LastInteractionUtc
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
                Address = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                IsActive = reader.GetInt32(4) == 1,
                LastInteractionUtc = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
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


    public async Task<int> SaveQuoteAsync(Quote quote, UserAccount actor)
    {
        EnsureAdminForCompletedQuoteMutation(actor, quote, "save");
        return await SaveQuoteAsync(quote);
    }

    public async Task<int> SaveQuoteAsync(Quote quote)
    {
        ValidateQuoteForPersistence(quote);
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
                        ShopHourlyRateSnapshot,
                        MasterTotal,
                        LifecycleQuoteId,
                        Status,
                        CreatedUtc,
                        LastUpdatedUtc,
                        WonUtc,
                        WonByUserId,
                        LostUtc,
                        LostByUserId,
                        ExpiredUtc,
                        ExpiredByUserId,
                        CompletedUtc,
                        CompletedByUserId,
                        PassedToPurchasingUtc,
                        PassedToPurchasingByUserId)
                    VALUES (
                        $customerId,
                        $name,
                        $shopHourlyRateSnapshot,
                        $masterTotal,
                        $lifecycleQuoteId,
                        $status,
                        $created,
                        $updated,
                        $wonUtc,
                        $wonByUserId,
                        $lostUtc,
                        $lostByUserId,
                        $expiredUtc,
                        $expiredByUserId,
                        $completedUtc,
                        $completedByUserId,
                        $passedToPurchasingUtc,
                        $passedToPurchasingByUserId);
                    SELECT last_insert_rowid();";
                insertQuote.Parameters.AddWithValue("$customerId", quote.CustomerId == 0 ? DBNull.Value : quote.CustomerId);
                insertQuote.Parameters.AddWithValue("$name", quote.CustomerName);
                insertQuote.Parameters.AddWithValue("$lifecycleQuoteId", quote.LifecycleQuoteId);
                insertQuote.Parameters.AddWithValue("$shopHourlyRateSnapshot", quote.ShopHourlyRateSnapshot);
                insertQuote.Parameters.AddWithValue("$masterTotal", quote.MasterTotal);
                insertQuote.Parameters.AddWithValue("$status", (int)quote.Status);
                insertQuote.Parameters.AddWithValue("$created", quote.CreatedUtc.ToString("O"));
                insertQuote.Parameters.AddWithValue("$updated", quote.LastUpdatedUtc.ToString("O"));
                AddNullableString(insertQuote, "$wonUtc", quote.WonUtc?.ToString("O"));
                AddNullableString(insertQuote, "$wonByUserId", quote.WonByUserId);
                AddNullableString(insertQuote, "$lostUtc", quote.LostUtc?.ToString("O"));
                AddNullableString(insertQuote, "$lostByUserId", quote.LostByUserId);
                AddNullableString(insertQuote, "$expiredUtc", quote.ExpiredUtc?.ToString("O"));
                AddNullableString(insertQuote, "$expiredByUserId", quote.ExpiredByUserId);
                AddNullableString(insertQuote, "$completedUtc", quote.CompletedUtc?.ToString("O"));
                AddNullableString(insertQuote, "$completedByUserId", quote.CompletedByUserId);
                AddNullableString(insertQuote, "$passedToPurchasingUtc", quote.PassedToPurchasingUtc?.ToString("O"));
                AddNullableString(insertQuote, "$passedToPurchasingByUserId", quote.PassedToPurchasingByUserId);

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
                        ShopHourlyRateSnapshot = $shopHourlyRateSnapshot,
                        MasterTotal = $masterTotal,
                        LifecycleQuoteId = $lifecycleQuoteId,
                        Status = $status,
                        LastUpdatedUtc = $updated,
                        WonUtc = $wonUtc,
                        WonByUserId = $wonByUserId,
                        LostUtc = $lostUtc,
                        LostByUserId = $lostByUserId,
                        ExpiredUtc = $expiredUtc,
                        ExpiredByUserId = $expiredByUserId,
                        CompletedUtc = $completedUtc,
                        CompletedByUserId = $completedByUserId,
                        PassedToPurchasingUtc = $passedToPurchasingUtc,
                        PassedToPurchasingByUserId = $passedToPurchasingByUserId
                    WHERE Id = $id;";
                updateQuote.Parameters.AddWithValue("$id", quote.Id);
                updateQuote.Parameters.AddWithValue("$customerId", quote.CustomerId == 0 ? DBNull.Value : quote.CustomerId);
                updateQuote.Parameters.AddWithValue("$name", quote.CustomerName);
                updateQuote.Parameters.AddWithValue("$lifecycleQuoteId", quote.LifecycleQuoteId);
                updateQuote.Parameters.AddWithValue("$shopHourlyRateSnapshot", quote.ShopHourlyRateSnapshot);
                updateQuote.Parameters.AddWithValue("$masterTotal", quote.MasterTotal);
                updateQuote.Parameters.AddWithValue("$status", (int)quote.Status);
                updateQuote.Parameters.AddWithValue("$updated", quote.LastUpdatedUtc.ToString("O"));
                AddNullableString(updateQuote, "$wonUtc", quote.WonUtc?.ToString("O"));
                AddNullableString(updateQuote, "$wonByUserId", quote.WonByUserId);
                AddNullableString(updateQuote, "$lostUtc", quote.LostUtc?.ToString("O"));
                AddNullableString(updateQuote, "$lostByUserId", quote.LostByUserId);
                AddNullableString(updateQuote, "$expiredUtc", quote.ExpiredUtc?.ToString("O"));
                AddNullableString(updateQuote, "$expiredByUserId", quote.ExpiredByUserId);
                AddNullableString(updateQuote, "$completedUtc", quote.CompletedUtc?.ToString("O"));
                AddNullableString(updateQuote, "$completedByUserId", quote.CompletedByUserId);
                AddNullableString(updateQuote, "$passedToPurchasingUtc", quote.PassedToPurchasingUtc?.ToString("O"));
                AddNullableString(updateQuote, "$passedToPurchasingByUserId", quote.PassedToPurchasingByUserId);
                await updateQuote.ExecuteNonQueryAsync();

                await DeleteStoredFilesForQuoteAsync(connection, transaction, quote.Id);
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
                        DrawingNumber,
                        DrawingName,
                        Revision,
                        Quantity,
                        UnitPrice,
                        ProductionHours,
                        SetupHours,
                        MaterialCost,
                        ToolingCost,
                        SecondaryOperationsCost,
                        LineItemTotal,
                        LeadTimeDays,
                        RequiresGForce,
                        RequiresSecondaryProcessing,
                        RequiresPlating,
                        RequiresDfars,
                        RequiresMaterialTestReport,
                        RequiresCertificateOfConformance,
                        RequiresSecondaryOperations,
                        Notes)
                    VALUES ($quoteId, $description, $drawingNumber, $drawingName, $revision, $qty, $unitPrice, $productionHours, $setupHours, $materialCost, $toolingCost, $secondaryOperationsCost, $lineItemTotal, $leadTimeDays, $requiresGForce, $requiresSecondary, $requiresPlating, $requiresDfars, $requiresMaterialTestReport, $requiresCertificateOfConformance, $requiresSecondaryOperations, $notes);
                    SELECT last_insert_rowid();";
                insertLineItem.Parameters.AddWithValue("$quoteId", quote.Id);
                insertLineItem.Parameters.AddWithValue("$description", lineItem.Description);
                insertLineItem.Parameters.AddWithValue("$drawingNumber", lineItem.DrawingNumber ?? string.Empty);
                insertLineItem.Parameters.AddWithValue("$drawingName", lineItem.DrawingName ?? string.Empty);
                insertLineItem.Parameters.AddWithValue("$revision", lineItem.Revision ?? string.Empty);
                insertLineItem.Parameters.AddWithValue("$qty", lineItem.Quantity);
                insertLineItem.Parameters.AddWithValue("$unitPrice", lineItem.UnitPrice);
                insertLineItem.Parameters.AddWithValue("$productionHours", lineItem.ProductionHours);
                insertLineItem.Parameters.AddWithValue("$setupHours", lineItem.SetupHours);
                insertLineItem.Parameters.AddWithValue("$materialCost", lineItem.MaterialCost);
                insertLineItem.Parameters.AddWithValue("$toolingCost", lineItem.ToolingCost);
                insertLineItem.Parameters.AddWithValue("$secondaryOperationsCost", lineItem.SecondaryOperationsCost);
                insertLineItem.Parameters.AddWithValue("$lineItemTotal", lineItem.LineItemTotal);
                insertLineItem.Parameters.AddWithValue("$leadTimeDays", lineItem.LeadTimeDays);
                insertLineItem.Parameters.AddWithValue("$requiresGForce", lineItem.RequiresGForce ? 1 : 0);
                insertLineItem.Parameters.AddWithValue("$requiresSecondary", lineItem.RequiresSecondaryProcessing ? 1 : 0);
                insertLineItem.Parameters.AddWithValue("$requiresPlating", lineItem.RequiresPlating ? 1 : 0);
                insertLineItem.Parameters.AddWithValue("$requiresDfars", lineItem.RequiresDfars ? 1 : 0);
                insertLineItem.Parameters.AddWithValue("$requiresMaterialTestReport", lineItem.RequiresMaterialTestReport ? 1 : 0);
                insertLineItem.Parameters.AddWithValue("$requiresCertificateOfConformance", lineItem.RequiresCertificateOfConformance ? 1 : 0);
                insertLineItem.Parameters.AddWithValue("$requiresSecondaryOperations", lineItem.RequiresSecondaryOperations ? 1 : 0);
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
                            StorageRelativePath,
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
                            $storageRelativePath,
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
                    var storageRelativePath = string.Empty;
                    insertBlob.Parameters.AddWithValue("$uploadedBy", blob.UploadedBy);
                    insertBlob.Parameters.AddWithValue("$storageRelativePath", storageRelativePath);
                    insertBlob.Parameters.AddWithValue("$blobData", blob.BlobData);
                    insertBlob.Parameters.AddWithValue("$uploadedUtc", blob.UploadedUtc.ToString("O"));
                    await insertBlob.ExecuteNonQueryAsync();

                    blob.QuoteId = quote.Id;
                    blob.LineItemId = lineItem.Id;
                    blob.LifecycleId = quote.LifecycleQuoteId;
                    blob.StorageRelativePath = storageRelativePath;
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

            if (_realtimeDataService is not null)
            {
                await _realtimeDataService.PublishChangeAsync("Quotes", operationMode);
            }

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
            case QuoteStatus.Completed:
                quote.CompletedUtc = now;
                quote.CompletedByUserId = actorUserId;
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

        if (nextStatus is QuoteStatus.Completed or QuoteStatus.Won or QuoteStatus.Lost or QuoteStatus.Expired)
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

    public async Task<(bool Success, string Message)> PassToPurchasingAsync(int quoteId, string actorUserId)
    {
        var quote = await GetQuoteAsync(quoteId);
        if (quote is null)
        {
            return (false, $"Quote {quoteId} does not exist.");
        }

        if (quote.Status != QuoteStatus.Won)
        {
            return (false, $"Only Won quotes can be passed to Purchasing. Current status: {quote.Status}.");
        }

        if (quote.PassedToPurchasingUtc.HasValue)
        {
            return (true, $"Quote {quoteId} is already in Purchasing.");
        }

        quote.PassedToPurchasingUtc = DateTime.UtcNow;
        quote.PassedToPurchasingByUserId = actorUserId;
        await SaveQuoteAsync(quote);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await AppendAuditEventAsync(
            connection,
            transaction,
            quoteId,
            eventType: "purchasing_handoff",
            operationMode: "update",
            lineItemCount: quote.LineItems.Count,
            details: $"actor={actorUserId};status={(int)quote.Status}");

        await transaction.CommitAsync();

        return (true, $"Quote {quoteId} passed to Purchasing.");
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
                   q.ShopHourlyRateSnapshot,
                   q.MasterTotal,
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
                   q.CompletedUtc,
                   q.CompletedByUserId,
                   q.PassedToPurchasingUtc,
                   q.PassedToPurchasingByUserId,
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

        var customerName = reader.IsDBNull(19) ? reader.GetString(2) : reader.GetString(19);
        return new Quote
        {
            Id = reader.GetInt32(0),
            CustomerId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            CustomerName = customerName,
            ShopHourlyRateSnapshot = Convert.ToDecimal(reader.GetDouble(3)),
            MasterTotal = Convert.ToDecimal(reader.GetDouble(4)),
            LifecycleQuoteId = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            Status = (QuoteStatus)reader.GetInt32(6),
            CreatedUtc = DateTime.Parse(reader.GetString(7)),
            LastUpdatedUtc = DateTime.Parse(reader.GetString(8)),
            WonUtc = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
            WonByUserId = reader.IsDBNull(10) ? null : reader.GetString(10),
            LostUtc = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)),
            LostByUserId = reader.IsDBNull(12) ? null : reader.GetString(12),
            ExpiredUtc = reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13)),
            ExpiredByUserId = reader.IsDBNull(14) ? null : reader.GetString(14),
            CompletedUtc = reader.IsDBNull(15) ? null : DateTime.Parse(reader.GetString(15)),
            CompletedByUserId = reader.IsDBNull(16) ? null : reader.GetString(16),
            PassedToPurchasingUtc = reader.IsDBNull(17) ? null : DateTime.Parse(reader.GetString(17)),
            PassedToPurchasingByUserId = reader.IsDBNull(18) ? null : reader.GetString(18)
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
                li.DrawingNumber,
                li.DrawingName,
                li.Revision,
                li.Quantity,
                li.UnitPrice,
                li.ProductionHours,
                li.SetupHours,
                li.MaterialCost,
                li.ToolingCost,
                li.SecondaryOperationsCost,
                li.LineItemTotal,
                li.LeadTimeDays,
                li.RequiresGForce,
                li.RequiresSecondaryProcessing,
                li.RequiresPlating,
                li.RequiresDfars,
                li.RequiresMaterialTestReport,
                li.RequiresCertificateOfConformance,
                li.RequiresSecondaryOperations,
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
                    DrawingNumber = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    DrawingName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Revision = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Quantity = Convert.ToDecimal(reader.GetDouble(5)),
                    UnitPrice = Convert.ToDecimal(reader.GetDouble(6)),
                    ProductionHours = Convert.ToDecimal(reader.GetDouble(7)),
                    SetupHours = Convert.ToDecimal(reader.GetDouble(8)),
                    MaterialCost = Convert.ToDecimal(reader.GetDouble(9)),
                    ToolingCost = Convert.ToDecimal(reader.GetDouble(10)),
                    SecondaryOperationsCost = Convert.ToDecimal(reader.GetDouble(11)),
                    LineItemTotal = Convert.ToDecimal(reader.GetDouble(12)),
                    LeadTimeDays = reader.GetInt32(13),
                    RequiresGForce = reader.GetInt32(14) == 1,
                    RequiresSecondaryProcessing = reader.GetInt32(15) == 1,
                    RequiresPlating = reader.GetInt32(16) == 1,
                    RequiresDfars = reader.GetInt32(17) == 1,
                    RequiresMaterialTestReport = reader.GetInt32(18) == 1,
                    RequiresCertificateOfConformance = reader.GetInt32(19) == 1,
                    RequiresSecondaryOperations = reader.GetInt32(20) == 1,
                    Notes = reader.IsDBNull(21) ? string.Empty : reader.GetString(21)
                };
                cache[lineItemId] = lineItem;
                lineItems.Add(lineItem);
            }

            if (!reader.IsDBNull(22))
            {
                lineItem.AssociatedFiles.Add(reader.GetString(22));
            }
        }

        foreach (var lineItem in lineItems)
        {
            lineItem.BlobAttachments = await ReadLineItemBlobFilesAsync(connection, quoteId, lineItem.Id);
        }

        return lineItems;
    }

    public async Task<int> SaveCustomerAsync(Customer customer)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Customers (Code, Name, Address, IsActive, LastInteractionUtc)
            VALUES ($code, $name, $address, $isActive, $lastInteractionUtc)
            ON CONFLICT(Code) DO UPDATE SET
                Name = excluded.Name,
                Address = excluded.Address,
                IsActive = excluded.IsActive,
                LastInteractionUtc = excluded.LastInteractionUtc;

            SELECT Id FROM Customers WHERE Code = $code;";
        command.Parameters.AddWithValue("$code", customer.Code);
        command.Parameters.AddWithValue("$name", customer.Name);
        command.Parameters.AddWithValue("$address", customer.Address ?? string.Empty);
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

    private static async Task<List<QuoteBlobAttachment>> ReadLineItemBlobFilesAsync(SqliteConnection connection, int quoteId, int lineItemId)
    {
        var blobs = new List<QuoteBlobAttachment>();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, QuoteId, LineItemId, LifecycleId, BlobType, FileName, Extension, ContentType, FileSizeBytes, Sha256, UploadedBy, StorageRelativePath, BlobData, UploadedUtc
            FROM QuoteBlobFiles
            WHERE LineItemId = $lineItemId
              AND QuoteId = $quoteId
            ORDER BY UploadedUtc DESC, Id DESC;";
        command.Parameters.AddWithValue("$quoteId", quoteId);
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
                StorageRelativePath = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                BlobData = reader.IsDBNull(12) ? Array.Empty<byte>() : (byte[])reader[12],
                UploadedUtc = DateTime.Parse(reader.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
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

        var overwrittenPaths = new List<string>();
        await using (var existingCommand = connection.CreateCommand())
        {
            existingCommand.CommandText = @"
                SELECT StorageRelativePath
                FROM QuoteBlobFiles
                WHERE LineItemId = $lineItemId
                  AND BlobType = $blobType
                  AND FileName = $fileName;";
            existingCommand.Parameters.AddWithValue("$lineItemId", lineItemId);
            existingCommand.Parameters.AddWithValue("$blobType", (int)blobType);
            existingCommand.Parameters.AddWithValue("$fileName", fileName);
            await using var existingReader = await existingCommand.ExecuteReaderAsync();
            while (await existingReader.ReadAsync())
            {
                if (!existingReader.IsDBNull(0))
                {
                    overwrittenPaths.Add(existingReader.GetString(0));
                }
            }
        }

        await using (var overwrite = connection.CreateCommand())
        {
            overwrite.CommandText = @"
                DELETE FROM QuoteBlobFiles
                WHERE LineItemId = $lineItemId
                  AND BlobType = $blobType
                  AND FileName = $fileName;";
            overwrite.Parameters.AddWithValue("$lineItemId", lineItemId);
            overwrite.Parameters.AddWithValue("$blobType", (int)blobType);
            overwrite.Parameters.AddWithValue("$fileName", fileName);
            await overwrite.ExecuteNonQueryAsync();
        }

        foreach (var path in overwrittenPaths)
        {
            DeletePhysicalBlob(path);
        }

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
                StorageRelativePath,
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
                $storageRelativePath,
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
        var storageRelativePath = string.Empty;
        command.Parameters.AddWithValue("$uploadedBy", uploadedBy);
        command.Parameters.AddWithValue("$storageRelativePath", storageRelativePath);
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
            StorageRelativePath = storageRelativePath,
            BlobData = blobData,
            UploadedUtc = uploadedUtc
        };
    }


    public async Task ArchiveAndDeletePurchasingQuoteAsync(int quoteId, string actorUserId)
    {
        var quote = await GetQuoteAsync(quoteId);
        if (quote is null)
        {
            throw new InvalidOperationException($"Quote {quoteId} was not found.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var archive = connection.CreateCommand())
        {
            archive.Transaction = transaction;
            archive.CommandText = @"
                INSERT INTO ArchivedPurchasingQuotes (
                    OriginalQuoteId,
                    LifecycleQuoteId,
                    CustomerName,
                    Status,
                    ArchivedByUserId,
                    ArchivedUtc)
                VALUES (
                    $quoteId,
                    $lifecycleQuoteId,
                    $customerName,
                    $status,
                    $archivedByUserId,
                    $archivedUtc);";
            archive.Parameters.AddWithValue("$quoteId", quote.Id);
            archive.Parameters.AddWithValue("$lifecycleQuoteId", quote.LifecycleQuoteId ?? string.Empty);
            archive.Parameters.AddWithValue("$customerName", quote.CustomerName);
            archive.Parameters.AddWithValue("$status", (int)quote.Status);
            archive.Parameters.AddWithValue("$archivedByUserId", actorUserId);
            archive.Parameters.AddWithValue("$archivedUtc", DateTime.UtcNow.ToString("O"));
            await archive.ExecuteNonQueryAsync();
        }

        await DeleteStoredFilesForQuoteAsync(connection, transaction, quoteId);

        await using (var deleteBlobFiles = connection.CreateCommand())
        {
            deleteBlobFiles.Transaction = transaction;
            deleteBlobFiles.CommandText = @"
                DELETE FROM QuoteBlobFiles
                WHERE QuoteId = $id
                   OR LineItemId IN (SELECT Id FROM QuoteLineItems WHERE QuoteId = $id);";
            deleteBlobFiles.Parameters.AddWithValue("$id", quoteId);
            await deleteBlobFiles.ExecuteNonQueryAsync();
        }

        await using (var deleteLineItemFiles = connection.CreateCommand())
        {
            deleteLineItemFiles.Transaction = transaction;
            deleteLineItemFiles.CommandText = @"
                DELETE FROM LineItemFiles
                WHERE LineItemId IN (SELECT Id FROM QuoteLineItems WHERE QuoteId = $id);";
            deleteLineItemFiles.Parameters.AddWithValue("$id", quoteId);
            await deleteLineItemFiles.ExecuteNonQueryAsync();
        }

        await using (var deleteLineItems = connection.CreateCommand())
        {
            deleteLineItems.Transaction = transaction;
            deleteLineItems.CommandText = "DELETE FROM QuoteLineItems WHERE QuoteId = $id;";
            deleteLineItems.Parameters.AddWithValue("$id", quoteId);
            await deleteLineItems.ExecuteNonQueryAsync();
        }

        await using (var deleteAuditEvents = connection.CreateCommand())
        {
            deleteAuditEvents.Transaction = transaction;
            deleteAuditEvents.CommandText = "DELETE FROM QuoteAuditEvents WHERE QuoteId = $id;";
            deleteAuditEvents.Parameters.AddWithValue("$id", quoteId);
            await deleteAuditEvents.ExecuteNonQueryAsync();
        }

        await using (var deleteQuote = connection.CreateCommand())
        {
            deleteQuote.Transaction = transaction;
            deleteQuote.CommandText = "DELETE FROM Quotes WHERE Id = $id;";
            deleteQuote.Parameters.AddWithValue("$id", quoteId);
            await deleteQuote.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        if (_realtimeDataService is not null)
        {
            await _realtimeDataService.PublishChangeAsync("Quotes", "archive-delete-purchasing");
        }
    }

    public async Task<IReadOnlyList<ArchivedPurchasingQuote>> GetArchivedPurchasingQuotesAsync()
    {
        var archived = new List<ArchivedPurchasingQuote>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT ArchiveId, OriginalQuoteId, LifecycleQuoteId, CustomerName, Status, ArchivedByUserId, ArchivedUtc
            FROM ArchivedPurchasingQuotes
            ORDER BY ArchivedUtc DESC;";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            archived.Add(new ArchivedPurchasingQuote
            {
                ArchiveId = reader.GetInt32(0),
                OriginalQuoteId = reader.GetInt32(1),
                LifecycleQuoteId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                CustomerName = reader.GetString(3),
                Status = (QuoteStatus)reader.GetInt32(4),
                ArchivedByUserId = reader.GetString(5),
                ArchivedUtc = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }

        return archived;
    }

    public async Task<(bool Success, string Message)> RestoreArchivedPurchasingQuoteAsync(int archiveId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        ArchivedPurchasingQuote? archived = null;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = @"
                SELECT ArchiveId, OriginalQuoteId, LifecycleQuoteId, CustomerName, Status, ArchivedByUserId, ArchivedUtc
                FROM ArchivedPurchasingQuotes
                WHERE ArchiveId = $archiveId;";
            select.Parameters.AddWithValue("$archiveId", archiveId);

            await using var reader = await select.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                archived = new ArchivedPurchasingQuote
                {
                    ArchiveId = reader.GetInt32(0),
                    OriginalQuoteId = reader.GetInt32(1),
                    LifecycleQuoteId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    CustomerName = reader.GetString(3),
                    Status = (QuoteStatus)reader.GetInt32(4),
                    ArchivedByUserId = reader.GetString(5),
                    ArchivedUtc = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                };
            }
        }

        if (archived is null)
        {
            await transaction.RollbackAsync();
            return (false, $"Archive entry {archiveId} was not found.");
        }

        await using (var restore = connection.CreateCommand())
        {
            restore.Transaction = transaction;
            restore.CommandText = @"
                INSERT INTO Quotes (
                    Id, CustomerId, CustomerName, ShopHourlyRateSnapshot, MasterTotal, LifecycleQuoteId, Status,
                    CreatedUtc, LastUpdatedUtc, WonUtc, WonByUserId, LostUtc, LostByUserId,
                    ExpiredUtc, ExpiredByUserId, CompletedUtc, CompletedByUserId, PassedToPurchasingUtc, PassedToPurchasingByUserId)
                VALUES (
                    $id, 0, $customerName, 0, 0, $lifecycleQuoteId, $status,
                    $now, $now, NULL, NULL, NULL, NULL,
                    NULL, NULL, NULL, NULL, $now, $actor)
                ON CONFLICT(Id) DO NOTHING;";
            restore.Parameters.AddWithValue("$id", archived.OriginalQuoteId);
            restore.Parameters.AddWithValue("$customerName", archived.CustomerName);
            restore.Parameters.AddWithValue("$lifecycleQuoteId", archived.LifecycleQuoteId);
            restore.Parameters.AddWithValue("$status", (int)QuoteStatus.Won);
            restore.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            restore.Parameters.AddWithValue("$actor", archived.ArchivedByUserId);
            await restore.ExecuteNonQueryAsync();
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM ArchivedPurchasingQuotes WHERE ArchiveId = $archiveId;";
            delete.Parameters.AddWithValue("$archiveId", archiveId);
            await delete.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return (true, $"Restored purchasing quote {archived.OriginalQuoteId}.");
    }


    public async Task DeleteQuoteAsync(int quoteId, UserAccount actor)
    {
        var quote = await GetQuoteAsync(quoteId);
        if (quote is null)
        {
            throw new InvalidOperationException($"Quote {quoteId} was not found.");
        }

        EnsureAdminForCompletedQuoteMutation(actor, quote, "delete");
        await DeleteQuoteAsync(quoteId);
    }

    public async Task DeleteQuoteAsync(int quoteId)
    {
        var quote = await GetQuoteAsync(quoteId);
        if (quote is null)
        {
            throw new InvalidOperationException($"Quote {quoteId} was not found.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        if (ShouldArchiveBeforeDelete(quote))
        {
            await ArchiveQuoteAsync(connection, transaction, quote);
        }

        await DeleteStoredFilesForQuoteAsync(connection, transaction, quoteId);

        await using (var deleteBlobFiles = connection.CreateCommand())
        {
            deleteBlobFiles.Transaction = transaction;
            deleteBlobFiles.CommandText = @"
                DELETE FROM QuoteBlobFiles
                WHERE QuoteId = $id
                   OR LineItemId IN (SELECT Id FROM QuoteLineItems WHERE QuoteId = $id);";
            deleteBlobFiles.Parameters.AddWithValue("$id", quoteId);
            await deleteBlobFiles.ExecuteNonQueryAsync();
        }

        await using (var deleteLineItemFiles = connection.CreateCommand())
        {
            deleteLineItemFiles.Transaction = transaction;
            deleteLineItemFiles.CommandText = @"
                DELETE FROM LineItemFiles
                WHERE LineItemId IN (SELECT Id FROM QuoteLineItems WHERE QuoteId = $id);";
            deleteLineItemFiles.Parameters.AddWithValue("$id", quoteId);
            await deleteLineItemFiles.ExecuteNonQueryAsync();
        }

        await using (var deleteLineItems = connection.CreateCommand())
        {
            deleteLineItems.Transaction = transaction;
            deleteLineItems.CommandText = "DELETE FROM QuoteLineItems WHERE QuoteId = $id;";
            deleteLineItems.Parameters.AddWithValue("$id", quoteId);
            await deleteLineItems.ExecuteNonQueryAsync();
        }

        await using (var deleteAuditEvents = connection.CreateCommand())
        {
            deleteAuditEvents.Transaction = transaction;
            deleteAuditEvents.CommandText = "DELETE FROM QuoteAuditEvents WHERE QuoteId = $id;";
            deleteAuditEvents.Parameters.AddWithValue("$id", quoteId);
            await deleteAuditEvents.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Quotes WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", quoteId);

        var affected = await command.ExecuteNonQueryAsync();
        if (affected == 0)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException($"Quote {quoteId} was not found.");
        }

        await transaction.CommitAsync();

        if (_realtimeDataService is not null)
        {
            await _realtimeDataService.PublishChangeAsync("Quotes", "delete");
        }
    }

    public async Task<IReadOnlyList<ArchivedQuoteSummary>> GetArchivedQuotesAsync()
    {
        var results = new List<ArchivedQuoteSummary>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT ArchiveId,
                   OriginalQuoteId,
                   LifecycleQuoteId,
                   CustomerId,
                   CustomerName,
                   Status,
                   CreatedUtc,
                   LastUpdatedUtc,
                   CompletedUtc,
                   WonUtc,
                   ArchivedUtc
            FROM ArchivedQuotes
            ORDER BY ArchivedUtc DESC;";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ArchivedQuoteSummary
            {
                ArchiveId = reader.GetInt32(0),
                OriginalQuoteId = reader.GetInt32(1),
                LifecycleQuoteId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                CustomerId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                CustomerName = reader.GetString(4),
                Status = (QuoteStatus)reader.GetInt32(5),
                CreatedUtc = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                LastUpdatedUtc = DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CompletedUtc = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                WonUtc = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ArchivedUtc = DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }

        return results;
    }

    public async Task<ArchivedQuote?> GetArchivedQuoteAsync(int archiveId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT ArchiveId,
                   OriginalQuoteId,
                   LifecycleQuoteId,
                   CustomerId,
                   CustomerName,
                   Status,
                   CreatedUtc,
                   LastUpdatedUtc,
                   WonUtc,
                   WonByUserId,
                   LostUtc,
                   LostByUserId,
                   ExpiredUtc,
                   ExpiredByUserId,
                   CompletedUtc,
                   CompletedByUserId,
                   ArchivedUtc
            FROM ArchivedQuotes
            WHERE ArchiveId = $archiveId;";
        command.Parameters.AddWithValue("$archiveId", archiveId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var archivedQuote = new ArchivedQuote
        {
            ArchiveId = reader.GetInt32(0),
            OriginalQuoteId = reader.GetInt32(1),
            LifecycleQuoteId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            CustomerId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
            CustomerName = reader.GetString(4),
            Status = (QuoteStatus)reader.GetInt32(5),
            CreatedUtc = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            LastUpdatedUtc = DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            WonUtc = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            WonByUserId = reader.IsDBNull(9) ? null : reader.GetString(9),
            LostUtc = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            LostByUserId = reader.IsDBNull(11) ? null : reader.GetString(11),
            ExpiredUtc = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            ExpiredByUserId = reader.IsDBNull(13) ? null : reader.GetString(13),
            CompletedUtc = reader.IsDBNull(14) ? null : DateTime.Parse(reader.GetString(14), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            CompletedByUserId = reader.IsDBNull(15) ? null : reader.GetString(15),
            ArchivedUtc = DateTime.Parse(reader.GetString(16), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        };

        archivedQuote.LineItems = await ReadArchivedLineItemsAsync(connection, archiveId);
        return archivedQuote;
    }


    public async Task<byte[]?> TryGetGlbCacheByStepHashAsync(string stepSha256, int lineItemId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT GlbData
            FROM StepGlbCache
            WHERE StepSha256 = $stepSha256
              AND LineItemId = $lineItemId
            ORDER BY Id DESC
            LIMIT 1;";
        command.Parameters.AddWithValue("$stepSha256", stepSha256);
        command.Parameters.AddWithValue("$lineItemId", lineItemId);

        var result = await command.ExecuteScalarAsync();
        if (result is DBNull or null)
        {
            return null;
        }

        await using var touch = connection.CreateCommand();
        touch.CommandText = @"
            UPDATE StepGlbCache
            SET LastAccessedUtc = $lastAccessedUtc
            WHERE StepSha256 = $stepSha256
              AND LineItemId = $lineItemId;";
        touch.Parameters.AddWithValue("$lastAccessedUtc", DateTime.UtcNow.ToString("O"));
        touch.Parameters.AddWithValue("$stepSha256", stepSha256);
        touch.Parameters.AddWithValue("$lineItemId", lineItemId);
        await touch.ExecuteNonQueryAsync();

        return (byte[])result;
    }

    public async Task UpsertGlbCacheAsync(int lineItemId, string stepSha256, byte[] glbData, string sourceFileName, string sourcePath)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var now = DateTime.UtcNow.ToString("O");
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO StepGlbCache (
                LineItemId,
                StepSha256,
                SourceFileName,
                SourcePath,
                GlbData,
                CreatedUtc,
                LastAccessedUtc)
            VALUES (
                $lineItemId,
                $stepSha256,
                $sourceFileName,
                $sourcePath,
                $glbData,
                $createdUtc,
                $lastAccessedUtc)
            ON CONFLICT(LineItemId, StepSha256)
            DO UPDATE SET
                GlbData = excluded.GlbData,
                SourceFileName = excluded.SourceFileName,
                SourcePath = excluded.SourcePath,
                LastAccessedUtc = excluded.LastAccessedUtc;";
        command.Parameters.AddWithValue("$lineItemId", lineItemId);
        command.Parameters.AddWithValue("$stepSha256", stepSha256);
        command.Parameters.AddWithValue("$sourceFileName", sourceFileName ?? string.Empty);
        command.Parameters.AddWithValue("$sourcePath", sourcePath ?? string.Empty);
        command.Parameters.AddWithValue("$glbData", glbData);
        command.Parameters.AddWithValue("$createdUtc", now);
        command.Parameters.AddWithValue("$lastAccessedUtc", now);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteQuoteLineItemFileAsync(int fileId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var storagePath = await GetBlobStoragePathAsync(connection, fileId);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM QuoteBlobFiles WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", fileId);
        await command.ExecuteNonQueryAsync();

        DeletePhysicalBlob(storagePath);
    }

    public async Task DeleteQuoteLineItemAsync(int lineItemId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await DeleteStoredFilesForLineItemAsync(connection, transaction, lineItemId);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM QuoteLineItems WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", lineItemId);
        await command.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
    }

    public async Task<byte[]> GetQuoteBlobContentAsync(int blobId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT StorageRelativePath, BlobData FROM QuoteBlobFiles WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", blobId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Array.Empty<byte>();
        }

        return reader.IsDBNull(1) ? Array.Empty<byte>() : (byte[])reader[1];
    }

    private async Task<string?> GetBlobStoragePathAsync(SqliteConnection connection, int blobId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT StorageRelativePath FROM QuoteBlobFiles WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", blobId);
        var result = await command.ExecuteScalarAsync();
        return result is DBNull or null ? null : Convert.ToString(result, CultureInfo.InvariantCulture);
    }

    private async Task DeleteStoredFilesForQuoteAsync(SqliteConnection connection, SqliteTransaction transaction, int quoteId)
    {
        var paths = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT StorageRelativePath FROM QuoteBlobFiles WHERE QuoteId = $quoteId;";
        command.Parameters.AddWithValue("$quoteId", quoteId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0))
            {
                paths.Add(reader.GetString(0));
            }
        }

        foreach (var path in paths)
        {
            DeletePhysicalBlob(path);
        }
    }

    private async Task DeleteStoredFilesForLineItemAsync(SqliteConnection connection, SqliteTransaction transaction, int lineItemId)
    {
        var paths = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT StorageRelativePath FROM QuoteBlobFiles WHERE LineItemId = $lineItemId;";
        command.Parameters.AddWithValue("$lineItemId", lineItemId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0))
            {
                paths.Add(reader.GetString(0));
            }
        }

        foreach (var path in paths)
        {
            DeletePhysicalBlob(path);
        }
    }

    private void DeletePhysicalBlob(string? storageRelativePath)
    {
        // Blob payloads are now persisted in-database only. StorageRelativePath is
        // retained for backward compatibility with previously file-backed records.
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
            CREATE INDEX IF NOT EXISTS IX_QuoteBlobFiles_QuoteId_LineItemId ON QuoteBlobFiles(QuoteId, LineItemId);
            CREATE INDEX IF NOT EXISTS IX_StepGlbCache_LineItemId_StepSha256 ON StepGlbCache(LineItemId, StepSha256);";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureArchiveIndexesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE INDEX IF NOT EXISTS IX_ArchivedQuotes_OriginalQuoteId ON ArchivedQuotes(OriginalQuoteId);
            CREATE INDEX IF NOT EXISTS IX_ArchivedQuotes_ArchivedUtc ON ArchivedQuotes(ArchivedUtc);
            CREATE INDEX IF NOT EXISTS IX_ArchivedQuoteLineItems_ArchiveId ON ArchivedQuoteLineItems(ArchiveId);
            CREATE INDEX IF NOT EXISTS IX_ArchivedQuoteBlobFiles_ArchiveId ON ArchivedQuoteBlobFiles(ArchiveId);";
        await command.ExecuteNonQueryAsync();
    }


    private static void EnsureAdminForCompletedQuoteMutation(UserAccount actor, Quote quote, string operation)
    {
        if (quote.Status != QuoteStatus.Completed)
        {
            return;
        }

        if (AuthorizationService.HasRole(actor, RoleCatalog.Administrator))
        {
            return;
        }

        throw new UnauthorizedAccessException($"Only administrators can {operation} completed quotes.");
    }

    private static bool ShouldArchiveBeforeDelete(Quote quote)
        => quote.Status is QuoteStatus.Completed or QuoteStatus.Won
           || quote.CompletedUtc.HasValue
           || quote.WonUtc.HasValue;

    private static async Task ArchiveQuoteAsync(SqliteConnection connection, SqliteTransaction transaction, Quote quote)
    {
        await using var insertArchive = connection.CreateCommand();
        insertArchive.Transaction = transaction;
        insertArchive.CommandText = @"
            INSERT INTO ArchivedQuotes (
                OriginalQuoteId,
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
                ExpiredByUserId,
                CompletedUtc,
                CompletedByUserId,
                ArchivedUtc)
            VALUES (
                $originalQuoteId,
                $customerId,
                $customerName,
                $lifecycleQuoteId,
                $status,
                $createdUtc,
                $lastUpdatedUtc,
                $wonUtc,
                $wonByUserId,
                $lostUtc,
                $lostByUserId,
                $expiredUtc,
                $expiredByUserId,
                $completedUtc,
                $completedByUserId,
                $archivedUtc);
            SELECT last_insert_rowid();";

        insertArchive.Parameters.AddWithValue("$originalQuoteId", quote.Id);
        insertArchive.Parameters.AddWithValue("$customerId", quote.CustomerId == 0 ? DBNull.Value : quote.CustomerId);
        insertArchive.Parameters.AddWithValue("$customerName", quote.CustomerName);
        insertArchive.Parameters.AddWithValue("$lifecycleQuoteId", quote.LifecycleQuoteId);
        insertArchive.Parameters.AddWithValue("$status", (int)quote.Status);
        insertArchive.Parameters.AddWithValue("$createdUtc", quote.CreatedUtc.ToString("O"));
        insertArchive.Parameters.AddWithValue("$lastUpdatedUtc", quote.LastUpdatedUtc.ToString("O"));
        AddNullableString(insertArchive, "$wonUtc", quote.WonUtc?.ToString("O"));
        AddNullableString(insertArchive, "$wonByUserId", quote.WonByUserId);
        AddNullableString(insertArchive, "$lostUtc", quote.LostUtc?.ToString("O"));
        AddNullableString(insertArchive, "$lostByUserId", quote.LostByUserId);
        AddNullableString(insertArchive, "$expiredUtc", quote.ExpiredUtc?.ToString("O"));
        AddNullableString(insertArchive, "$expiredByUserId", quote.ExpiredByUserId);
        AddNullableString(insertArchive, "$completedUtc", quote.CompletedUtc?.ToString("O"));
        AddNullableString(insertArchive, "$completedByUserId", quote.CompletedByUserId);
        insertArchive.Parameters.AddWithValue("$archivedUtc", DateTime.UtcNow.ToString("O"));

        var archiveId = Convert.ToInt32(await insertArchive.ExecuteScalarAsync());

        foreach (var lineItem in quote.LineItems)
        {
            await using var insertLine = connection.CreateCommand();
            insertLine.Transaction = transaction;
            insertLine.CommandText = @"
                INSERT INTO ArchivedQuoteLineItems (
                    ArchiveId,
                    OriginalLineItemId,
                    Description,
                    Quantity,
                    UnitPrice,
                    LeadTimeDays,
                    RequiresGForce,
                    RequiresSecondaryProcessing,
                    RequiresPlating,
                    RequiresDfars,
                    RequiresMaterialTestReport,
                    RequiresCertificateOfConformance,
                    RequiresSecondaryOperations,
                    Notes,
                    AssociatedFiles)
                VALUES (
                    $archiveId,
                    $originalLineItemId,
                    $description,
                    $quantity,
                    $unitPrice,
                    $leadTimeDays,
                    $requiresGForce,
                    $requiresSecondaryProcessing,
                    $requiresPlating,
                    $requiresDfars,
                    $requiresMaterialTestReport,
                    $requiresCertificateOfConformance,
                    $requiresSecondaryOperations,
                    $notes,
                    $associatedFiles);";
            insertLine.Parameters.AddWithValue("$archiveId", archiveId);
            insertLine.Parameters.AddWithValue("$originalLineItemId", lineItem.Id);
            insertLine.Parameters.AddWithValue("$description", lineItem.Description);
            insertLine.Parameters.AddWithValue("$quantity", Convert.ToDouble(lineItem.Quantity));
            insertLine.Parameters.AddWithValue("$unitPrice", Convert.ToDouble(lineItem.UnitPrice));
            insertLine.Parameters.AddWithValue("$leadTimeDays", lineItem.LeadTimeDays);
            insertLine.Parameters.AddWithValue("$requiresGForce", lineItem.RequiresGForce ? 1 : 0);
            insertLine.Parameters.AddWithValue("$requiresSecondaryProcessing", lineItem.RequiresSecondaryProcessing ? 1 : 0);
            insertLine.Parameters.AddWithValue("$requiresPlating", lineItem.RequiresPlating ? 1 : 0);
            insertLine.Parameters.AddWithValue("$requiresDfars", lineItem.RequiresDfars ? 1 : 0);
            insertLine.Parameters.AddWithValue("$requiresMaterialTestReport", lineItem.RequiresMaterialTestReport ? 1 : 0);
            insertLine.Parameters.AddWithValue("$requiresCertificateOfConformance", lineItem.RequiresCertificateOfConformance ? 1 : 0);
            insertLine.Parameters.AddWithValue("$requiresSecondaryOperations", lineItem.RequiresSecondaryOperations ? 1 : 0);
            insertLine.Parameters.AddWithValue("$notes", lineItem.Notes ?? string.Empty);
            insertLine.Parameters.AddWithValue("$associatedFiles", string.Join('|', lineItem.AssociatedFiles));
            await insertLine.ExecuteNonQueryAsync();

            foreach (var blob in lineItem.BlobAttachments)
            {
                await using var insertBlob = connection.CreateCommand();
                insertBlob.Transaction = transaction;
                insertBlob.CommandText = @"
                    INSERT INTO ArchivedQuoteBlobFiles (
                        ArchiveId,
                        OriginalBlobId,
                        OriginalLineItemId,
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
                        $archiveId,
                        $originalBlobId,
                        $originalLineItemId,
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
                insertBlob.Parameters.AddWithValue("$archiveId", archiveId);
                insertBlob.Parameters.AddWithValue("$originalBlobId", blob.Id);
                insertBlob.Parameters.AddWithValue("$originalLineItemId", lineItem.Id);
                insertBlob.Parameters.AddWithValue("$lifecycleId", blob.LifecycleId);
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
    }

    private static async Task<List<QuoteLineItem>> ReadArchivedLineItemsAsync(SqliteConnection connection, int archiveId)
    {
        var lineItems = new List<QuoteLineItem>();
        var originalLineIds = new Dictionary<int, int>();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id,
                   OriginalLineItemId,
                   Description,
                   Quantity,
                   UnitPrice,
                   LeadTimeDays,
                   RequiresGForce,
                   RequiresSecondaryProcessing,
                   RequiresPlating,
                   RequiresDfars,
                   RequiresMaterialTestReport,
                   RequiresCertificateOfConformance,
                   RequiresSecondaryOperations,
                   Notes,
                   AssociatedFiles
            FROM ArchivedQuoteLineItems
            WHERE ArchiveId = $archiveId
            ORDER BY Id;";
        command.Parameters.AddWithValue("$archiveId", archiveId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var lineId = reader.GetInt32(0);
            var originalLineId = reader.GetInt32(1);
            originalLineIds[originalLineId] = lineItems.Count;

            var associatedFilesRaw = reader.IsDBNull(14) ? string.Empty : reader.GetString(14);
            lineItems.Add(new QuoteLineItem
            {
                Id = lineId,
                Description = reader.GetString(2),
                Quantity = Convert.ToDecimal(reader.GetDouble(3)),
                UnitPrice = Convert.ToDecimal(reader.GetDouble(4)),
                LeadTimeDays = reader.GetInt32(5),
                RequiresGForce = reader.GetInt32(6) == 1,
                RequiresSecondaryProcessing = reader.GetInt32(7) == 1,
                RequiresPlating = reader.GetInt32(8) == 1,
                RequiresDfars = reader.GetInt32(9) == 1,
                RequiresMaterialTestReport = reader.GetInt32(10) == 1,
                RequiresCertificateOfConformance = reader.GetInt32(11) == 1,
                RequiresSecondaryOperations = reader.GetInt32(12) == 1,
                Notes = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                AssociatedFiles = associatedFilesRaw.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList(),
                BlobAttachments = new List<QuoteBlobAttachment>()
            });
        }

        await using var blobsCommand = connection.CreateCommand();
        blobsCommand.CommandText = @"
            SELECT OriginalLineItemId,
                   BlobType,
                   FileName,
                   Extension,
                   ContentType,
                   FileSizeBytes,
                   Sha256,
                   UploadedBy,
                   BlobData,
                   UploadedUtc,
                   LifecycleId
            FROM ArchivedQuoteBlobFiles
            WHERE ArchiveId = $archiveId
            ORDER BY Id;";
        blobsCommand.Parameters.AddWithValue("$archiveId", archiveId);

        await using var blobReader = await blobsCommand.ExecuteReaderAsync();
        while (await blobReader.ReadAsync())
        {
            var originalLineId = blobReader.GetInt32(0);
            if (!originalLineIds.TryGetValue(originalLineId, out var index))
            {
                continue;
            }

            lineItems[index].BlobAttachments.Add(new QuoteBlobAttachment
            {
                BlobType = (QuoteBlobType)blobReader.GetInt32(1),
                FileName = blobReader.GetString(2),
                Extension = blobReader.IsDBNull(3) ? string.Empty : blobReader.GetString(3),
                ContentType = blobReader.IsDBNull(4) ? string.Empty : blobReader.GetString(4),
                FileSizeBytes = blobReader.GetInt64(5),
                Sha256 = blobReader.IsDBNull(6) ? Array.Empty<byte>() : (byte[])blobReader[6],
                UploadedBy = blobReader.IsDBNull(7) ? string.Empty : blobReader.GetString(7),
                BlobData = blobReader.IsDBNull(8) ? Array.Empty<byte>() : (byte[])blobReader[8],
                UploadedUtc = DateTime.Parse(blobReader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                LifecycleId = blobReader.IsDBNull(10) ? string.Empty : blobReader.GetString(10)
            });
        }

        return lineItems;
    }


    public async Task<IReadOnlyList<Quote>> GetPurchasingQuotesAsync()
    {
        var all = await GetQuotesAsync();
        return all
            .Where(q => q.Status == QuoteStatus.Won && q.PassedToPurchasingUtc.HasValue)
            .OrderBy(q => q.PassedToPurchasingUtc)
            .ToList();
    }

    public async Task<(bool Success, string Message)> ValidateQuoteFileLinkageAsync(int quoteId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var quoteExistsCommand = connection.CreateCommand();
        quoteExistsCommand.CommandText = "SELECT COUNT(1) FROM Quotes WHERE Id = $quoteId;";
        quoteExistsCommand.Parameters.AddWithValue("$quoteId", quoteId);
        var existsCount = Convert.ToInt32(await quoteExistsCommand.ExecuteScalarAsync());
        if (existsCount == 0)
        {
            return (false, $"Quote {quoteId} does not exist.");
        }

        await using var orphanCountCommand = connection.CreateCommand();
        orphanCountCommand.CommandText = @"
            SELECT COUNT(1)
            FROM QuoteBlobFiles qb
            LEFT JOIN QuoteLineItems li ON li.Id = qb.LineItemId
            WHERE qb.QuoteId = $quoteId
              AND (li.Id IS NULL OR li.QuoteId <> $quoteId);";
        orphanCountCommand.Parameters.AddWithValue("$quoteId", quoteId);
        var orphanCount = Convert.ToInt32(await orphanCountCommand.ExecuteScalarAsync());
        if (orphanCount > 0)
        {
            return (false, $"Quote {quoteId} has {orphanCount} file(s) with broken line-item linkage.");
        }

        await using var invalidDocCountCommand = connection.CreateCommand();
        invalidDocCountCommand.CommandText = @"
            SELECT COUNT(1)
            FROM QuoteBlobFiles qb
            WHERE qb.QuoteId = $quoteId
              AND (TRIM(COALESCE(qb.FileName, '')) = '' OR length(COALESCE(qb.BlobData, X'')) = 0);";
        invalidDocCountCommand.Parameters.AddWithValue("$quoteId", quoteId);
        var invalidDocCount = Convert.ToInt32(await invalidDocCountCommand.ExecuteScalarAsync());
        if (invalidDocCount > 0)
        {
            return (false, $"Quote {quoteId} has {invalidDocCount} invalid file record(s) (missing name/content).");
        }

        await using var totalDocCountCommand = connection.CreateCommand();
        totalDocCountCommand.CommandText = "SELECT COUNT(1) FROM QuoteBlobFiles WHERE QuoteId = $quoteId;";
        totalDocCountCommand.Parameters.AddWithValue("$quoteId", quoteId);
        var totalDocCount = Convert.ToInt32(await totalDocCountCommand.ExecuteScalarAsync());

        return (true, $"Quote {quoteId} file linkage verified ({totalDocCount} file(s) accessible and linked).");
    }

    public static bool HasBlobType(Quote quote, QuoteBlobType blobType)
        => quote.LineItems.SelectMany(li => li.BlobAttachments).Any(blob => blob.BlobType == blobType);

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

    public async Task<IntegrityValidationReport> RunReferentialIntegrityBatchAsync()
    {
        var report = new IntegrityValidationReport { ExecutedUtc = DateTime.UtcNow, Success = true };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var checks = new (string Name, string Sql, string Details)[]
        {
            (
                "QuotesMissingCustomers",
                @"SELECT COUNT(1)
                  FROM Quotes q
                  LEFT JOIN Customers c ON c.Id = q.CustomerId
                  WHERE c.Id IS NULL;",
                "Every quote must reference a valid customer."
            ),
            (
                "LineItemsMissingQuotes",
                @"SELECT COUNT(1)
                  FROM QuoteLineItems li
                  LEFT JOIN Quotes q ON q.Id = li.QuoteId
                  WHERE q.Id IS NULL;",
                "Every quote line item must reference a valid quote."
            ),
            (
                "BlobFilesMissingLineItems",
                @"SELECT COUNT(1)
                  FROM QuoteBlobFiles b
                  LEFT JOIN QuoteLineItems li ON li.Id = b.LineItemId
                  WHERE li.Id IS NULL;",
                "Every quote file must reference a valid line item."
            )
        };

        foreach (var check in checks)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = check.Sql;
            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (count > 0)
            {
                report.Success = false;
                report.Issues.Add(new IntegrityValidationIssue
                {
                    CheckName = check.Name,
                    AffectedRows = count,
                    Details = check.Details
                });
            }
        }

        await using var insertRun = connection.CreateCommand();
        insertRun.CommandText = @"
            INSERT INTO IntegrityValidationRuns (Scope, Success, IssueCount, Details, ExecutedUtc)
            VALUES ($scope, $success, $issueCount, $details, $executedUtc);";
        insertRun.Parameters.AddWithValue("$scope", "quotes");
        insertRun.Parameters.AddWithValue("$success", report.Success ? 1 : 0);
        insertRun.Parameters.AddWithValue("$issueCount", report.Issues.Count);
        insertRun.Parameters.AddWithValue("$details", report.Summary);
        insertRun.Parameters.AddWithValue("$executedUtc", report.ExecutedUtc.ToString("O"));
        await insertRun.ExecuteNonQueryAsync();

        return report;
    }

    public async Task<decimal> GetShopHourlyRateAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await EnsureDefaultQuoteSettingsAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ShopHourlyRate FROM QuoteSettings WHERE Id = 1;";
        var result = await command.ExecuteScalarAsync();
        return result is null || result is DBNull ? 75m : Convert.ToDecimal(result, CultureInfo.InvariantCulture);
    }

    public async Task SaveShopHourlyRateAsync(decimal rate)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO QuoteSettings (Id, ShopHourlyRate)
            VALUES (1, $rate)
            ON CONFLICT(Id) DO UPDATE SET ShopHourlyRate = excluded.ShopHourlyRate;";
        command.Parameters.AddWithValue("$rate", rate);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureDefaultQuoteSettingsAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO QuoteSettings (Id, ShopHourlyRate)
            SELECT 1, 75
            WHERE NOT EXISTS (SELECT 1 FROM QuoteSettings WHERE Id = 1);";
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateQuoteForPersistence(Quote quote)
    {
        if (string.IsNullOrWhiteSpace(quote.CustomerName))
        {
            throw new InvalidOperationException("Customer name is required.");
        }

        if (quote.LineItems.Count == 0)
        {
            throw new InvalidOperationException("A quote must include at least one line item.");
        }

        foreach (var lineItem in quote.LineItems)
        {
            if (string.IsNullOrWhiteSpace(lineItem.Description))
            {
                throw new InvalidOperationException("Each line item requires a description.");
            }

            if (lineItem.Quantity <= 0)
            {
                throw new InvalidOperationException("Each line item must have a quantity greater than zero.");
            }
        }
    }

    private static async Task EnsureRelationshipTriggersAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TRIGGER IF NOT EXISTS trg_Quotes_StatusChange
            AFTER UPDATE OF Status ON Quotes
            FOR EACH ROW
            WHEN OLD.Status <> NEW.Status
            BEGIN
                INSERT INTO RelationshipChangeLog (TableName, ChangeType, RelationshipKey, Details, ChangedUtc)
                VALUES ('Quotes', 'status_change', NEW.Id,
                        'Quote status changed from ' || OLD.Status || ' to ' || NEW.Status,
                        strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            END;";
        await command.ExecuteNonQueryAsync();
    }

}
