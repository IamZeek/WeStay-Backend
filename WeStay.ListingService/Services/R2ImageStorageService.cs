using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using WeStay.ListingService.Services.Interfaces;

namespace WeStay.ListingService.Services
{
    /// <summary>
    /// Stores listing images in Cloudflare R2 and returns a publicly readable URL.
    ///
    /// R2 speaks the S3 API, so this uses AWSSDK.S3 pointed at the R2 endpoint. Three
    /// R2-specific details matter:
    ///
    ///   1. Region must be "auto" and the endpoint must be the account's
    ///      https://{accountId}.r2.cloudflarestorage.com.
    ///   2. Path-style addressing is required; virtual-host style is not supported.
    ///   3. R2 has no S3 ACLs. You cannot make an object public per-request the way
    ///      Azure's PublicAccessType.Blob worked. Public read is a property of the
    ///      BUCKET, enabled in the Cloudflare dashboard either via the r2.dev
    ///      subdomain or (better) a custom domain. That public hostname is what
    ///      R2:PublicBaseUrl must hold — the S3 endpoint above is authenticated-only
    ///      and is NOT usable as an image src.
    ///
    /// Configuration (User Secrets / environment variables — never commit these):
    ///   R2:AccountId        (required)  Cloudflare account id
    ///   R2:AccessKeyId      (required)  R2 API token access key id
    ///   R2:SecretAccessKey  (required)  R2 API token secret
    ///   R2:PublicBaseUrl    (required)  e.g. https://pub-xxxx.r2.dev
    ///                                   or   https://images.westayy.com
    ///   R2:BucketName       (optional)  defaults to "listing-images"
    ///
    /// The bucket is NOT created automatically: a bucket created through the S3 API is
    /// private, and its URLs would 404 for guests. Create it in the dashboard and turn
    /// on public access there.
    /// </summary>
    public class R2ImageStorageService : IImageStorageService
    {
        private const string DefaultBucketName = "listing-images";

        private readonly IConfiguration _configuration;
        private readonly ILogger<R2ImageStorageService> _logger;

        // Built once and reused; the SDK client is thread-safe.
        private readonly object _clientLock = new();
        private IAmazonS3? _client;

        public R2ImageStorageService(IConfiguration configuration, ILogger<R2ImageStorageService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        private string Required(string key)
        {
            var value = _configuration[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"{key} is not configured. Set it via User Secrets or an environment variable.");
            }
            return value;
        }

        private IAmazonS3 GetClient()
        {
            if (_client is not null) return _client;

            lock (_clientLock)
            {
                if (_client is not null) return _client;

                var accountId = Required("R2:AccountId");
                var accessKey = Required("R2:AccessKeyId");
                var secretKey = Required("R2:SecretAccessKey");

                var config = new AmazonS3Config
                {
                    ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
                    // R2 ignores regions but the SDK insists on one; "auto" is what
                    // Cloudflare documents.
                    AuthenticationRegion = "auto",
                    // Virtual-host style ({bucket}.{endpoint}) is not supported by R2.
                    ForcePathStyle = true,
                };

                _client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
                return _client;
            }
        }

        public async Task<string> UploadImageAsync(IFormFile file, CancellationToken cancellationToken = default)
        {
            var bucket = _configuration["R2:BucketName"];
            if (string.IsNullOrWhiteSpace(bucket)) bucket = DefaultBucketName;

            // Trailing slash trimmed so the join below can't produce a double slash.
            var publicBaseUrl = Required("R2:PublicBaseUrl").TrimEnd('/');

            var extension = Path.GetExtension(file.FileName);
            var key = $"listings/{Guid.NewGuid():N}{extension}";

            // Buffer to a seekable stream with a known length. R2 rejects the SDK's
            // default chunked/streaming signature, and an unseekable stream forces it.
            await using var source = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;

            var request = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = buffer,
                ContentType = file.ContentType,
                // Belt and braces alongside the seekable stream above.
                DisablePayloadSigning = true,
                // NOTE: deliberately no CannedACL. R2 rejects ACLs; public read comes
                // from the bucket's own settings.
            };

            try
            {
                await GetClient().PutObjectAsync(request, cancellationToken);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException(
                    $"R2 bucket '{bucket}' was not found. Create it in the Cloudflare dashboard " +
                    "and enable public access, then set R2:BucketName if it is not 'listing-images'.", ex);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException(
                    "R2 rejected the credentials. Check R2:AccessKeyId / R2:SecretAccessKey, and that " +
                    "the API token has Object Read & Write on this bucket.", ex);
            }

            var url = $"{publicBaseUrl}/{key}";
            _logger.LogInformation("Uploaded listing image {Key} to R2 bucket {Bucket}", key, bucket);
            return url;
        }
    }
}
