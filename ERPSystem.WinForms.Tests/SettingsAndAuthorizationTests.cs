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
                    Name = "Inspector",
                    Permissions = [UserPermission.ViewInspection, UserPermission.ManageInspection]
                }
            ]
        };

        Assert.True(AuthorizationService.HasPermission(user, UserPermission.ManageInspection));
        Assert.False(AuthorizationService.HasPermission(user, UserPermission.ManageUsers));
    }
}
