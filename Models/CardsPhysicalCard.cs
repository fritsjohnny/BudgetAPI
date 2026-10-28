namespace BudgetAPI.Models
{
    public class CardsPhysicalCard
    {
        public int Id { get; set; }
        public int CardId { get; set; }
        public string NameOnCard { get; set; } = string.Empty;
        public string LastFourDigits { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        public Cards? Card { get; set; }
    }
}
