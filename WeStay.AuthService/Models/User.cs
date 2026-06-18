using System.ComponentModel.DataAnnotations;

namespace WeStay.AuthService.Models
{
    public enum UserRole
    {
        Guest = 0,
        Host = 1,
        Admin = 2
    }

    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string Email { get; set; }

        public string PasswordHash { get; set; }

        [Required]
        [MaxLength(100)]
        public string? FirstName { get; set; }

        [Required]
        [MaxLength(100)]
        public string? LastName { get; set; }

        [Url]
        [MaxLength(500)]
        public string? ProfilePicture { get; set; }

        public DateTime? DateOfBirth { get; set; }

        [Phone]
        [MaxLength(20)]
        public string PhoneNumber { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public bool IsActive { get; set; }

        [Required]
        public UserRole Role { get; set; } = UserRole.Guest;

        [MaxLength(255)]
        public string? ExternalId { get; set; }

        [MaxLength(50)]
        public string? ExternalType { get; set; }

        [MaxLength(255)]
        public string? ExternalSubject { get; set; }

        [MaxLength(255)]
        public string? ExternalIssuer { get; set; }
        public bool? IsEmailVerified { get; set; }
        public bool? IsPhoneNoVerified { get; set; }

        public virtual Verification Verification { get; set; }

        public virtual ICollection<ExternalLogin> ExternalLogins { get; set; }
    }
}