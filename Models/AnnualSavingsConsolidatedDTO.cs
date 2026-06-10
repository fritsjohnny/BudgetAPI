namespace BudgetAPI.Models
{
    public class AnnualSavingsConsolidatedDTO
    {
        public string Label { get; set; } = string.Empty;
        public int? Year { get; set; }
        public decimal Total { get; set; }
        public int Months { get; set; }
        public decimal Average { get; set; }
        public bool IsTotal { get; set; }
    }
}
