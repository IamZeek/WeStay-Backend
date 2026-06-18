namespace WeStay.ListingService.Services.Interfaces
{
    public interface IImageStorageService
    {
        /// <summary>
        /// Uploads an image and returns its publicly accessible URL
        /// (to be stored as ListingImage.ImageUrl).
        /// </summary>
        Task<string> UploadImageAsync(IFormFile file, CancellationToken cancellationToken = default);
    }
}
