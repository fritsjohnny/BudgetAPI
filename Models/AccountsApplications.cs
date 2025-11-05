namespace BudgetAPI.Models
{
    /// <summary>
    /// Registro de aporte vinculado à conta (base para cálculo de IOF/IR/rendimentos por aplicação).
    /// </summary>
    public class AccountsApplications
    {
        /// <summary>
        /// Chave primária da aplicação. Imutável.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// ID da conta (FK) que recebeu o aporte.
        /// </summary>
        public int AccountId { get; set; }

        /// <summary>
        /// Data em que o aporte foi efetivado (D+0). Base para IOF/IR.
        /// </summary>
        public DateTime DateApplied { get; set; }

        /// <summary>
        /// Valor bruto aportado em R$ (não inclui rendimentos).
        /// </summary>
        public decimal AmountApplied { get; set; }

        /// <summary>
        /// Percentual do CDI contratado (fração: 1.07 = 107%). NULO para prefixado.
        /// </summary>
        public decimal? CdiPercent { get; set; }

        /// <summary>
        /// Taxa prefixada a.a. (fração: 0.1250 = 12,50% a.a.). NULO para pós-CDI.
        /// </summary>
        public decimal? FixedRate { get; set; }

        /// <summary>
        /// Data de vencimento do aporte. NULO para liquidez diária.
        /// </summary>
        public DateTime? MaturityDate { get; set; }

        /// <summary>
        /// Timestamp de criação do registro (auditoria).
        /// </summary>
        public DateTime CreatedAt { get; set; }

        public AccountsApplications()
        {
            CreatedAt = DateTime.UtcNow;
        }
    }
}
