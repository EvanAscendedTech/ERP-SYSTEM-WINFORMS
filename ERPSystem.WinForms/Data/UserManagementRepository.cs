using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace ERPSystem.WinForms.Data;

public class UserManagementRepository
{
    private readonly string _connectionString;
    private readonly RealtimeDataService? _realtimeDataService;

    public UserManagementRepository(string dbPath, RealtimeDataService? realtimeDataService = null)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, ForeignKeys = true }.ToString();
        _realtimeDataService = realtimeDataService;
    }

    public async Task InitializeDatabaseAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                IconPath TEXT NOT NULL DEFAULT '',
                IconBlob BLOB,
                IsOnline INTEGER NOT NULL DEFAULT 0,
                LastActivityUtc TEXT
            );

            CREATE TABLE IF NOT EXISTS AccountRequests (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RequestedUsername TEXT NOT NULL,
                RequestNote TEXT NOT NULL,
                TermsAccepted INTEGER NOT NULL,
                RequestedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Roles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Permissions TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS UserRoles (
                UserId INTEGER NOT NULL,
                RoleId INTEGER NOT NULL,
                PRIMARY KEY (UserId, RoleId),
                FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE,
                FOREIGN KEY(RoleId) REFERENCES Roles(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS UserPreferences (
                UserId INTEGER NOT NULL,
                PreferenceKey TEXT NOT NULL,
                PreferenceValue TEXT NOT NULL DEFAULT '',
                LastUpdatedUtc TEXT NOT NULL,
                PRIMARY KEY (UserId, PreferenceKey),
                FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS PurchasingLayouts (
                UserId INTEGER PRIMARY KEY,
                LeftPanelProportion REAL NOT NULL,
                RightTopPanelProportion REAL NOT NULL,
                RightBottomPanelProportion REAL NOT NULL,
                LastUpdatedUtc TEXT NOT NULL,
                FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS PasswordResetRequests (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                RequestedUtc TEXT NOT NULL,
                Note TEXT NOT NULL DEFAULT '',
                IsResolved INTEGER NOT NULL DEFAULT 0,
                ResolvedUtc TEXT,
                FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AuditLogEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OccurredUtc TEXT NOT NULL,
                Username TEXT NOT NULL,
                RoleSnapshot TEXT NOT NULL,
                Module TEXT NOT NULL,
                Action TEXT NOT NULL,
                Details TEXT NOT NULL DEFAULT ''
            );";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();

        await EnsureColumnExistsAsync(connection, "Users", "IconPath", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "Users", "IconBlob", "BLOB");
        await EnsureColumnExistsAsync(connection, "Users", "IsOnline", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "Users", "LastActivityUtc", "TEXT");
        await EnsureColumnExistsAsync(connection, "Users", "MustResetPassword", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnExistsAsync(connection, "Users", "TemporaryPasswordIssuedUtc", "TEXT");
    }


    private static async Task EnsureColumnExistsAsync(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
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
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alter.ExecuteNonQueryAsync();
    }

    public async Task<int> SaveRoleAsync(RoleDefinition role)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Roles (Name, Permissions)
            VALUES ($name, $permissions)
            ON CONFLICT(Name) DO UPDATE SET Permissions = excluded.Permissions;

            SELECT Id FROM Roles WHERE Name = $name;";
        command.Parameters.AddWithValue("$name", role.Name);
        command.Parameters.AddWithValue("$permissions", string.Join(',', role.Permissions.Distinct().Select(p => (int)p)));

        var roleId = Convert.ToInt32(await command.ExecuteScalarAsync());

        if (_realtimeDataService is not null)
        {
            await _realtimeDataService.PublishChangeAsync("Users", "save-role");
        }

        return roleId;
    }

    public async Task<int> SaveUserAsync(UserAccount user)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO Users (Username, DisplayName, PasswordHash, IsActive, IconPath, IconBlob, MustResetPassword, TemporaryPasswordIssuedUtc)
            VALUES ($username, $displayName, $passwordHash, $isActive, $iconPath, $iconBlob, $mustResetPassword, $temporaryPasswordIssuedUtc)
            ON CONFLICT(Username) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                PasswordHash = excluded.PasswordHash,
                IsActive = excluded.IsActive,
                IconPath = excluded.IconPath,
                IconBlob = excluded.IconBlob,
                MustResetPassword = excluded.MustResetPassword,
                TemporaryPasswordIssuedUtc = excluded.TemporaryPasswordIssuedUtc;

            SELECT Id FROM Users WHERE Username = $username;";

        command.Parameters.AddWithValue("$username", user.Username);
        command.Parameters.AddWithValue("$displayName", user.DisplayName);
        command.Parameters.AddWithValue("$passwordHash", user.PasswordHash);
        command.Parameters.AddWithValue("$isActive", user.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$iconPath", user.IconPath ?? string.Empty);
        command.Parameters.AddWithValue("$iconBlob", (object?)user.IconBlob ?? DBNull.Value);
        command.Parameters.AddWithValue("$mustResetPassword", user.MustResetPassword ? 1 : 0);
        command.Parameters.AddWithValue("$temporaryPasswordIssuedUtc", user.TemporaryPasswordIssuedUtc?.ToString("O") ?? (object)DBNull.Value);
        var userId = Convert.ToInt32(await command.ExecuteScalarAsync());

        await using var deleteUserRoles = connection.CreateCommand();
        deleteUserRoles.Transaction = transaction;
        deleteUserRoles.CommandText = "DELETE FROM UserRoles WHERE UserId = $userId";
        deleteUserRoles.Parameters.AddWithValue("$userId", userId);
        await deleteUserRoles.ExecuteNonQueryAsync();

        var normalizedRoleNames = user.Roles
            .Select(role => RoleCatalog.NormalizeRoleName(role.Name))
            .Where(roleName => roleName is not null)
            .Select(roleName => roleName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var roleName in normalizedRoleNames)
        {
            var roleId = await UpsertRoleAsync(connection, transaction, AuthorizationService.BuildRole(roleName));
            await using var insertUserRole = connection.CreateCommand();
            insertUserRole.Transaction = transaction;
            insertUserRole.CommandText = "INSERT INTO UserRoles (UserId, RoleId) VALUES ($userId, $roleId)";
            insertUserRole.Parameters.AddWithValue("$userId", userId);
            insertUserRole.Parameters.AddWithValue("$roleId", roleId);
            await insertUserRole.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        if (_realtimeDataService is not null)
        {
            await _realtimeDataService.PublishChangeAsync("Users", "save-user");
        }

        return userId;
    }

    private static async Task<int> UpsertRoleAsync(SqliteConnection connection, SqliteTransaction transaction, RoleDefinition role)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO Roles (Name, Permissions)
            VALUES ($name, $permissions)
            ON CONFLICT(Name) DO UPDATE SET Permissions = excluded.Permissions;

            SELECT Id FROM Roles WHERE Name = $name;";
        command.Parameters.AddWithValue("$name", role.Name);
        command.Parameters.AddWithValue("$permissions", string.Join(',', role.Permissions.Distinct().Select(p => (int)p)));
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<UserAccount>> GetUsersAsync()
    {
        var users = new List<UserAccount>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT u.Id, u.Username, u.DisplayName, u.PasswordHash, u.IsActive, u.IconPath,
                   u.IconBlob, u.IsOnline, u.LastActivityUtc, u.MustResetPassword, u.TemporaryPasswordIssuedUtc,
                   r.Id, r.Name, r.Permissions
            FROM Users u
            LEFT JOIN UserRoles ur ON ur.UserId = u.Id
            LEFT JOIN Roles r ON r.Id = ur.RoleId
            ORDER BY u.Username;";

        await using var reader = await command.ExecuteReaderAsync();
        var map = new Dictionary<int, UserAccount>();

        while (await reader.ReadAsync())
        {
            var userId = reader.GetInt32(0);
            if (!map.TryGetValue(userId, out var user))
            {
                user = new UserAccount
                {
                    Id = userId,
                    Username = reader.GetString(1),
                    DisplayName = reader.GetString(2),
                    PasswordHash = reader.GetString(3),
                    IsActive = reader.GetInt32(4) == 1,
                    IconPath = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    IconBlob = reader.IsDBNull(6) ? null : (byte[])reader[6],
                    IsOnline = !reader.IsDBNull(7) && reader.GetInt32(7) == 1,
                    LastActivityUtc = reader.IsDBNull(8)
                        ? null
                        : DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    MustResetPassword = !reader.IsDBNull(9) && reader.GetInt32(9) == 1,
                    TemporaryPasswordIssuedUtc = reader.IsDBNull(10)
                        ? null
                        : DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                };
                map[userId] = user;
                users.Add(user);
            }

            if (reader.IsDBNull(11))
            {
                continue;
            }

            var permissionsText = reader.GetString(13);
            var permissions = string.IsNullOrWhiteSpace(permissionsText)
                ? new List<UserPermission>()
                : permissionsText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse)
                .Select(i => (UserPermission)i)
                .ToList();

            user.Roles.Add(new RoleDefinition
            {
                Id = reader.GetInt32(11),
                Name = reader.GetString(12),
                Permissions = permissions
            });
        }

        return users;
    }


    public async Task<string?> GetUserPreferenceAsync(int userId, string preferenceKey)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT PreferenceValue
            FROM UserPreferences
            WHERE UserId = $userId AND PreferenceKey = $preferenceKey;";
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$preferenceKey", preferenceKey);

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    public async Task SaveUserPreferenceAsync(int userId, string preferenceKey, string preferenceValue)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO UserPreferences (UserId, PreferenceKey, PreferenceValue, LastUpdatedUtc)
            VALUES ($userId, $preferenceKey, $preferenceValue, $lastUpdatedUtc)
            ON CONFLICT(UserId, PreferenceKey) DO UPDATE SET
                PreferenceValue = excluded.PreferenceValue,
                LastUpdatedUtc = excluded.LastUpdatedUtc;";
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$preferenceKey", preferenceKey);
        command.Parameters.AddWithValue("$preferenceValue", preferenceValue);
        command.Parameters.AddWithValue("$lastUpdatedUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();

        if (_realtimeDataService is not null)
        {
            await _realtimeDataService.PublishChangeAsync("Users", "save-preference");
        }
    }

    public async Task<PurchasingLayoutSetting?> GetPurchasingLayoutAsync(int userId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT UserId, LeftPanelProportion, RightTopPanelProportion, RightBottomPanelProportion, LastUpdatedUtc
            FROM PurchasingLayouts
            WHERE UserId = $userId;";
        command.Parameters.AddWithValue("$userId", userId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PurchasingLayoutSetting
        {
            UserId = reader.GetInt32(0),
            LeftPanelProportion = reader.GetDouble(1),
            RightTopPanelProportion = reader.GetDouble(2),
            RightBottomPanelProportion = reader.GetDouble(3),
            LastUpdatedUtc = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        };
    }

    public async Task SavePurchasingLayoutAsync(PurchasingLayoutSetting layout)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO PurchasingLayouts (UserId, LeftPanelProportion, RightTopPanelProportion, RightBottomPanelProportion, LastUpdatedUtc)
            VALUES ($userId, $leftPanelProportion, $rightTopPanelProportion, $rightBottomPanelProportion, $lastUpdatedUtc)
            ON CONFLICT(UserId) DO UPDATE SET
                LeftPanelProportion = excluded.LeftPanelProportion,
                RightTopPanelProportion = excluded.RightTopPanelProportion,
                RightBottomPanelProportion = excluded.RightBottomPanelProportion,
                LastUpdatedUtc = excluded.LastUpdatedUtc;";
        command.Parameters.AddWithValue("$userId", layout.UserId);
        command.Parameters.AddWithValue("$leftPanelProportion", Math.Clamp(layout.LeftPanelProportion, 0.01d, 0.99d));
        command.Parameters.AddWithValue("$rightTopPanelProportion", Math.Clamp(layout.RightTopPanelProportion, 0.01d, 0.99d));
        command.Parameters.AddWithValue("$rightBottomPanelProportion", Math.Clamp(layout.RightBottomPanelProportion, 0.01d, 0.99d));
        command.Parameters.AddWithValue("$lastUpdatedUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();

        if (_realtimeDataService is not null)
        {
            await _realtimeDataService.PublishChangeAsync("Users", "save-purchasing-layout");
        }
    }


    public async Task<UserAccount?> FindByUsernameAsync(string username)
    {
        var users = await GetUsersAsync();
        return users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SetOnlineStatusAsync(int userId, bool isOnline)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Users
            SET IsOnline = $isOnline,
                LastActivityUtc = $lastActivityUtc
            WHERE Id = $userId;";
        command.Parameters.AddWithValue("$isOnline", isOnline ? 1 : 0);
        command.Parameters.AddWithValue("$lastActivityUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$userId", userId);
        await command.ExecuteNonQueryAsync();

        if (_realtimeDataService is not null)
        {
            await _realtimeDataService.PublishChangeAsync("Users", isOnline ? "online" : "offline");
        }
    }

    public async Task TouchUserActivityAsync(int userId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Users
            SET IsOnline = 1,
                LastActivityUtc = $lastActivityUtc
            WHERE Id = $userId;";
        command.Parameters.AddWithValue("$lastActivityUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$userId", userId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task MarkUsersOfflineByInactivityAsync(TimeSpan inactivityThreshold)
    {
        var cutoffUtc = DateTime.UtcNow - inactivityThreshold;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Users
            SET IsOnline = 0
            WHERE IsOnline = 1
              AND LastActivityUtc IS NOT NULL
              AND LastActivityUtc < $cutoffUtc;";
        command.Parameters.AddWithValue("$cutoffUtc", cutoffUtc.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveAccountRequestAsync(AccountRequest request)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO AccountRequests (RequestedUsername, RequestNote, TermsAccepted, RequestedUtc)
            VALUES ($username, $note, $termsAccepted, $requestedUtc);";
        command.Parameters.AddWithValue("$username", request.RequestedUsername.Trim());
        command.Parameters.AddWithValue("$note", request.RequestNote.Trim());
        command.Parameters.AddWithValue("$termsAccepted", request.TermsAccepted ? 1 : 0);
        command.Parameters.AddWithValue("$requestedUtc", request.RequestedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<AccountRequest>> GetAccountRequestsAsync()
    {
        var requests = new List<AccountRequest>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, RequestedUsername, RequestNote, TermsAccepted, RequestedUtc
            FROM AccountRequests
            ORDER BY RequestedUtc DESC;";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            requests.Add(new AccountRequest
            {
                Id = reader.GetInt32(0),
                RequestedUsername = reader.GetString(1),
                RequestNote = reader.GetString(2),
                TermsAccepted = reader.GetInt32(3) == 1,
                RequestedUtc = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }

        return requests;
    }


    public async Task SavePasswordResetRequestAsync(int userId, string note)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO PasswordResetRequests (UserId, RequestedUtc, Note, IsResolved)
            VALUES ($userId, $requestedUtc, $note, 0);";
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$requestedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$note", note.Trim());
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<PasswordResetRequest>> GetPasswordResetRequestsAsync(int? userId = null)
    {
        var requests = new List<PasswordResetRequest>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT pr.Id, pr.UserId, u.Username, pr.RequestedUtc, pr.Note, pr.IsResolved, pr.ResolvedUtc
            FROM PasswordResetRequests pr
            INNER JOIN Users u ON u.Id = pr.UserId
            WHERE ($userId IS NULL OR pr.UserId = $userId)
            ORDER BY pr.RequestedUtc DESC;";
        command.Parameters.AddWithValue("$userId", userId.HasValue ? userId.Value : DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            requests.Add(new PasswordResetRequest
            {
                Id = reader.GetInt32(0),
                UserId = reader.GetInt32(1),
                Username = reader.GetString(2),
                RequestedUtc = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Note = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                IsResolved = reader.GetInt32(5) == 1,
                ResolvedUtc = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }

        return requests;
    }

    public async Task IssueTemporaryPasswordAsync(int userId, string temporaryPassword)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = @"
                UPDATE Users
                SET PasswordHash = $passwordHash,
                    MustResetPassword = 1,
                    TemporaryPasswordIssuedUtc = $issuedUtc
                WHERE Id = $userId;";
            command.Parameters.AddWithValue("$passwordHash", AuthorizationService.HashPassword(temporaryPassword));
            command.Parameters.AddWithValue("$issuedUtc", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$userId", userId);
            await command.ExecuteNonQueryAsync();
        }

        await using (var markRequests = connection.CreateCommand())
        {
            markRequests.Transaction = tx;
            markRequests.CommandText = @"
                UPDATE PasswordResetRequests
                SET IsResolved = 1,
                    ResolvedUtc = $resolvedUtc
                WHERE UserId = $userId AND IsResolved = 0;";
            markRequests.Parameters.AddWithValue("$resolvedUtc", DateTime.UtcNow.ToString("O"));
            markRequests.Parameters.AddWithValue("$userId", userId);
            await markRequests.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task WriteAuditLogAsync(AuditLogEntry entry)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO AuditLogEntries (OccurredUtc, Username, RoleSnapshot, Module, Action, Details)
            VALUES ($occurredUtc, $username, $roleSnapshot, $module, $action, $details);";
        command.Parameters.AddWithValue("$occurredUtc", entry.OccurredUtc.ToString("O"));
        command.Parameters.AddWithValue("$username", entry.Username);
        command.Parameters.AddWithValue("$roleSnapshot", entry.RoleSnapshot);
        command.Parameters.AddWithValue("$module", entry.Module);
        command.Parameters.AddWithValue("$action", entry.Action);
        command.Parameters.AddWithValue("$details", entry.Details);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetAuditLogEntriesAsync(string? username = null)
    {
        var logs = new List<AuditLogEntry>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, OccurredUtc, Username, RoleSnapshot, Module, Action, Details
            FROM AuditLogEntries
            WHERE ($username IS NULL OR Username = $username)
            ORDER BY OccurredUtc DESC
            LIMIT 500;";
        command.Parameters.AddWithValue("$username", string.IsNullOrWhiteSpace(username) ? DBNull.Value : username.Trim());

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            logs.Add(new AuditLogEntry
            {
                Id = reader.GetInt64(0),
                OccurredUtc = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Username = reader.GetString(2),
                RoleSnapshot = reader.GetString(3),
                Module = reader.GetString(4),
                Action = reader.GetString(5),
                Details = reader.GetString(6)
            });
        }

        return logs;
    }

    public static string BuildRoleSnapshot(UserAccount user)
        => string.Join(", ", user.Roles.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase));

}
