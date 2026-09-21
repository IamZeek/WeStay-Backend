// REMOVED: AzureBlobImageStorageService
//
// Listing images moved from Azure Blob Storage to Cloudflare R2.
// See Services/R2ImageStorageService.cs.
//
// The implementation lived here and depended on the Azure.Storage.Blobs package, which has
// been dropped from WeStay.ListingService.csproj in favour of AWSSDK.S3. The class body was
// emptied rather than the file deleted so the build stays green; DELETE THIS FILE.
//
// If you ever need the old version back, it's in git history alongside the
// AzureBlobStorage:ConnectionString / AzureBlobStorage:ContainerName settings it read.
