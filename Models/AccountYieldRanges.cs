namespace BudgetAPI.Models
{
    public class AccountYieldRanges
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public decimal StartAmount { get; set; }
        public decimal? EndAmount { get; set; }
        public decimal YieldPercent { get; set; }
        public short Position { get; set; }
        public DateTime CreatedAt { get; set; }

        public Accounts? Account { get; set; }
    }
}