using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using WeStay.ListingService.Services.Interfaces;

namespace WeStay.ListingService.Services
{
    /// <summary>
    /// Stores listing images in Azure Blob Storage and returns the blob's public URL.
    /// Mirrors the upload/validation pattern of MessagingService's file upload, but persists to
    /// Azure Blob instead of local disk.
    ///
    /// Configuration (set via User Secrets / environment variables — never commit these):
    ///   AzureBlobStorage:ConnectionString  (required)
    ///   AzureBlobStorage:ContainerName     (optional, defaults to "listing-images")
    /// </summary>
    public class AzureBlobImageStorageService : IImageStorageService
    {
        private const string DefaultContainerName = "listing-images";

        private readonly IConfiguration _configuration;
        private readonly ILogger<AzureBlobImageStorageService> _logger;

        public AzureBlobImageStorageService(IConfiguration configuration, ILogger<AzureBlobImageStorageService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string> UploadImageAsync(IFormFile file, CancellationToken cancellationToken = default)
        {
            var connectionString = _configuration["AzureBlobStorage:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "AzureBlobStorage:ConnectionString is not configured. Set it via User Secrets or an environment variable.");
            }

            var containerName = _configuration["AzureBlobStorage:ContainerName"];
            if (string.IsNullOrWhiteSpace(containerName))
            {
                containerName = DefaultContainerName;
            }

            var containerClient = new BlobContainerClient(connectionString, containerName);
            // Public read access on blobs so the returned URL works directly as ListingImage.ImageUrl.
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

            var extension = Path.GetExtension(file.FileName);
            var blobName = $"{Guid.NewGuid():N}{extension}";
            var blobClient = containerClient.GetBlobClient(blobName);

            await using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(
                stream,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType }
                },
                cancellationToken);

            _logger.LogInformation("Uploaded listing image {BlobName} to container {Container}", blobName, containerName);
            return blobClient.Uri.ToString();
        }
    }
}
