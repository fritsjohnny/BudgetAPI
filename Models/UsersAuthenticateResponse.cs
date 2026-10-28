namespace BudgetAPI.Models
{
	public class UsersAuthenticateResponse
	{
		public int Id { get; set; }
		public string Name { get; set; } = null!;
		public string Login { get; set; } = null!;
		public string? Email { get; set; }
		public bool EmailVerified { get; set; }
		public string Token { get; set; } = null!;
	}
}
