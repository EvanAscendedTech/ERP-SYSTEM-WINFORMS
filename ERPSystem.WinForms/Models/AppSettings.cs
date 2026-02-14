namespace ERPSystem.WinForms.Models;

public class AppSettings
{
    public string CompanyName { get; set; } = "Your Company";
    public byte[]? CompanyLogo { get; set; }
    public string Theme { get; set; } = "Light";
    public bool EnableNotifications { get; set; } = true;
    public int AutoRefreshSeconds { get; set; } = 30;
    public string DefaultArchivePath { get; set; } = "archive";
}
