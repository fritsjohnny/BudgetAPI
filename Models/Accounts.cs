using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace BudgetAPI.Models
{
    public class Accounts
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; }
        public string? Color { get; set; }
        public string? Background { get; set; }
        public bool? CalcInGeneral { get; set; }
        public bool? Disabled { get; set; }
        public short? Position { get; set; }
        public string? AppPackageName { get; set; }
        /// <summary>
        /// Percentual aplicado sobre o índice de rendimento (ex: 120 para 120% do CDI).
        /// Usado no cálculo do rendimento bruto diário.
        /// </summary>
        public decimal? YieldPercent { get; set; }


        /// <summary>
        /// Nome do índice de referência para o rendimento (ex: "CDI", "IPCA", "SELIC").
        /// </summary>
        public string? YieldIndex { get; set; }


        /// <summary>
        /// Alíquota de Imposto de Renda (%) aplicada sobre o rendimento bruto.
        /// Segue tabela regressiva do IR.
        /// </summary>
        public decimal? IrPercent { get; set; }


        /// <summary>
        /// Indica se os rendimentos dessa conta são isentos de IR e IOF (ex: LCI, LCA).
        /// Se verdadeiro, ignora o campo IrPercent no cálculo.
        /// </summary>
        public bool IsTaxExempt { get; set; }
    }
}
