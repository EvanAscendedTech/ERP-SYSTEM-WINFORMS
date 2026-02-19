using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ERPSystem.WinForms.Data;

namespace ERPSystem.WinForms.Services;

public interface IStepPreviewService
{
    Task<byte[]> GetOrCreateGlbAsync(byte[] stepBytes, string fileName, CancellationToken ct);
    Task<StepPreviewResult> GetOrCreateGlbDetailedAsync(byte[] stepBytes, string fileName, CancellationToken ct);
}

public sealed record StepPreviewResult(
    bool Success,
    byte[] GlbBytes,
    string StepHash,
    bool CacheHit,
    string ErrorCode,
    string Message,
    int ExitCode,
    string StdOut,
    string StdErr,
    string ValidationDetails);

public sealed class StepPreviewService : IStepPreviewService
{
    private readonly QuoteRepository _quoteRepository;
    private readonly string _converterExecutablePath;
    private readonly ConcurrentDictionary<string, Lazy<Task<StepPreviewResult>>> _inflight = new();

    public StepPreviewService(QuoteRepository quoteRepository, string? converterExecutablePath = null)
    {
        _quoteRepository = quoteRepository;
        _converterExecutablePath = string.IsNullOrWhiteSpace(converterExecutablePath)
            ? ResolveDefaultConverterPath()
            : converterExecutablePath;
    }

    public async Task<byte[]> GetOrCreateGlbAsync(byte[] stepBytes, string fileName, CancellationToken ct)
    {
        var result = await GetOrCreateGlbDetailedAsync(stepBytes, fileName, ct);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }

        return result.GlbBytes;
    }

    public Task<StepPreviewResult> GetOrCreateGlbDetailedAsync(byte[] stepBytes, string fileName, CancellationToken ct)
    {
        var hash = ComputeStepHash(stepBytes);
        var singleFlight = _inflight.GetOrAdd(hash, _ => new Lazy<Task<StepPreviewResult>>(() => ConvertInternalAsync(stepBytes, fileName, hash, ct)));
        return AwaitAndCleanupAsync(hash, singleFlight);
    }

    private async Task<StepPreviewResult> AwaitAndCleanupAsync(string hash, Lazy<Task<StepPreviewResult>> task)
    {
        try
        {
            return await task.Value;
        }
        finally
        {
            _inflight.TryRemove(hash, out _);
        }
    }

    private async Task<StepPreviewResult> ConvertInternalAsync(byte[] stepBytes, string fileName, string hash, CancellationToken ct)
    {
        var validation = ValidateStepBytes(stepBytes);
        if (!validation.valid)
        {
            return new(false, Array.Empty<byte>(), hash, false, "STEP_INVALID_HEADER", validation.message, -1, string.Empty, string.Empty, validation.details);
        }

        var cached = await _quoteRepository.TryGetGlbCacheByHashAsync(hash);
        if (cached is not null)
        {
            return new(true, cached, hash, true, string.Empty, "GLB cache hit.", 0, string.Empty, string.Empty, validation.details);
        }

        if (!File.Exists(_converterExecutablePath))
        {
            return new(false, Array.Empty<byte>(), hash, false, "STEP_CONVERTER_MISSING", $"Converter executable not found: {_converterExecutablePath}", -1, string.Empty, string.Empty, validation.details);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "erp-step-preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var stepPath = Path.Combine(tempRoot, "source.step");
        var glbPath = Path.Combine(tempRoot, "model.glb");

        try
        {
            await File.WriteAllBytesAsync(stepPath, stepBytes, ct);

            var result = await RunConverterAsync(stepPath, glbPath, ct);
            if (!result.Success)
            {
                return result with { StepHash = hash, ValidationDetails = validation.details };
            }

            var glbBytes = await File.ReadAllBytesAsync(glbPath, ct);
            if (glbBytes.Length == 0)
            {
                return new(false, Array.Empty<byte>(), hash, false, "STEP_CONVERT_FAILED", "Converter produced an empty GLB file.", result.ExitCode, result.StdOut, result.StdErr, validation.details);
            }

            await _quoteRepository.UpsertGlbCacheByHashAsync(hash, glbBytes, fileName);
            return new(true, glbBytes, hash, false, string.Empty, "STEP converted to GLB.", result.ExitCode, result.StdOut, result.StdErr, validation.details);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private async Task<StepPreviewResult> RunConverterAsync(string stepPath, string glbPath, CancellationToken ct)
    {
        var attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _converterExecutablePath,
                    Arguments = $"\"{stepPath}\" \"{glbPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(ct);
                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0 || !File.Exists(glbPath))
                {
                    return new(false, Array.Empty<byte>(), string.Empty, false, "STEP_CONVERT_FAILED", "STEP to GLB conversion failed.", process.ExitCode, stdout, stderr, string.Empty);
                }

                return new(true, Array.Empty<byte>(), string.Empty, false, string.Empty, "Conversion completed.", process.ExitCode, stdout, stderr, string.Empty);
            }
            catch (IOException ex) when (attempts == 1 && IsFileLocked(ex))
            {
                await Task.Delay(150, ct);
            }
            catch (Exception ex)
            {
                return new(false, Array.Empty<byte>(), string.Empty, false, "STEP_CONVERT_FAILED", ex.Message, -1, string.Empty, ex.ToString(), string.Empty);
            }
        }
    }

    private static (bool valid, string message, string details) ValidateStepBytes(byte[] stepBytes)
    {
        if (stepBytes.Length < 32)
        {
            return (false, "STEP payload is missing or too small.", "length<32");
        }

        var header = Encoding.ASCII.GetString(stepBytes, 0, Math.Min(stepBytes.Length, 256)).ToUpperInvariant();
        var hasIso = header.Contains("ISO-10303-21");
        var hasHeader = header.Contains("HEADER;");
        return hasIso && hasHeader
            ? (true, "Valid STEP header.", "iso-10303-21+header")
            : (false, "Invalid STEP header. Missing ISO-10303-21/HEADER markers.", $"prefix={header.Replace("\n", " ")}");
    }

    private static bool IsFileLocked(IOException ex) =>
        ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("file is in use", StringComparison.OrdinalIgnoreCase);

    private static string ComputeStepHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string ResolveDefaultConverterPath()
    {
        var env = Environment.GetEnvironmentVariable("STEP_TO_GLB_CONVERTER_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return Path.Combine(AppContext.BaseDirectory, "Tools", "x64", "step-to-glb-converter.exe");
    }
}
