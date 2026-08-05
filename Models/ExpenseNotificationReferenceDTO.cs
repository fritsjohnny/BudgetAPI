namespace BudgetAPI.Models
{
    public class ExpenseNotificationReferenceDTO
    {
        public string Reference { get; set; } = string.Empty;
        public bool HasDueToday { get; set; }
        public bool HasOverdue { get; set; }
    }
}
