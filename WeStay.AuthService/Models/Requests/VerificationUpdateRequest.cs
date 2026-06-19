using System.ComponentModel.DataAnnotations;
using WeStay.AuthService.Models;

namespace WeStay.AuthService.Models.Requests
{
    /// <summary>
    /// Body for PUT /api/auth/verification-update (user submits/updates their KYC document).
    /// A dedicated DTO — NOT the EF Verification entity — so model binding doesn't implicitly
    /// require the entity's non-nullable navigation/audit fields (User, RejectionReason, etc.).
    /// </summary>
    public class VerificationUpdateRequest
    {
        [Required]
        public DocumentType DocumentType { get; set; }

        [Required]
        [MaxLength(50)]
        public string DocumentNumber { get; set; }

        [Required]
        [Url]
        [MaxLength(500)]
        public string ImageUrl { get; set; }
    }
}
