namespace BudgetAPI.Models
{
    public class BudgetTotals
    {
        public decimal MyExpenses { get; set; }
        public decimal MyExpensesPerc { get; set; }
        public decimal OthersExpenses { get; set; }
        public decimal OthersExpensesPerc { get; set; }

        public decimal MyIncomes { get; set; }
        public decimal MyIncomesPerc { get; set; }
        public decimal OthersIncomes { get; set; }
        public decimal OthersIncomesPerc { get; set; }

        public decimal MyYields { get; set; }
        public decimal MyYieldsPerc { get; set; }
        public decimal MyIncomesWithoutYields { get; set; }
        public decimal MyIncomesWithoutYieldsPerc { get; set; }
    }
}