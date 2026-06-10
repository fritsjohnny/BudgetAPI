namespace BudgetAPI.Models
{
    public class AnnualSavingsReportDTO
    {
        public int Year { get; set; }
        public decimal Total { get; set; }
        public int Months { get; set; }
        public decimal Average { get; set; }
        public decimal GeneralBalance { get; set; }
        public decimal RealGeneralBalance { get; set; }
        public List<AnnualSavingsMonthDTO> MonthRows { get; set; } = new List<AnnualSavingsMonthDTO>();
    }
}
