namespace BudgetAPI.Models
{
    public class CardsInvoiceClosing
    {
        public int Id { get; set; }
        public int CardId { get; set; }
        public string Reference { get; set; } = string.Empty;
        public DateTime ClosingDate { get; set; }
        public bool IsEstimated { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Cards? Card { get; set; }
    }
}
