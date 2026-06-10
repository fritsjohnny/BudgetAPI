namespace BudgetAPI.Models
{
    public class AnnualSavingsMonthProjectionDTO
    {
        public string Reference { get; set; } = string.Empty;
        public int MonthNumber { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public decimal? Total { get; set; }
        public decimal? GeneralBalance { get; set; }
        public decimal? RealGeneralBalance { get; set; }
        public bool HasData { get; set; }
        public bool Included { get; set; }
    }
}