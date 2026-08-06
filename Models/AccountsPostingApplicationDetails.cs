using System.Text.Json.Serialization;

namespace BudgetAPI.Models;

public class AccountsPostingApplicationDetails
{
    public int Id { get; set; }
    public int AccountPostingId { get; set; }
    public int AccountApplicationId { get; set; }
    public decimal Amount { get; set; }
    public decimal? GrossAmount { get; set; }
    public decimal? TotalGrossBalance { get; set; }
    public decimal? TotalBalance { get; set; }
    public decimal? TotalIOF { get; set; }
    public decimal? TotalIR { get; set; }
    public int? IOFElapsedDays { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [JsonIgnore]
    public AccountsPostings? AccountPosting { get; set; }
    public AccountsApplications? AccountApplication { get; set; }
}
