namespace BudgetAPI.Models
{
	public class CardsReceipts
	{
		public int Id { get; set; }
		public required DateTime Date { get; set; }
		public required string Reference { get; set; }
		public required int CardId { get; set; }
		public int? PeopleId { get; set; }
		public int AccountId { get; set; }
		public decimal Amount { get; set; }
		public string? Note { get; set; }
		public Cards? Card { get; set; }
	}
}
