namespace BudgetAPI.Models
{
	public class CategoriesDTO
    {
		public int Id { get; set; }
		public string Name { get; set; } = string.Empty;
		public bool HasExpense { get; set; }
	}
}
