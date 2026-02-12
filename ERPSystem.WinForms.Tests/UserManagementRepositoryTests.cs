using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Tests;

public class UserManagementRepositoryTests
{
    [Fact]
    public async Task SaveUserAsync_StoresRoleAndPermissions()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-user-{Guid.NewGuid():N}.db");
        var repository = new UserManagementRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var user = new UserAccount
        {
            Username = "jdoe",
            DisplayName = "John Doe",
            PasswordHash = "hash",
            Roles =
            [
                new RoleDefinition
                {
                    Name = "Manager",
                    Permissions = [UserPermission.ViewProduction, UserPermission.ManageUsers]
                }
            ]
        };

        await repository.SaveUserAsync(user);
        var users = await repository.GetUsersAsync();

        var loaded = Assert.Single(users);
        var role = Assert.Single(loaded.Roles);
        Assert.Contains(UserPermission.ManageUsers, role.Permissions);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task GetAccountRequestsAsync_ReturnsMostRecentRequestWithUtcTimestamp()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-requests-{Guid.NewGuid():N}.db");
        var repository = new UserManagementRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var older = DateTime.UtcNow.AddMinutes(-10);
        var newer = DateTime.UtcNow;

        await repository.SaveAccountRequestAsync(new AccountRequest
        {
            RequestedUsername = "older-user",
            RequestNote = "older",
            TermsAccepted = true,
            RequestedUtc = older
        });

        await repository.SaveAccountRequestAsync(new AccountRequest
        {
            RequestedUsername = "newer-user",
            RequestNote = "newer",
            TermsAccepted = true,
            RequestedUtc = newer
        });

        var requests = await repository.GetAccountRequestsAsync();

        Assert.Equal("newer-user", requests[0].RequestedUsername);
        Assert.Equal(DateTimeKind.Utc, requests[0].RequestedUtc.Kind);

        File.Delete(dbPath);
    }
}
