namespace BudgetAPI.Models
{
    public class AccountHistoricalBalanceDTO
    {
        public decimal Balance { get; set; }
        public decimal GrossBalance { get; set; }
        public decimal? TotalIOF { get; set; }
        public decimal? TotalIR { get; set; }
        public int? IOFElapsedDays { get; set; }
        public DateTime? PostingDate { get; set; }
    }
}
