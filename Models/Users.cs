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
        public string? FcmToken { get; set; }
        public string? TimezoneId { get; set; }
    }
}
