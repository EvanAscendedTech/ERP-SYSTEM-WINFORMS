using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Tests;

public class SettingsAndAuthorizationTests
{
    [Fact]
    public async Task AppSettingsService_SaveAndLoad_RoundTrips()
    {
        var jsonPath = Path.Combine(Path.GetTempPath(), $"erp-settings-{Guid.NewGuid():N}.json");
        var service = new AppSettingsService(jsonPath);
        var settings = new AppSettings
        {
            CompanyName = "Acme Aerospace",
            Theme = "Dark",
            EnableNotifications = false,
            AutoRefreshSeconds = 120,
            DefaultArchivePath = "archive/custom"
        };

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal(settings.CompanyName, loaded.CompanyName);
        Assert.Equal(settings.Theme, loaded.Theme);
        Assert.Equal(settings.AutoRefreshSeconds, loaded.AutoRefreshSeconds);

        File.Delete(jsonPath);
    }

    [Fact]
    public void AuthorizationService_HasPermission_ReturnsExpectedResult()
    {
        var user = new UserAccount
        {
            Username = "qa",
            Roles =
            [
                new RoleDefinition
                {
                    Name = RoleCatalog.Inspection,
                    Permissions = [UserPermission.ViewInspection, UserPermission.EditInspection]
                }
            ]
        };

        Assert.True(AuthorizationService.HasPermission(user, UserPermission.EditInspection));
        Assert.False(AuthorizationService.HasPermission(user, UserPermission.ManageUsers));
    }

    [Fact]
    public void AuthorizationService_CanAccessSettings_AlwaysTrue()
    {
        var user = new UserAccount
        {
            Username = "viewer",
            Roles =
            [
                new RoleDefinition
                {
                    Name = RoleCatalog.ProductionEmployee,
                    Permissions = [UserPermission.ViewProduction]
                }
            ]
        };

        Assert.True(AuthorizationService.CanAccessSection(user, "Settings"));
        Assert.False(AuthorizationService.CanEditSection(user, "Settings"));
    }


    [Fact]
    public void RoleCatalog_NormalizeRoleName_MapsToFixedCatalog()
    {
        Assert.Equal(RoleCatalog.Purchaser, RoleCatalog.NormalizeRoleName("Purchasing"));
        Assert.Equal(RoleCatalog.Administrator, RoleCatalog.NormalizeRoleName("Admin"));
        Assert.Equal(RoleCatalog.Shipping, RoleCatalog.NormalizeRoleName("Shipping/Receiving"));
        Assert.Null(RoleCatalog.NormalizeRoleName("Unknown"));
    }

}