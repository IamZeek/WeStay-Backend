namespace WeStay.AuthService.Models
{
    /// <summary>
    /// Minimal public-safe host/user profile served to other services (ListingService enriches
    /// listing responses with it). IsVerified = the user's KYC Verification status is Approved.
    /// </summary>
    public class UserProfileSummary
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public bool IsVerified { get; set; }
        public string? AvatarUrl { get; set; }
    }
}
