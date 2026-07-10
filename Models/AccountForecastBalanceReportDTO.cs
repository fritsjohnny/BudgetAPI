namespace BudgetAPI.Models
{
    public class AccountForecastBalanceReportDTO
    {
        public int AccountId { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public decimal CurrentBalance { get; set; }
        public decimal FinalBalance { get; set; }
        public List<AccountForecastBalanceReportRowDTO> Rows { get; set; } = new();
    }

    public class AccountForecastBalanceReportRowDTO
    {
        public int Id { get; set; }
        public int Sequence { get; set; }
        public DateTime Date { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal Balance { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
