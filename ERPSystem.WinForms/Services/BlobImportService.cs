using System.Security.Cryptography;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Services;

public enum BlobUploadStatus
{
    Queued,
    Uploading,
    Done,
    Failed
}

public sealed class BlobUploadProgressEventArgs : EventArgs
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required BlobUploadStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class BlobUploadResult
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public QuoteBlobAttachment? Attachment { get; init; }
}

public sealed class BlobImportService
{
    private readonly Func<int, int, string, QuoteBlobType, string, string, long, byte[], string, DateTime, byte[], Task<QuoteBlobAttachment>> _insertBlobAsync;
    private readonly SemaphoreSlim _concurrencyLimiter = new(2, 2);

    public event EventHandler<BlobUploadProgressEventArgs>? UploadProgressChanged;

    public BlobImportService(Func<int, int, string, QuoteBlobType, string, string, long, byte[], string, DateTime, byte[], Task<QuoteBlobAttachment>> insertBlobAsync)
    {
        _insertBlobAsync = insertBlobAsync;
    }

    public async Task<IReadOnlyList<BlobUploadResult>> EnqueueFilesAsync(
        int quoteId,
        int lineItemId,
        string lifecycleId,
        QuoteBlobType blobType,
        IEnumerable<string> filePaths,
        string uploadedBy,
        CancellationToken cancellationToken = default)
    {
        var normalizedPaths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var filePath in normalizedPaths)
        {
            OnUploadProgressChanged(filePath, Path.GetFileName(filePath), BlobUploadStatus.Queued);
        }

        var tasks = normalizedPaths.Select(path => UploadSingleAsync(
            quoteId,
            lineItemId,
            lifecycleId,
            blobType,
            path,
            uploadedBy,
            cancellationToken));

        return await Task.WhenAll(tasks);
    }

    private async Task<BlobUploadResult> UploadSingleAsync(
        int quoteId,
        int lineItemId,
        string lifecycleId,
        QuoteBlobType blobType,
        string filePath,
        string uploadedBy,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(filePath);

        if (!File.Exists(filePath))
        {
            const string missingFileError = "File does not exist.";
            OnUploadProgressChanged(filePath, fileName, BlobUploadStatus.Failed, missingFileError);
            return new BlobUploadResult
            {
                FilePath = filePath,
                FileName = fileName,
                IsSuccess = false,
                ErrorMessage = missingFileError
            };
        }

        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            OnUploadProgressChanged(filePath, fileName, BlobUploadStatus.Uploading);

            var uploadedUtc = DateTime.UtcNow;
            var extension = Path.GetExtension(filePath);

            byte[] fileBytes;
            byte[] hash;
            long size;

            await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                size = stream.Length;
                fileBytes = new byte[size];
                var offset = 0;
                while (offset < fileBytes.Length)
                {
                    var read = await stream.ReadAsync(fileBytes.AsMemory(offset, fileBytes.Length - offset), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    offset += read;
                }
            }

            await using (var hashStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                using var sha256 = SHA256.Create();
                hash = await sha256.ComputeHashAsync(hashStream, cancellationToken);
            }

            var attachment = await _insertBlobAsync(
                quoteId,
                lineItemId,
                lifecycleId,
                blobType,
                fileName,
                extension,
                size,
                hash,
                uploadedBy,
                uploadedUtc,
                fileBytes);

            OnUploadProgressChanged(filePath, fileName, BlobUploadStatus.Done);
            return new BlobUploadResult
            {
                FilePath = filePath,
                FileName = fileName,
                IsSuccess = true,
                Attachment = attachment
            };
        }
        catch (Exception ex)
        {
            OnUploadProgressChanged(filePath, fileName, BlobUploadStatus.Failed, ex.Message);
            return new BlobUploadResult
            {
                FilePath = filePath,
                FileName = fileName,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    private void OnUploadProgressChanged(string filePath, string fileName, BlobUploadStatus status, string? errorMessage = null)
    {
        UploadProgressChanged?.Invoke(this, new BlobUploadProgressEventArgs
        {
            FilePath = filePath,
            FileName = fileName,
            Status = status,
            ErrorMessage = errorMessage
        });
    }
}
