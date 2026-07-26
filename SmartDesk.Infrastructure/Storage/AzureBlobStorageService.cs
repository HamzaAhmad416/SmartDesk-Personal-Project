using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using SmartDesk.Application.Interfaces;

namespace SmartDesk.Infrastructure.Storage;

/// <summary>
/// WHY AZURE BLOB STORAGE FOR ATTACHMENTS?
///
/// Binary files (screenshots, logs, PDFs) should never go through your API.
/// If you stored files in Cosmos or SQL, every attachment read would hit your app server.
/// 
/// The correct cloud pattern:
/// 1. User picks a file in Blazor
/// 2. Blazor calls API → API uploads to Blob Storage → returns the blob name
/// 3. When displaying, API generates a SAS URL → Blazor links directly to Azure CDN
/// 4. The file download goes Azure → User, bypassing your app entirely
///
/// SAS URL (Shared Access Signature):
/// A time-limited, signed URL that gives temporary read access to a private blob.
/// Expires after the given TimeSpan — security best practice.
/// No permanent public access — blobs are private by default.
/// </summary>
public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(
        BlobServiceClient blobServiceClient,
        string containerName,
        ILogger<AzureBlobStorageService> logger)
    {
        _container = blobServiceClient.GetBlobContainerClient(containerName);
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        // Organise blobs by year/month to avoid hitting container limits
        // e.g. "2024/07/a3f1c2d4-e5b6-screenshot.png"
        var blobName = $"{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid()}_{fileName}";
        var blobClient = _container.GetBlobClient(blobName);

        await blobClient.UploadAsync(fileStream, new BlobHttpHeaders
        {
            ContentType = contentType
        }, cancellationToken: ct);

        _logger.LogInformation("Uploaded blob '{BlobName}' ({ContentType})", blobName, contentType);

        // Return just the blob NAME — not the URL
        // URL is generated on-demand via GetSasUrlAsync (expires, more secure)
        return blobName;
    }

    public async Task DeleteAsync(string blobName, CancellationToken ct = default)
    {
        var blobClient = _container.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
        _logger.LogInformation("Deleted blob '{BlobName}'", blobName);
    }

    public Task<string> GetSasUrlAsync(string blobName, TimeSpan expiry, CancellationToken ct = default)
    {
        var blobClient = _container.GetBlobClient(blobName);

        // Read-only, expires after `expiry` duration
        var sasUri = blobClient.GenerateSasUri(
            BlobSasPermissions.Read,
            DateTimeOffset.UtcNow.Add(expiry));

        return Task.FromResult(sasUri.ToString());
    }

    /// <summary>
    /// Called once at startup to ensure the container exists.
    /// PublicAccessType.None = blobs are private (SAS required to read).
    /// </summary>
    public async Task EnsureContainerExistsAsync(CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        _logger.LogInformation("Blob container ready: '{ContainerName}'", _container.Name);
    }
}
