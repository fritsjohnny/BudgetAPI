namespace BudgetAPI.Models
{
    public class CardsInvoiceClosingDTO
    {
        public int Id { get; set; }
        public int CardId { get; set; }
        public string? CardName { get; set; }
        public string Reference { get; set; } = string.Empty;
        public DateTime ClosingDate { get; set; }
        public bool IsEstimated { get; set; }
        public bool IsClosed { get; set; }
    }
}
