using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WeStay.AuthService.Models
{
    public enum DocumentType
    {
        Passport,
        NationalID,
        Other
    }

    public enum VerificationStatus
    {
        Pending,
        Approved,
        Rejected,
        UnderReview
    }

    public class Verification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        [Required]
        public DocumentType DocumentType { get; set; }

        [Required]
        [MaxLength(50)]
        public string DocumentNumber { get; set; }

        [Required]
        [Url]
        [MaxLength(500)]
        public string ImageUrl { get; set; }

        [Required]
        public VerificationStatus Status { get; set; } = VerificationStatus.Pending;

        public DateTime? VerifiedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Maps to a NOT NULL column; default to empty so inserts/updates that don't set a reason
        // (creation, approval) don't fail with a NULL-insert error.
        [MaxLength(500)]
        public string RejectionReason { get; set; } = string.Empty;
    }
}