namespace WeStay.AuthService.Models.Requests
{
    /// <summary>Body of the internal batch POST /api/auth/users/profile-summaries.</summary>
    public class ProfileSummariesRequest
    {
        public List<int> Ids { get; set; } = new List<int>();
    }
}
