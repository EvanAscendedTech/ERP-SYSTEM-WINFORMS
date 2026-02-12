using ERPSystem.WinForms.Models;
using Microsoft.Data.Sqlite;

namespace ERPSystem.WinForms.Data;

public class UserManagementRepository
{
    private readonly string _connectionString;

    public UserManagementRepository(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
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
                IconPath TEXT NOT NULL DEFAULT ''
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
            );";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();

        await EnsureColumnExistsAsync(connection, "Users", "IconPath", "TEXT NOT NULL DEFAULT ''");
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

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<int> SaveUserAsync(UserAccount user)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO Users (Username, DisplayName, PasswordHash, IsActive, IconPath)
            VALUES ($username, $displayName, $passwordHash, $isActive, $iconPath)
            ON CONFLICT(Username) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                PasswordHash = excluded.PasswordHash,
                IsActive = excluded.IsActive,
                IconPath = excluded.IconPath;

            SELECT Id FROM Users WHERE Username = $username;";

        command.Parameters.AddWithValue("$username", user.Username);
        command.Parameters.AddWithValue("$displayName", user.DisplayName);
        command.Parameters.AddWithValue("$passwordHash", user.PasswordHash);
        command.Parameters.AddWithValue("$isActive", user.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$iconPath", user.IconPath ?? string.Empty);
        var userId = Convert.ToInt32(await command.ExecuteScalarAsync());

        await using var deleteUserRoles = connection.CreateCommand();
        deleteUserRoles.Transaction = transaction;
        deleteUserRoles.CommandText = "DELETE FROM UserRoles WHERE UserId = $userId";
        deleteUserRoles.Parameters.AddWithValue("$userId", userId);
        await deleteUserRoles.ExecuteNonQueryAsync();

        foreach (var role in user.Roles)
        {
            var roleId = await UpsertRoleAsync(connection, transaction, role);
            await using var insertUserRole = connection.CreateCommand();
            insertUserRole.Transaction = transaction;
            insertUserRole.CommandText = "INSERT INTO UserRoles (UserId, RoleId) VALUES ($userId, $roleId)";
            insertUserRole.Parameters.AddWithValue("$userId", userId);
            insertUserRole.Parameters.AddWithValue("$roleId", roleId);
            await insertUserRole.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
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
                    IconPath = reader.IsDBNull(5) ? string.Empty : reader.GetString(5)
                };
                map[userId] = user;
                users.Add(user);
            }

            if (reader.IsDBNull(6))
            {
                continue;
            }

            var permissionsText = reader.GetString(8);
            var permissions = string.IsNullOrWhiteSpace(permissionsText)
                ? new List<UserPermission>()
                : permissionsText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse)
                    .Select(i => (UserPermission)i)
                    .ToList();

            user.Roles.Add(new RoleDefinition
            {
                Id = reader.GetInt32(6),
                Name = reader.GetString(7),
                Permissions = permissions
            });
        }

        return users;
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
            SET IsActive = 1
            WHERE Id = $userId;";
        command.Parameters.AddWithValue("$userId", userId);
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
                RequestedUtc = DateTime.Parse(reader.GetString(4))
            });
        }

        return requests;
    }

}
