using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Services;

public static class AuthorizationService
{
    public static bool HasPermission(UserAccount user, UserPermission permission)
    {
        return user.Roles.SelectMany(r => r.Permissions).Distinct().Contains(permission);
    }
}
