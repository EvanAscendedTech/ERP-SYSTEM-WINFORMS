using System.Diagnostics;

namespace ERPSystem.WinForms.Services;

public interface IModelToGlbConverter
{
    Task<(bool ok, byte[] glb, string stdout, string stderr, int exitCode)> ConvertAsync(byte[] modelBytes, string fileName, CancellationToken ct);
}

public sealed class ModelToGlbConverter : IModelToGlbConverter
{
    private const int ConversionTimeoutSeconds = 90;
    private static readonly string[] ConverterRelativePaths =
    [
        Path.Combine("Tools", "model2glb", "model2glb.exe"),
        Path.Combine("Tools", "model-preview", "model2glb.exe"),
        Path.Combine("Tools", "x64", "model2glb.exe")
    ];

    private readonly string _converterExecutablePath;

    public ModelToGlbConverter(string? converterExecutablePath = null)
    {
        _converterExecutablePath = string.IsNullOrWhiteSpace(converterExecutablePath)
            ? ResolveConverterPath()
            : converterExecutablePath;
    }

    public static string ResolveConverterPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("ERP_MODEL2GLB_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        foreach (var candidate in GetConverterPathCandidates(AppContext.BaseDirectory))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, ConverterRelativePaths[0]);
    }

    internal static IReadOnlyList<string> GetConverterPathCandidates(string baseDirectory)
    {
        var paths = new List<string>();
        void Add(string path)
        {
            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(path);
            }
        }

        AddRelativeCandidates(baseDirectory, Add);
        var current = new DirectoryInfo(baseDirectory);
        for (var i = 0; i < 6 && current is not null; i++)
        {
            AddRelativeCandidates(current.FullName, Add);
            AddRelativeCandidates(Path.Combine(current.FullName, "ERPSystem.WinForms"), Add);
            current = current.Parent;
        }

        return paths;
    }

    private static void AddRelativeCandidates(string rootPath, Action<string> add)
    {
        foreach (var relativePath in ConverterRelativePaths)
        {
            add(Path.Combine(rootPath, relativePath));
        }
    }

    public async Task<(bool ok, byte[] glb, string stdout, string stderr, int exitCode)> ConvertAsync(byte[] modelBytes, string fileName, CancellationToken ct)
    {
        if (modelBytes is null || modelBytes.Length == 0)
        {
            return (false, Array.Empty<byte>(), string.Empty, "MODEL payload is empty.", -1);
        }

        if (!File.Exists(_converterExecutablePath))
        {
            return (false, Array.Empty<byte>(), string.Empty, $"MODEL_CONVERTER_MISSING: {_converterExecutablePath}", -1);
        }

        var safeName = string.IsNullOrWhiteSpace(fileName) ? "input.model" : Path.GetFileName(fileName);
        var ext = Path.GetExtension(safeName);
        if (string.IsNullOrWhiteSpace(ext))
        {
            safeName = safeName + ".model";
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "erp_model_preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, safeName);
        var outputPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.glb");

        try
        {
            await File.WriteAllBytesAsync(inputPath, modelBytes, ct);

            var psi = new ProcessStartInfo
            {
                FileName = _converterExecutablePath,
                Arguments = $"\"{inputPath}\" \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(ConversionTimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(process);
                return (false, Array.Empty<byte>(), await stdoutTask, $"MODEL_CONVERT_FAILED: timeout after {ConversionTimeoutSeconds}s. {await stderrTask}", -1);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var exitCode = process.ExitCode;

            if (exitCode != 0 || !File.Exists(outputPath))
            {
                return (false, Array.Empty<byte>(), stdout, $"MODEL_CONVERT_FAILED: {stderr}", exitCode);
            }

            var glbBytes = await File.ReadAllBytesAsync(outputPath, ct);
            return glbBytes.Length > 0
                ? (true, glbBytes, stdout, stderr, exitCode)
                : (false, Array.Empty<byte>(), stdout, "MODEL_CONVERT_FAILED: converter produced empty output.", exitCode);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
            // ignored
        }
    }
}
