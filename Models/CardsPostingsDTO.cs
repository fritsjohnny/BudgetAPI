﻿namespace BudgetAPI.Models
{
    public class CardsPostingsDTO
    {
        public int Id { get; set; }
        public int CardId { get; set; }
        public DateTime Date { get; set; }
        public string Reference { get; set; }
        public string? PeopleId { get; set; }
        public short? Position { get; set; }
        public string Description { get; set; }
        public int? ParcelNumber { get; set; }
        public int? Parcels { get; set; }
        public decimal Amount { get; set; }
        public decimal? TotalAmount { get; set; }
        public bool Others { get; set; }
        public string? Note { get; set; }
        public int? CategoryId { get; set; }
        public string? Category { get; set; }
        public People? People { get; set; }
        public Cards? Card { get; set; }
        public bool InTheCycle { get; set; }
        public int? RelatedId { get; set; }
        public bool? Fixed { get; set; }
        public DateTime? DueDate { get; set; }
        public bool? IsPaid { get; set; }
    }
}
