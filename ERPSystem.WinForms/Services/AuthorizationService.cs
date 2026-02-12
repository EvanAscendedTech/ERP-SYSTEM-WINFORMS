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
