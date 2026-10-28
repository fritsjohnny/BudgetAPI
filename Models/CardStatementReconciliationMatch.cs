namespace BudgetAPI.Models
{
    public class CardStatementReconciliationMatch
    {
        public int Id { get; set; }
        public int CardId { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string TransactionFingerprint { get; set; } = string.Empty;
        public DateTime PurchaseDate { get; set; }
        public int? PhysicalCardId { get; set; }
        public string LastFourDigits { get; set; } = string.Empty;
        public string NormalizedStatementDescription { get; set; } = string.Empty;
        public string Installment { get; set; } = string.Empty;
        public decimal AmountBrl { get; set; }
        public int Occurrence { get; set; }
        public int? PostingId { get; set; }
        public bool IsResolved { get; set; }
        public bool IsIgnored { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Cards? Card { get; set; }
    }

    public class CardStatementReconciliationMatchInput
    {
        public int CardId { get; set; }
        public string Reference { get; set; } = string.Empty;
        public DateTime PurchaseDate { get; set; }
        public int? PhysicalCardId { get; set; }
        public string LastFourDigits { get; set; } = string.Empty;
        public string StatementDescription { get; set; } = string.Empty;
        public string Installment { get; set; } = string.Empty;
        public decimal AmountBrl { get; set; }
        public int Occurrence { get; set; }
        public int? PostingId { get; set; }
        public bool IsResolved { get; set; }
        public bool IsIgnored { get; set; }
    }

    public class CardStatementReconciliationMatchDTO
    {
        public int CardId { get; set; }
        public string Reference { get; set; } = string.Empty;
        public DateTime PurchaseDate { get; set; }
        public int? PhysicalCardId { get; set; }
        public string LastFourDigits { get; set; } = string.Empty;
        public string NormalizedStatementDescription { get; set; } = string.Empty;
        public string Installment { get; set; } = string.Empty;
        public decimal AmountBrl { get; set; }
        public int Occurrence { get; set; }
        public int? PostingId { get; set; }
        public bool IsResolved { get; set; }
        public bool IsIgnored { get; set; }
    }
}
