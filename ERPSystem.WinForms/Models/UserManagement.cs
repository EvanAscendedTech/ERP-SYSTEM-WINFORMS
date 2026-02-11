namespace ERPSystem.WinForms.Models;

public enum UserPermission
{
    ViewProduction = 0,
    ManageProduction = 1,
    ViewInspection = 2,
    ManageInspection = 3,
    ViewArchive = 4,
    ManageUsers = 5,
    ManageSettings = 6
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
    public List<RoleDefinition> Roles { get; set; } = new();
}

public class EmployeeRecord
{
    public int Id { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int? LinkedUserId { get; set; }
}
