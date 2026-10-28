namespace BudgetAPI.Models
{
    public sealed class PhysicalCardAssociationInput
    {
        public int CardsPostingId { get; set; }
        public int? PhysicalCardId { get; set; }
    }

    public class C6StatementTransaction
    {
        public DateTime PurchaseDate { get; set; }
        public string CardholderName { get; set; } = string.Empty;
        public string LastFourDigits { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Installment { get; set; } = string.Empty;
        public decimal AmountUsd { get; set; }
        public decimal ExchangeRate { get; set; }
        public decimal AmountBrl { get; set; }
    }

    public class C6StatementPreview
    {
        public string FileName { get; set; } = string.Empty;
        public int TransactionCount { get; set; }
        public int PurchaseCount { get; set; }
        public int PaymentOrCreditCount { get; set; }
        public decimal PurchasesTotal { get; set; }
        public List<C6PhysicalCardMarker> PhysicalCards { get; set; } = new();
        public List<C6StatementTransaction> Transactions { get; set; } = new();
    }

    public class C6PhysicalCardMarker
    {
        public string CardholderName { get; set; } = string.Empty;
        public string LastFourDigits { get; set; } = string.Empty;
        public int TransactionCount { get; set; }
    }

    public class CardsPhysicalCardInput
    {
        public int? Id { get; set; }
        public string NameOnCard { get; set; } = string.Empty;
        public string LastFourDigits { get; set; } = string.Empty;
    }

    public class CardStatementDescriptionAliasInput
    {
        public int CardId { get; set; }
        public int PhysicalCardId { get; set; }
        public string StatementDescription { get; set; } = string.Empty;
        public string BudgetDescription { get; set; } = string.Empty;
    }
}
