using System.Security.Cryptography;

namespace ERPSystem.WinForms.Services;

public interface IModelPreviewCache
{
    string ComputeHash(byte[] modelBytes);
    Task<byte[]?> TryGetGlbAsync(string modelHash, CancellationToken cancellationToken);
    Task SaveGlbAsync(string modelHash, byte[] glbBytes, CancellationToken cancellationToken);
}

public sealed class ModelPreviewCache : IModelPreviewCache
{
    private readonly string _cacheDirectory;

    public ModelPreviewCache(string? cacheDirectory = null)
    {
        _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ERPSystem", "ModelGlbCache")
            : cacheDirectory;

        Directory.CreateDirectory(_cacheDirectory);
    }

    public string ComputeHash(byte[] modelBytes)
    {
        return Convert.ToHexString(SHA256.HashData(modelBytes));
    }

    public async Task<byte[]?> TryGetGlbAsync(string modelHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(_cacheDirectory, $"{SanitizeHash(modelHash)}.glb");
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public async Task SaveGlbAsync(string modelHash, byte[] glbBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_cacheDirectory);
        var path = Path.Combine(_cacheDirectory, $"{SanitizeHash(modelHash)}.glb");
        await File.WriteAllBytesAsync(path, glbBytes, cancellationToken);
    }

    private static string SanitizeHash(string hash)
    {
        var safe = string.Concat(hash.Where(char.IsLetterOrDigit));
        return string.IsNullOrWhiteSpace(safe) ? "model" : safe;
    }
}
