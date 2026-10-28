namespace BudgetAPI.Models
{
    public class CardStatementDescriptionAlias
    {
        public int Id { get; set; }
        public int CardId { get; set; }
        public int PhysicalCardId { get; set; }
        public string StatementDescription { get; set; } = string.Empty;
        public string NormalizedStatementDescription { get; set; } = string.Empty;
        public string BudgetDescription { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Cards? Card { get; set; }
        public CardsPhysicalCard? PhysicalCard { get; set; }
    }
}
