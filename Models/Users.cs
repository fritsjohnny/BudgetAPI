using System.Text.Json.Serialization;

namespace BudgetAPI.Models
{
    public class Users
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Login { get; set; } = null!;
        [JsonIgnore]
        public string Password { get; set; } = null!;
        public string? Email { get; set; }
        public bool EmailVerified { get; set; }
        [JsonIgnore]
        public string? PendingEmail { get; set; }
        [JsonIgnore]
        public DateTime? PasswordChangedAt { get; set; }
        public string? FcmToken { get; set; }
        public string? TimezoneId { get; set; }
    }
}
