namespace BudgetAPI.Models;

public class InvestmentStrategyRequestDTO
{
    public DateTime InitialDate { get; set; }
    public DateTime FinalDate { get; set; }
    public int AccountId { get; set; }
    public decimal? OperationalReserve { get; set; }
}

public class InvestmentStrategyReportDTO
{
    public decimal SafeSurplusWithoutDestination { get; set; }
    public decimal HistoricalPaidAmount { get; set; }
    public int HistoricalDays { get; set; }
    public int ReserveCoverageDays { get; set; } = 7;
    public decimal SuggestedReserve { get; set; }
    public decimal HistoricalDailyExpenseAverage { get; set; }
    public DateTime HistoricalStartDate { get; set; }
    public DateTime HistoricalEndDate { get; set; }
    public string ReserveExplanation { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal FinalBalance { get; set; }
    public decimal LowestBalance { get; set; }
    public DateTime? CriticalDate { get; set; }
    public decimal SafeSurplus { get; set; }
    public decimal RecommendedInvestment { get; set; }
    public decimal KeptInMainAccount { get; set; }
    public decimal Reserve { get; set; }
    public string Classification { get; set; } = string.Empty;
    public List<InvestmentTimelineRowDTO> Timeline { get; set; } = new();
    public List<InvestmentRecommendationDTO> Recommendations { get; set; } = new();
    public List<InvestmentExclusionDTO> Exclusions { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Limitations { get; set; } = new();
}

public class InvestmentTimelineRowDTO
{
    public DateTime Date { get; set; }
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal BaseBalance { get; set; }
    public decimal StrategyBalance { get; set; }
    public decimal ReserveMargin { get; set; }
    public bool IsCritical { get; set; }
}

public class InvestmentRecommendationDTO
{
    public int AccountId { get; set; }
    public int? ApplicationId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; }
    public decimal? Capacity { get; set; }
    public decimal RecommendedAmount { get; set; }
    public decimal YieldPercent { get; set; }
    public decimal MainAccountYieldPercent { get; set; }
    public decimal AdvantagePercent { get; set; }
    public decimal? ApplicationCapacity { get; set; }
    public decimal? RangeCapacity { get; set; }
    public decimal RangeStart { get; set; }
    public decimal? RangeEnd { get; set; }
    public decimal DestinationGrossYield { get; set; }
    public decimal DestinationNetYield { get; set; }
    public decimal SourceGrossYield { get; set; }
    public decimal SourceNetYield { get; set; }
    public decimal? CapacityAfter { get; set; }
    public decimal DestinationBalanceBefore { get; set; }
    public decimal DestinationBalanceAfter { get; set; }
    public decimal? MaximumAmount { get; set; }
    public decimal OccupiedAmount { get; set; }
    public decimal? ApplicationCapacityBefore { get; set; }
    public decimal? ApplicationCapacityAfter { get; set; }
    public int? RangeId { get; set; }
    public decimal? RangeCapacityBefore { get; set; }
    public decimal? RangeCapacityAfter { get; set; }
    public string DestinationYieldIndex { get; set; } = string.Empty;
    public string SourceYieldIndex { get; set; } = string.Empty;
    public bool IsDestinationTaxExempt { get; set; }
    public decimal DestinationIrPercent { get; set; }
    public string CapacityBasis { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class InvestmentExclusionDTO
{
    public string AccountName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
