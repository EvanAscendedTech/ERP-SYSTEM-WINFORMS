using System.Collections.Concurrent;

namespace ERPSystem.WinForms.Services;

public interface IModelPreviewService
{
    Task<ModelPreviewResult> GetOrCreateGlbAsync(byte[] modelBytes, string fileName, CancellationToken ct);
    bool IsConverterUnavailable { get; }
    void ResetConverterAvailability();
}

public sealed record ModelPreviewResult(
    bool Success,
    byte[] GlbBytes,
    string ModelHash,
    bool CacheHit,
    string ErrorCode,
    string Message,
    int ExitCode,
    string StdOut,
    string StdErr);

public sealed class ModelPreviewService : IModelPreviewService
{
    private static int _converterUnavailable;
    private readonly IModelPreviewCache _cache;
    private readonly IModelToGlbConverter _converter;
    private readonly ConcurrentDictionary<string, Lazy<Task<ModelPreviewResult>>> _inflight = new();

    public ModelPreviewService(IModelPreviewCache cache, IModelToGlbConverter converter)
    {
        _cache = cache;
        _converter = converter;
    }

    public ModelPreviewService()
        : this(new ModelPreviewCache(), new ModelToGlbConverter())
    {
    }

    public bool IsConverterUnavailable => Volatile.Read(ref _converterUnavailable) == 1;

    public void ResetConverterAvailability()
    {
        Interlocked.Exchange(ref _converterUnavailable, 0);
    }

    public Task<ModelPreviewResult> GetOrCreateGlbAsync(byte[] modelBytes, string fileName, CancellationToken ct)
    {
        var hash = _cache.ComputeHash(modelBytes);
        if (IsConverterUnavailable)
        {
            return Task.FromResult(new ModelPreviewResult(
                false,
                Array.Empty<byte>(),
                hash,
                false,
                "MODEL_CONVERTER_UNAVAILABLE",
                "Model converter is unavailable for this session.",
                -1,
                string.Empty,
                string.Empty));
        }

        var singleFlight = _inflight.GetOrAdd(hash, _ => new Lazy<Task<ModelPreviewResult>>(() => ConvertInternalAsync(modelBytes, fileName, hash, ct)));
        return AwaitAndCleanupAsync(hash, singleFlight);
    }

    private async Task<ModelPreviewResult> AwaitAndCleanupAsync(string hash, Lazy<Task<ModelPreviewResult>> task)
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

    private async Task<ModelPreviewResult> ConvertInternalAsync(byte[] modelBytes, string fileName, string hash, CancellationToken ct)
    {
        if (modelBytes is null || modelBytes.Length == 0)
        {
            return new(false, Array.Empty<byte>(), hash, false, "MODEL_INVALID_BYTES", "3D model payload is empty.", -1, string.Empty, string.Empty);
        }

        var cached = await _cache.TryGetGlbAsync(hash, ct);
        if (cached is not null && cached.Length > 0)
        {
            return new(true, cached, hash, true, string.Empty, "GLB cache hit.", 0, string.Empty, string.Empty);
        }

        var (ok, glb, stdout, stderr, exitCode) = await _converter.ConvertAsync(modelBytes, fileName, ct);
        if (!ok)
        {
            var errorCode = stderr.Contains("MODEL_CONVERTER_MISSING", StringComparison.OrdinalIgnoreCase)
                ? "MODEL_CONVERTER_MISSING"
                : "MODEL_CONVERT_FAILED";

            if (errorCode == "MODEL_CONVERTER_MISSING")
            {
                Interlocked.Exchange(ref _converterUnavailable, 1);
            }

            return new(false, Array.Empty<byte>(), hash, false, errorCode, "3D model conversion failed.", exitCode, stdout, stderr);
        }

        await _cache.SaveGlbAsync(hash, glb, ct);
        return new(true, glb, hash, false, string.Empty, "3D model converted to GLB.", exitCode, stdout, stderr);
    }
}
