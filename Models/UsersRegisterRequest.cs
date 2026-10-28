using System.ComponentModel.DataAnnotations;

namespace BudgetAPI.Models
{
    public class UsersRegisterRequest
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = null!;

        [Required, StringLength(50)]
        public string Login { get; set; } = null!;

        [Required, EmailAddress, StringLength(254)]
        public string Email { get; set; } = null!;

        [Required, MinLength(12), StringLength(72)]
        public string Password { get; set; } = null!;
    }
}
