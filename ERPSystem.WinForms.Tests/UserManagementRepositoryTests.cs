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

    [Fact]
    public async Task SetOnlineStatusAsync_TracksOnlineAndLastActivity()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-online-{Guid.NewGuid():N}.db");
        var repository = new UserManagementRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        await repository.SaveUserAsync(new UserAccount
        {
            Username = "presence-user",
            DisplayName = "Presence User",
            PasswordHash = "hash",
            IsActive = true
        });

        var user = Assert.Single(await repository.GetUsersAsync());
        await repository.SetOnlineStatusAsync(user.Id, true);

        var onlineUser = Assert.Single(await repository.GetUsersAsync());
        Assert.True(onlineUser.IsOnline);
        Assert.True(onlineUser.LastActivityUtc.HasValue);

        await repository.MarkUsersOfflineByInactivityAsync(TimeSpan.Zero);

        var offlineUser = Assert.Single(await repository.GetUsersAsync());
        Assert.False(offlineUser.IsOnline);

        File.Delete(dbPath);
    }


    [Fact]
    public async Task UserPreferences_RoundTripPerUser()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-prefs-{Guid.NewGuid():N}.db");
        var repository = new UserManagementRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        await repository.SaveUserAsync(new UserAccount
        {
            Username = "layout.user",
            DisplayName = "Layout User",
            PasswordHash = "hash",
            IsActive = true
        });

        var user = Assert.Single(await repository.GetUsersAsync());
        await repository.SaveUserPreferenceAsync(user.Id, "purchasing.layout", "{\"MainSplitterDistance\":500,\"DocsSplitterDistance\":180}");

        var loaded = await repository.GetUserPreferenceAsync(user.Id, "purchasing.layout");
        Assert.NotNull(loaded);
        Assert.Contains("MainSplitterDistance", loaded);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task PurchasingLayouts_ArePersistedAndIsolatedPerUser()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-purchasing-layout-{Guid.NewGuid():N}.db");
        var repository = new UserManagementRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        await repository.SaveUserAsync(new UserAccount
        {
            Username = "layout-a",
            DisplayName = "Layout A",
            PasswordHash = "hash",
            IsActive = true
        });

        await repository.SaveUserAsync(new UserAccount
        {
            Username = "layout-b",
            DisplayName = "Layout B",
            PasswordHash = "hash",
            IsActive = true
        });

        var users = await repository.GetUsersAsync();
        var userA = Assert.Single(users.Where(u => u.Username == "layout-a"));
        var userB = Assert.Single(users.Where(u => u.Username == "layout-b"));

        await repository.SavePurchasingLayoutAsync(new PurchasingLayoutSetting
        {
            UserId = userA.Id,
            LeftPanelProportion = 0.62,
            RightTopPanelProportion = 0.35,
            RightBottomPanelProportion = 0.65
        });

        await repository.SavePurchasingLayoutAsync(new PurchasingLayoutSetting
        {
            UserId = userB.Id,
            LeftPanelProportion = 0.41,
            RightTopPanelProportion = 0.57,
            RightBottomPanelProportion = 0.43
        });

        var loadedA = await repository.GetPurchasingLayoutAsync(userA.Id);
        var loadedB = await repository.GetPurchasingLayoutAsync(userB.Id);

        Assert.NotNull(loadedA);
        Assert.NotNull(loadedB);
        Assert.Equal(0.62, loadedA!.LeftPanelProportion, 3);
        Assert.Equal(0.35, loadedA.RightTopPanelProportion, 3);
        Assert.Equal(0.41, loadedB!.LeftPanelProportion, 3);
        Assert.Equal(0.57, loadedB.RightTopPanelProportion, 3);

        var reloadedRepository = new UserManagementRepository(dbPath);
        var persistedA = await reloadedRepository.GetPurchasingLayoutAsync(userA.Id);
        Assert.NotNull(persistedA);
        Assert.Equal(0.62, persistedA!.LeftPanelProportion, 3);

        File.Delete(dbPath);
    }

}
