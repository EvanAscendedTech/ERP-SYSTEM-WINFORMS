using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using ERPSystem.WinForms.Data;

namespace ERPSystem.WinForms.Services;

public interface IStepToGlbConverter
{
    Task<StepToGlbConversionResult> ConvertAsync(byte[] stepBytes, StepToGlbRequest request, CancellationToken cancellationToken);
}

public sealed record StepToGlbRequest(int LineItemId, string FileName, string SourcePath);

public sealed record StepToGlbConversionResult(bool Success, byte[] GlbBytes, string ErrorCode, string Message, string StdOut, string StdErr, int ExitCode, string StepHash, bool CacheHit);

public sealed class StepToGlbConverter : IStepToGlbConverter
{
    private readonly QuoteRepository _quoteRepository;
    private readonly string _converterExecutablePath;
    private readonly ConcurrentDictionary<string, Lazy<Task<StepToGlbConversionResult>>> _inflight = new();

    public StepToGlbConverter(QuoteRepository quoteRepository, string? converterExecutablePath = null)
    {
        _quoteRepository = quoteRepository;
        _converterExecutablePath = string.IsNullOrWhiteSpace(converterExecutablePath)
            ? ResolveDefaultConverterPath()
            : converterExecutablePath;
    }

    public Task<StepToGlbConversionResult> ConvertAsync(byte[] stepBytes, StepToGlbRequest request, CancellationToken cancellationToken)
    {
        var hash = ComputeStepHash(stepBytes);
        var singleFlight = _inflight.GetOrAdd(hash, _ => new Lazy<Task<StepToGlbConversionResult>>(() => ConvertInternalAsync(stepBytes, hash, request, cancellationToken)));
        return AwaitAndCleanupAsync(hash, singleFlight);
    }

    private async Task<StepToGlbConversionResult> AwaitAndCleanupAsync(string hash, Lazy<Task<StepToGlbConversionResult>> singleFlight)
    {
        try
        {
            return await singleFlight.Value;
        }
        finally
        {
            _inflight.TryRemove(hash, out _);
        }
    }

    private async Task<StepToGlbConversionResult> ConvertInternalAsync(byte[] stepBytes, string hash, StepToGlbRequest request, CancellationToken cancellationToken)
    {
        if (stepBytes.Length < 32)
        {
            return new(false, Array.Empty<byte>(), "STEP_INVALID_BYTES", "STEP payload is empty or too small.", string.Empty, string.Empty, -1, hash, false);
        }

        var cached = await _quoteRepository.TryGetGlbCacheByStepHashAsync(hash, request.LineItemId);
        if (cached is not null)
        {
            return new(true, cached, string.Empty, "GLB cache hit", string.Empty, string.Empty, 0, hash, true);
        }

        if (!File.Exists(_converterExecutablePath))
        {
            return new(false, Array.Empty<byte>(), "STEP_CONVERT_FAILED", $"Converter executable not found: {_converterExecutablePath}", string.Empty, string.Empty, -1, hash, false);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "erp-step-glb", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var stepPath = Path.Combine(tempRoot, "input.step");
        var glbPath = Path.Combine(tempRoot, "output.glb");

        try
        {
            await File.WriteAllBytesAsync(stepPath, stepBytes, cancellationToken);

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
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var exitCode = process.ExitCode;

            if (exitCode != 0 || !File.Exists(glbPath))
            {
                return new(false, Array.Empty<byte>(), "STEP_CONVERT_FAILED", "STEP to GLB conversion failed.", stdout, stderr, exitCode, hash, false);
            }

            var glbBytes = await File.ReadAllBytesAsync(glbPath, cancellationToken);
            if (glbBytes.Length == 0)
            {
                return new(false, Array.Empty<byte>(), "STEP_CONVERT_FAILED", "Converter produced an empty GLB file.", stdout, stderr, exitCode, hash, false);
            }

            await _quoteRepository.UpsertGlbCacheAsync(request.LineItemId, hash, glbBytes, request.FileName, request.SourcePath);
            return new(true, glbBytes, string.Empty, "Converted STEP to GLB.", stdout, stderr, exitCode, hash, false);
        }
        catch (OperationCanceledException)
        {
            return new(false, Array.Empty<byte>(), "STEP_LOAD_CANCELLED", "STEP conversion cancelled.", string.Empty, string.Empty, -1, hash, false);
        }
        catch (Exception ex)
        {
            return new(false, Array.Empty<byte>(), "STEP_CONVERT_FAILED", ex.Message, string.Empty, ex.ToString(), -1, hash, false);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    private static string ComputeStepHash(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string ResolveDefaultConverterPath()
    {
        var envPath = Environment.GetEnvironmentVariable("STEP_TO_GLB_CONVERTER_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return envPath;
        }

        return Path.Combine(AppContext.BaseDirectory, "Tools", "step-to-glb-converter.exe");
    }
}
