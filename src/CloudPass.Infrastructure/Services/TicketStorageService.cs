using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CloudPass.Application.Interfaces;
using CloudPass.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudPass.Infrastructure.Services;

/// <summary>
/// Centralized Cloud Storage service supporting Azure Blob Storage (production)
/// with automatic graceful fallback to local managed storage for local dev/testing.
/// </summary>
public class CloudTicketStorageService : ITicketStorageService
{
    private readonly StorageOptions _options;
    private readonly ILogger<CloudTicketStorageService> _logger;
    private readonly BlobContainerClient? _containerClient;
    private readonly string _localRoot;

    public string ProviderName => _containerClient is not null ? "AzureBlobStorage" : "LocalCloudStorageEmulator";

    public CloudTicketStorageService(IOptions<StorageOptions> options, ILogger<CloudTicketStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var connStr = _options.AzureBlobConnectionString
            ?? Environment.GetEnvironmentVariable("CLOUDPASS_AZURE_BLOB_CONNECTION");

        if (string.Equals(_options.Provider, "AzureBlob", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(connStr))
        {
            var serviceClient = new BlobServiceClient(connStr);
            _containerClient = serviceClient.GetBlobContainerClient(_options.ContainerName);
        }

        _localRoot = Path.IsPathRooted(_options.LocalRootPath)
            ? _options.LocalRootPath
            : Path.Combine(AppContext.BaseDirectory, _options.LocalRootPath);
        Directory.CreateDirectory(_localRoot);
    }

    public async Task<string> UploadAsync(string blobName, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        var safeName = SanitizeBlobName(blobName);

        if (_containerClient is not null)
        {
            try
            {
                await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
                var blob = _containerClient.GetBlobClient(safeName);
                using var ms = new MemoryStream(content);
                await blob.UploadAsync(ms, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                    Metadata = new Dictionary<string, string>
                    {
                        ["platform"] = "cloudpass",
                        ["uploadedUtc"] = DateTime.UtcNow.ToString("O")
                    }
                }, cancellationToken);
                return safeName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure Blob upload failed for {BlobName}; falling back to local storage.", safeName);
            }
        }

        var localPath = Path.Combine(_localRoot, safeName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllBytesAsync(localPath, content, cancellationToken);
        return safeName;
    }

    public async Task<byte[]?> DownloadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var safeName = SanitizeBlobName(blobName);

        if (_containerClient is not null)
        {
            try
            {
                var blob = _containerClient.GetBlobClient(safeName);
                if (await blob.ExistsAsync(cancellationToken))
                {
                    var download = await blob.DownloadContentAsync(cancellationToken);
                    return download.Value.Content.ToArray();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure Blob download failed for {BlobName}.", safeName);
            }
        }

        var localPath = Path.Combine(_localRoot, safeName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(localPath))
            return await File.ReadAllBytesAsync(localPath, cancellationToken);

        return null;
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (_containerClient is not null)
        {
            try
            {
                await _containerClient.ExistsAsync(cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return Directory.Exists(_localRoot);
    }

    private static string SanitizeBlobName(string blobName)
        => blobName.Replace("..", string.Empty).TrimStart('/', '\\');
}
