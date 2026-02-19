using ERPSystem.WinForms.Data;

namespace ERPSystem.WinForms.Services;

public interface IStepPreviewCache
{
    Task<byte[]?> TryGetGlbAsync(string stepHash, CancellationToken cancellationToken);
    Task SaveGlbAsync(string stepHash, byte[] glbBytes, string sourceFileName, CancellationToken cancellationToken);
}

public sealed class StepPreviewCache : IStepPreviewCache
{
    private readonly QuoteRepository _quoteRepository;

    public StepPreviewCache(QuoteRepository quoteRepository)
    {
        _quoteRepository = quoteRepository;
    }

    public Task<byte[]?> TryGetGlbAsync(string stepHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _quoteRepository.TryGetGlbCacheByHashAsync(stepHash);
    }

    public Task SaveGlbAsync(string stepHash, byte[] glbBytes, string sourceFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _quoteRepository.UpsertGlbCacheByHashAsync(stepHash, glbBytes, sourceFileName);
    }
}
