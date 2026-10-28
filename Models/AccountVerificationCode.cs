namespace BudgetAPI.Models
{
    public class AccountVerificationCode
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Purpose { get; set; } = null!;
        public string ValueHash { get; set; } = null!;
        public string? Email { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
