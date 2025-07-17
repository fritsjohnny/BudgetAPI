namespace BudgetAPI.Models
{
    public class ExpensesDTO2
    {
        public int Id { get; set; }
        public short? Position { get; set; }
        public string? Description { get; set; }
        public int? CategoryId { get; set; }
    }
}
