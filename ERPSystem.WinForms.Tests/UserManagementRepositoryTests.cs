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
}
