namespace BudgetAPI.Models
{
    public class CardReceiptMovementDTO
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Reference { get; set; } = string.Empty;
        public int CardId { get; set; }
        public string? CardName { get; set; }
        public string? CardBackground { get; set; }
        public string? CardColor { get; set; }
        public int? PeopleId { get; set; }
        public string? PeopleName { get; set; }
        public int AccountId { get; set; }
        public string? AccountName { get; set; }
        public decimal Amount { get; set; }
        public string? Note { get; set; }
    }
}
