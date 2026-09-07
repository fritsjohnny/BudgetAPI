namespace BudgetAPI.Models
{
	public class UsersUpdateRequest
	{
		public string Name { get; set; } = null!;
		public string Login { get; set; } = null!;
		public string? Password { get; set; }
	}
}
