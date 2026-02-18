namespace ERPSystem.WinForms.Models;

public enum UserPermission
{
    ViewPurchasing = 0,
    EditPurchasing = 1,
    ViewProduction = 2,
    EditProduction = 3,
    ViewInspection = 4,
    EditInspection = 5,
    ViewShipping = 6,
    EditShipping = 7,
    ManageUsers = 8,
    ManageSettings = 9,
    ViewPricing = 10
}

public class RoleDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<UserPermission> Permissions { get; set; } = new();
}

public class UserAccount
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string IconPath { get; set; } = string.Empty;
    public byte[]? IconBlob { get; set; }
    public bool IsOnline { get; set; }
    public DateTime? LastActivityUtc { get; set; }
    public List<RoleDefinition> Roles { get; set; } = new();
}

public class AccountRequest
{
    public int Id { get; set; }
    public string RequestedUsername { get; set; } = string.Empty;
    public string RequestNote { get; set; } = string.Empty;
    public bool TermsAccepted { get; set; }
    public DateTime RequestedUtc { get; set; } = DateTime.UtcNow;
}

public class EmployeeRecord
{
    public int Id { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int? LinkedUserId { get; set; }
}

public class PurchasingLayoutSetting
{
    public int UserId { get; set; }
    public double LeftPanelProportion { get; set; }
    public double RightTopPanelProportion { get; set; }
    public double RightBottomPanelProportion { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}

public static class RoleCatalog
{
    public const string Purchaser = "Purchaser";
    public const string ProductionEmployee = "Production Employee";
    public const string ProductionManager = "Production Manager";
    public const string Inspection = "Inspection";
    public const string Shipping = "Shipping";
    public const string Administrator = "Administrator";

    public static readonly string[] AccountLevels =
    [
        Purchaser,
        ProductionEmployee,
        ProductionManager,
        Inspection,
        Shipping,
        Administrator
    ];

    public static string? NormalizeRoleName(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return null;
        }

        var normalized = roleName.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "admin" => Administrator,
            "administrator" => Administrator,
            "purchasing" => Purchaser,
            "purchaser" => Purchaser,
            "production" => ProductionEmployee,
            "production employee" => ProductionEmployee,
            "production manager" => ProductionManager,
            "inspector" => Inspection,
            "inspection" => Inspection,
            "shipping" => Shipping,
            "shipping/receiving" => Shipping,
            _ => AccountLevels.FirstOrDefault(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        };
    }

    public static bool IsAllowedRole(string? roleName)
        => NormalizeRoleName(roleName) is not null;
}
