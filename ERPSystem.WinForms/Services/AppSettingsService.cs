using System.Text.Json;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Services;

public class AppSettingsService
{
    private readonly string _filePath;

    public AppSettingsService(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_filePath);
        var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream);
        return loaded ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? AppContext.BaseDirectory);
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true });
    }
}
