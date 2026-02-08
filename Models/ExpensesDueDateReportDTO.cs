namespace BudgetAPI.Models
{
    public class ExpensesDueDateReportDTO
    {
        public int Id { get; set; }
        public DateTime? DueDate { get; set; }
        public string? Reference { get; set; }
        public string? Description { get; set; }
        public decimal ToPay { get; set; }
        public decimal Paid { get; set; }
        public decimal Remaining { get; set; }
        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public int? PeopleId { get; set; }
    }
}
