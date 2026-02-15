using System.Security.Cryptography;
using System.Text;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Services;

public static class AuthorizationService
{
    public static bool HasPermission(UserAccount user, UserPermission permission)
    {
        return user.Roles.SelectMany(r => r.Permissions).Distinct().Contains(permission);
    }

    public static bool CanAccessSection(UserAccount user, string sectionKey)
    {
        return sectionKey switch
        {
            "Dashboard" => true,
            "Quotes" => true,
            "CRM" => true,
            "Purchasing" => HasPermission(user, UserPermission.ViewPurchasing),
            "Production" => HasPermission(user, UserPermission.ViewProduction),
            "Inspection" => HasPermission(user, UserPermission.ViewInspection),
            "Shipping" => HasPermission(user, UserPermission.ViewShipping),
            "Settings" => HasPermission(user, UserPermission.ManageSettings) || HasPermission(user, UserPermission.ManageUsers),
            _ => false
        };
    }

    public static bool CanEditSection(UserAccount user, string sectionKey)
    {
        return sectionKey switch
        {
            "Purchasing" => HasPermission(user, UserPermission.EditPurchasing),
            "Production" => HasPermission(user, UserPermission.EditProduction),
            "Inspection" => HasPermission(user, UserPermission.EditInspection),
            "Shipping" => HasPermission(user, UserPermission.EditShipping),
            "Settings" => HasPermission(user, UserPermission.ManageSettings),
            _ => false
        };
    }

    public static RoleDefinition BuildRole(string roleName)
    {
        var normalized = roleName.Trim();
        var permissions = normalized switch
        {
            var r when r.Equals(RoleCatalog.Admin, StringComparison.OrdinalIgnoreCase)
                => Enum.GetValues<UserPermission>().ToList(),
            var r when r.Equals(RoleCatalog.Purchasing, StringComparison.OrdinalIgnoreCase)
                => new List<UserPermission> { UserPermission.ViewPurchasing, UserPermission.EditPurchasing, UserPermission.ViewPricing },
            var r when r.Equals(RoleCatalog.Production, StringComparison.OrdinalIgnoreCase)
                => new List<UserPermission> { UserPermission.ViewProduction, UserPermission.EditProduction },
            var r when r.Equals(RoleCatalog.Inspector, StringComparison.OrdinalIgnoreCase)
                => new List<UserPermission> { UserPermission.ViewInspection, UserPermission.EditInspection },
            var r when r.Equals(RoleCatalog.ShippingReceiving, StringComparison.OrdinalIgnoreCase)
                => new List<UserPermission> { UserPermission.ViewShipping, UserPermission.EditShipping },
            _ => new List<UserPermission>()
        };

        return new RoleDefinition
        {
            Name = normalized,
            Permissions = permissions.Distinct().ToList()
        };
    }

    public static string HashPassword(string plainText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainText));
        return Convert.ToHexString(bytes);
    }

    public static bool VerifyPassword(UserAccount user, string plainTextPassword)
    {
        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return false;
        }

        var expected = HashPassword(plainTextPassword);
        return string.Equals(user.PasswordHash, expected, StringComparison.OrdinalIgnoreCase);
    }
}
