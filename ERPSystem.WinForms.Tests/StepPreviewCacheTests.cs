using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Tests;

public class StepPreviewCacheTests
{
    [Fact]
    public async Task SaveAndLoad_UsesStepHashFileCache()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "erp-winforms-step-cache-tests", Guid.NewGuid().ToString("N"));
        var cache = new StepPreviewCache(cacheRoot);
        var hash = "ABC123";
        var payload = new byte[] { 1, 2, 3, 4 };

        await cache.SaveGlbAsync(hash, payload, "sample.step", CancellationToken.None);
        var loaded = await cache.TryGetGlbAsync(hash, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(payload, loaded);
    }
}
