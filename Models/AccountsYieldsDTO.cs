namespace BudgetAPI.Models
{
    public class AccountsYieldsDTO
    {
        public int AccountId { get; set; }
        public string Account { get; set; }
        public string Color { get; set; }
        public string Background { get; set; }
        public DateTime Date { get; set; }
        public string Reference { get; set; }   // ex.: "202510"
        public decimal Amount { get; set; }   
        public decimal RunningTotal { get; set; }   // SUM(...) OVER (ORDER BY Date)
        public decimal DayTotal { get; set; }   // SUM(...) OVER (PARTITION BY Date)
        public long RowNum { get; set; }   
    }
}