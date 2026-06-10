namespace BudgetAPI.Models
{
    public class AnnualSavingsMonthDTO
    {
        public string Reference { get; set; } = string.Empty;
        public int Month { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public decimal? Total { get; set; }
        public decimal? QuarterAverage { get; set; }
        public bool ShowQuarterAverage { get; set; }
        public int QuarterRowSpan { get; set; }
        public decimal? GeneralBalance { get; set; }
        public decimal? RealGeneralBalance { get; set; }
        public bool HasData { get; set; }
    }
}
