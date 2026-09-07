namespace BudgetAPI.Models
{
	public class UsersAuthenticateResponse
	{
		public int Id { get; set; }
		public string Name { get; set; } = null!;
		public string Login { get; set; } = null!;
		public string Token { get; set; } = null!;
	}
}
