using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using WeStay.AuthService.Models;

namespace WeStay.AuthService.Models.Requests
{
    public class SetUserRoleRequest
    {
        // Accepts either the enum name ("Guest"/"Host"/"Admin") or its integer value (0/1/2).
        [Required]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public UserRole Role { get; set; }
    }
}
