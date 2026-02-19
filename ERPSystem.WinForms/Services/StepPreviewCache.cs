namespace ERPSystem.WinForms.Services;

public interface IStepPreviewCache
{
    Task<byte[]?> TryGetGlbAsync(string stepHash, CancellationToken cancellationToken);
    Task SaveGlbAsync(string stepHash, byte[] glbBytes, string sourceFileName, CancellationToken cancellationToken);
}

public sealed class StepPreviewCache : IStepPreviewCache
{
    private readonly string _cacheDirectory;

    public StepPreviewCache(string? cacheDirectory = null)
    {
        _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ERPSystem", "StepGlbCache")
            : cacheDirectory;

        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<byte[]?> TryGetGlbAsync(string stepHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = BuildCachePath(stepHash);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public async Task SaveGlbAsync(string stepHash, byte[] glbBytes, string sourceFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_cacheDirectory);

        var path = BuildCachePath(stepHash);
        await File.WriteAllBytesAsync(path, glbBytes, cancellationToken);
    }

    private string BuildCachePath(string stepHash)
    {
        var safeHash = string.Concat(stepHash.Where(char.IsLetterOrDigit));
        if (safeHash.Length == 0)
        {
            safeHash = "step";
        }

        return Path.Combine(_cacheDirectory, $"{safeHash}.glb");
    }
}
