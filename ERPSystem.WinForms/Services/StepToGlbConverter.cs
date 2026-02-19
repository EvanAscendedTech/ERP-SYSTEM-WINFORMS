using System.Diagnostics;
using System.Security.Cryptography;

namespace ERPSystem.WinForms.Services;

public interface IStepToGlbConverter
{
    Task<(bool ok, byte[] glb, string stdout, string stderr, int exitCode)> ConvertAsync(byte[] stepBytes, string fileName, CancellationToken ct);
}

public sealed class StepToGlbConverter : IStepToGlbConverter
{
    private const int ConversionTimeoutSeconds = 60;
    private readonly string _converterExecutablePath;
    private readonly bool _keepTempOnFailure;

    public StepToGlbConverter(string? converterExecutablePath = null, bool keepTempOnFailure = false)
    {
        _converterExecutablePath = string.IsNullOrWhiteSpace(converterExecutablePath)
            ? ResolveConverterPath()
            : converterExecutablePath;
        _keepTempOnFailure = keepTempOnFailure;
    }

    public static string ResolveConverterPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("ERP_STEP2GLB_PATH");
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

        // Return the default output location so callers get a stable, actionable error message.
        return Path.Combine(AppContext.BaseDirectory, "Tools", "step2glb", "step2glb.exe");
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

        Add(Path.Combine(baseDirectory, "Tools", "step2glb", "step2glb.exe"));

        var current = new DirectoryInfo(baseDirectory);
        for (var i = 0; i < 6 && current is not null; i++)
        {
            Add(Path.Combine(current.FullName, "Tools", "step2glb", "step2glb.exe"));
            Add(Path.Combine(current.FullName, "ERPSystem.WinForms", "Tools", "step2glb", "step2glb.exe"));
            current = current.Parent;
        }

        return paths;
    }

    public static string ComputeStepHash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public async Task<(bool ok, byte[] glb, string stdout, string stderr, int exitCode)> ConvertAsync(byte[] stepBytes, string fileName, CancellationToken ct)
    {
        if (stepBytes is null || stepBytes.Length == 0)
        {
            return (false, Array.Empty<byte>(), string.Empty, "STEP payload is empty.", -1);
        }

        if (!File.Exists(_converterExecutablePath))
        {
            return (false, Array.Empty<byte>(), string.Empty, $"STEP_CONVERTER_MISSING: {_converterExecutablePath}", -1);
        }

        var safeName = string.IsNullOrWhiteSpace(fileName) ? "input.step" : Path.GetFileName(fileName);
        var ext = Path.GetExtension(safeName);
        if (!ext.Equals(".step", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".stp", StringComparison.OrdinalIgnoreCase))
        {
            safeName = Path.ChangeExtension(safeName, ".step");
        }

        var hash = ComputeStepHash(stepBytes);
        var tempRoot = Path.Combine(Path.GetTempPath(), "erp_step_preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, safeName);
        var outputPath = Path.Combine(tempRoot, $"{hash}.glb");

        try
        {
            await File.WriteAllBytesAsync(inputPath, stepBytes, ct);

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
                var partialStdout = await stdoutTask;
                var partialStderr = await stderrTask;
                return (false, Array.Empty<byte>(), partialStdout, $"STEP_CONVERT_FAILED: timeout after {ConversionTimeoutSeconds}s. {partialStderr}", -1);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var exitCode = process.ExitCode;

            if (exitCode != 0 || !File.Exists(outputPath))
            {
                return (false, Array.Empty<byte>(), stdout, $"STEP_CONVERT_FAILED: {stderr}", exitCode);
            }

            var glbBytes = await File.ReadAllBytesAsync(outputPath, ct);
            if (glbBytes.Length == 0)
            {
                return (false, Array.Empty<byte>(), stdout, "STEP_CONVERT_FAILED: converter produced empty output.", exitCode);
            }

            return (true, glbBytes, stdout, stderr, exitCode);
        }
        finally
        {
            if (!_keepTempOnFailure)
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
    }

    public static async Task<int> RunConversionProbeAsync(string stepPath, CancellationToken ct)
    {
        var converter = new StepToGlbConverter();
        var bytes = await File.ReadAllBytesAsync(stepPath, ct);
        var (ok, glb, stdout, stderr, exitCode) = await converter.ConvertAsync(bytes, Path.GetFileName(stepPath), ct);
        Console.WriteLine($"ok={ok}; exitCode={exitCode}; glbBytes={glb.Length}");
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            Console.WriteLine($"stdout: {Truncate(stdout, 512)}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            Console.WriteLine($"stderr: {Truncate(stderr, 512)}");
        }

        return ok && glb.Length > 0 ? 0 : 1;
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

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars] + "...";
    }
}
