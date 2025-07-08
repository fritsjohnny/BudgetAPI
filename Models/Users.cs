using System.Text.Json.Serialization;

namespace BudgetAPI.Models
{
	public class Users
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public string Login { get; set; }
		[JsonIgnore]
		public string Password { get; set; }
        public string? FcmToken { get; set; }
    }
}
