using System.Collections.Concurrent;
using System.Text;

namespace ERPSystem.WinForms.Services;

public interface IStepPreviewService
{
    Task<byte[]> GetOrCreateGlbAsync(byte[] stepBytes, string fileName, CancellationToken ct);
    Task<StepPreviewResult> GetOrCreateGlbDetailedAsync(byte[] stepBytes, string fileName, CancellationToken ct);
    bool IsConverterUnavailable { get; }
    void ResetConverterAvailability();
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
    private static int _converterUnavailable;
    private readonly IStepPreviewCache _cache;
    private readonly IStepToGlbConverter _converter;
    private readonly ConcurrentDictionary<string, Lazy<Task<StepPreviewResult>>> _inflight = new();

    public StepPreviewService(IStepPreviewCache cache, IStepToGlbConverter converter)
    {
        _cache = cache;
        _converter = converter;
    }

    public StepPreviewService()
        : this(new StepPreviewCache(), new StepToGlbConverter())
    {
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
        if (IsConverterUnavailable)
        {
            var hash = StepToGlbConverter.ComputeStepHash(stepBytes);
            return Task.FromResult(new StepPreviewResult(
                false,
                Array.Empty<byte>(),
                hash,
                false,
                "STEP_CONVERTER_UNAVAILABLE",
                "STEP converter is unavailable for this session. Click Retry after fix once converter binaries are restored.",
                -1,
                string.Empty,
                string.Empty,
                "converter-unavailable"));
        }

        var hash = StepToGlbConverter.ComputeStepHash(stepBytes);
        var singleFlight = _inflight.GetOrAdd(hash, _ => new Lazy<Task<StepPreviewResult>>(() => ConvertInternalAsync(stepBytes, fileName, hash, ct)));
        return AwaitAndCleanupAsync(hash, singleFlight);
    }

    public bool IsConverterUnavailable => Volatile.Read(ref _converterUnavailable) == 1;

    public void ResetConverterAvailability()
    {
        Interlocked.Exchange(ref _converterUnavailable, 0);
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

        var cached = await _cache.TryGetGlbAsync(hash, ct);
        if (cached is not null && cached.Length > 0)
        {
            return new(true, cached, hash, true, string.Empty, "GLB cache hit.", 0, string.Empty, string.Empty, validation.details);
        }

        var (ok, glb, stdout, stderr, exitCode) = await _converter.ConvertAsync(stepBytes, fileName, ct);
        if (!ok)
        {
            var errorCode = stderr.Contains("STEP_CONVERTER_MISSING", StringComparison.OrdinalIgnoreCase)
                ? "STEP_CONVERTER_MISSING"
                : "STEP_CONVERT_FAILED";
            if (errorCode == "STEP_CONVERTER_MISSING")
            {
                Interlocked.Exchange(ref _converterUnavailable, 1);
            }

            return new(false, Array.Empty<byte>(), hash, false, errorCode, "STEP conversion failed.", exitCode, stdout, Truncate(stderr, 2048), validation.details);
        }

        await _cache.SaveGlbAsync(hash, glb, fileName, ct);
        return new(true, glb, hash, false, string.Empty, "STEP converted to GLB.", exitCode, Truncate(stdout, 2048), Truncate(stderr, 2048), validation.details);
    }

    private static (bool valid, string message, string details) ValidateStepBytes(byte[] stepBytes)
    {
        if (stepBytes is null || stepBytes.Length == 0)
        {
            return (false, "STEP payload is empty.", "length=0");
        }

        var header = Encoding.ASCII.GetString(stepBytes, 0, Math.Min(stepBytes.Length, 256)).ToUpperInvariant();
        var hasIso = header.Contains("ISO-10303-21");
        return hasIso
            ? (true, "Valid STEP header.", "iso-10303-21")
            : (false, "Invalid STEP header. Missing ISO-10303-21 marker.", $"prefix={header.Replace("\n", " ")}");
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars] + "...";
    }
}
