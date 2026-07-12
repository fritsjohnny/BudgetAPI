namespace BudgetAPI.Models
{
    public class FinancialHealthReportDTO
    {
        public string InitialReference { get; set; } = string.Empty;
        public string FinalReference { get; set; } = string.Empty;
        public string EffectiveFinalReference { get; set; } = string.Empty;
        public int PeriodMonths { get; set; }
        public bool IncludeCurrentMonth { get; set; }
        public int FutureMonths { get; set; }
        public DateTime GeneratedAt { get; set; }
        public int Score { get; set; }
        public string Classification { get; set; } = string.Empty;
        public string ExecutiveSummary { get; set; } = string.Empty;
        public FinancialHealthSummaryDTO Summary { get; set; } = new();
        public FinancialHealthComparisonDTO Comparison { get; set; } = new();
        public List<FinancialHealthMonthlyDTO> MonthlyEvolution { get; set; } = new();
        public List<FinancialHealthAccountDTO> Accounts { get; set; } = new();
        public List<FinancialHealthInstitutionDTO> Institutions { get; set; } = new();
        public List<FinancialHealthCategoryDTO> Categories { get; set; } = new();
        public List<FinancialHealthFutureProjectionDTO> FutureProjection { get; set; } = new();
        public List<FinancialHealthInstallmentCategoryDTO> FutureInstallmentCategories { get; set; } = new();
        public FinancialHealthDataQualityDTO DataQuality { get; set; } = new();
        public List<FinancialHealthInsightDTO> Insights { get; set; } = new();
        public List<FinancialHealthScoreComponentDTO> ScoreComponents { get; set; } = new();
        public List<FinancialHealthLegendDTO> Legends { get; set; } = new();
    }

    public class FinancialHealthSummaryDTO
    {
        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal TotalYields { get; set; }
        public decimal TotalSurplus { get; set; }
        public decimal SurplusWithoutYields { get; set; }
        public decimal AverageIncome { get; set; }
        public decimal AverageExpenses { get; set; }
        public decimal AverageSurplus { get; set; }
        public decimal MedianIncome { get; set; }
        public decimal MedianExpenses { get; set; }
        public decimal MedianSurplus { get; set; }
        public decimal SavingsRate { get; set; }
        public decimal SavingsRateWithoutYields { get; set; }
        public decimal YieldShareOfIncome { get; set; }
        public decimal YieldShareOfSurplus { get; set; }
        public decimal NormalizedAverageIncome { get; set; }
        public decimal NormalizedAverageExpenses { get; set; }
        public decimal NormalizedAverageSurplus { get; set; }
        public decimal NormalizedSavingsRate { get; set; }
        public int NormalizedMonths { get; set; }
        public int PositiveMonths { get; set; }
        public int NegativeMonths { get; set; }
        public int NeutralMonths { get; set; }
        public decimal NetCashChange { get; set; }
        public decimal LiquidBalance { get; set; }
        public decimal GrossBalance { get; set; }
        public decimal GrossDifference { get; set; }
        public decimal AverageFixedCommitments { get; set; }
        public decimal ReserveCoverageMonths { get; set; }
        public int ReserveTargetMonths { get; set; }
        public decimal ReserveTargetValue { get; set; }
        public decimal ReserveGap { get; set; }
        public decimal FutureInstallments { get; set; }
        public decimal InstallmentPressurePercent { get; set; }
        public string TopInstitutionName { get; set; } = string.Empty;
        public decimal TopInstitutionShare { get; set; }
        public string TopCategoryName { get; set; } = string.Empty;
        public decimal TopCategoryShare { get; set; }
    }

    public class FinancialHealthComparisonDTO
    {
        public bool HasPreviousData { get; set; }
        public string PreviousInitialReference { get; set; } = string.Empty;
        public string PreviousFinalReference { get; set; } = string.Empty;
        public decimal PreviousAverageIncome { get; set; }
        public decimal PreviousAverageExpenses { get; set; }
        public decimal PreviousAverageSurplus { get; set; }
        public decimal PreviousSavingsRate { get; set; }
        public decimal? IncomeChangePercent { get; set; }
        public decimal? ExpensesChangePercent { get; set; }
        public decimal? SurplusChangePercent { get; set; }
        public decimal SavingsRateChangePoints { get; set; }
    }

    public class FinancialHealthMonthlyDTO
    {
        public string Reference { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public decimal Income { get; set; }
        public decimal Expenses { get; set; }
        public decimal Yields { get; set; }
        public decimal Surplus { get; set; }
        public decimal SurplusWithoutYields { get; set; }
        public decimal SavingsRate { get; set; }
        public decimal NetCashChange { get; set; }
        public decimal FixedCommitments { get; set; }
        public decimal ClosingBalance { get; set; }
        public bool IsIncomeOutlier { get; set; }
        public bool IsExpenseOutlier { get; set; }
        public bool IsOutlier => IsIncomeOutlier || IsExpenseOutlier;
    }

    public class FinancialHealthAccountDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Institution { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public decimal GrossBalance { get; set; }
        public decimal GrossDifference { get; set; }
        public decimal Share { get; set; }
        public decimal? YieldPercent { get; set; }
        public string YieldIndex { get; set; } = string.Empty;
        public DateTime? MaturityDate { get; set; }
    }

    public class FinancialHealthInstitutionDTO
    {
        public string Name { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public decimal Share { get; set; }
        public int Accounts { get; set; }
    }

    public class FinancialHealthCategoryDTO
    {
        public int? CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal PreviousAmount { get; set; }
        public decimal ChangeAmount { get; set; }
        public decimal? ChangePercent { get; set; }
        public decimal Average { get; set; }
        public decimal Share { get; set; }
    }

    public class FinancialHealthFutureProjectionDTO
    {
        public string Reference { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public decimal Income { get; set; }
        public decimal Expenses { get; set; }
        public decimal Surplus { get; set; }
        public decimal CardInstallments { get; set; }
        public decimal DirectInstallments { get; set; }
        public decimal TotalInstallments { get; set; }
        public bool IsPossiblyIncomplete { get; set; }
    }

    public class FinancialHealthInstallmentCategoryDTO
    {
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal Share { get; set; }
    }

    public class FinancialHealthDataQualityDTO
    {
        public int PotentialDuplicateGroups { get; set; }
        public int PotentialDuplicateRows { get; set; }
        public decimal PotentialDuplicateAmount { get; set; }
        public int ExpensesWithoutCategory { get; set; }
        public int CardPostingsWithoutCategory { get; set; }
        public int ExpensesWithoutDueDate { get; set; }
        public int IncomesWithoutReceiptDate { get; set; }
        public int FutureMonthsPossiblyIncomplete { get; set; }
        public List<FinancialHealthPotentialDuplicateDTO> PotentialDuplicates { get; set; } = new();
    }

    public class FinancialHealthPotentialDuplicateDTO
    {
        public string Source { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime? Date { get; set; }
        public decimal Amount { get; set; }
        public int Count { get; set; }
        public int ExtraRows { get; set; }
        public decimal ExtraAmount { get; set; }
    }

    public class FinancialHealthInsightDTO
    {
        public string Section { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int Priority { get; set; }
    }

    public class FinancialHealthScoreComponentDTO
    {
        public string Name { get; set; } = string.Empty;
        public decimal Weight { get; set; }
        public decimal Score { get; set; }
        public decimal WeightedScore { get; set; }
        public string Explanation { get; set; } = string.Empty;
    }

    public class FinancialHealthLegendDTO
    {
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
