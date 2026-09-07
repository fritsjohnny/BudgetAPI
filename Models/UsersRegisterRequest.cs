using System.ComponentModel.DataAnnotations;

namespace BudgetAPI.Models
{
	public class UsersRegisterRequest
	{
		[Required]
		public string Name { get; set; } = null!;
		[Required]
		public string Login { get; set; } = null!;
		[Required]
		public string Password { get; set; } = null!;
	}
}
