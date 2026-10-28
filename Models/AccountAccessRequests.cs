using System.ComponentModel.DataAnnotations;

namespace BudgetAPI.Models
{
    public class PasswordChangeRequest
    {
        [Required] public string CurrentPassword { get; set; } = null!;
        [Required, MinLength(12), StringLength(72)] public string NewPassword { get; set; } = null!;
    }

    public class PasswordResetRequest
    {
        [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = null!;
        [Required] public string Token { get; set; } = null!;
        [Required, MinLength(12), StringLength(72)] public string NewPassword { get; set; } = null!;
    }

    public class EmailTokenRequest
    {
        [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = null!;
        [Required] public string Token { get; set; } = null!;
    }

    public class PasswordResetEmailRequest
    {
        [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = null!;
    }

    public class UserProfileResponse
    {
        public string Name { get; set; } = null!;
        public string? Email { get; set; }
        public bool EmailVerified { get; set; }
        public string? PendingEmail { get; set; }
    }

    public class UserProfileUpdateRequest
    {
        [Required, StringLength(100)] public string Name { get; set; } = null!;
        [EmailAddress, StringLength(254)] public string? Email { get; set; }
    }
}
