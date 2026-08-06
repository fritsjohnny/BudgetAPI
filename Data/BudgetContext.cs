using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Data
{
    public class BudgetContext : DbContext
    {
        public BudgetContext(DbContextOptions<BudgetContext> options) : base(options)
        {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AccountsDTO>().ToTable("AccountsDTO").HasNoKey();
            modelBuilder.Entity<AccountsSummary>().ToTable("AccountsSummary").HasNoKey();
            modelBuilder.Entity<AccountsSummaryTotals>().ToTable("AccountsSummaryTotals").HasNoKey();
            modelBuilder.Entity<CardsPostingsPeople>().ToTable("CardsPostingsPeople").HasNoKey();
            modelBuilder.Entity<BudgetTotals>().ToTable("GetBudgetTotals").HasNoKey();
            modelBuilder.Entity<ExpensesByCategories>().ToTable("GetExpensesByCategories").HasNoKey();
            modelBuilder.Entity<ExpensesDTO>().ToTable("GetMyExpenses").HasNoKey();
            modelBuilder.Entity<AccountsYieldsDTO>().ToTable("GetAccountsYields").HasNoKey();
            modelBuilder.Entity<AnnualSavingsMonthProjectionDTO>().ToTable("GetAnnualSavings").HasNoKey();
            modelBuilder.Entity<AnnualSavingsConsolidatedDTO>().ToTable("GetAnnualSavingsConsolidated").HasNoKey();

            modelBuilder.Entity<CardsInvoiceClosing>(entity =>
            {
                entity.ToTable("CardsInvoiceClosings", "dbo");
                entity.HasKey(closing => closing.Id);
                entity.Property(closing => closing.Reference).HasColumnType("varchar(6)").HasMaxLength(6).IsRequired();
                entity.Property(closing => closing.ClosingDate).HasColumnType("date").IsRequired();
                entity.Property(closing => closing.IsEstimated).IsRequired();
                entity.Property(closing => closing.CreatedAt).HasColumnType("datetime2(0)").IsRequired();
                entity.Property(closing => closing.UpdatedAt).HasColumnType("datetime2(0)").IsRequired();
                entity.HasIndex(closing => new { closing.CardId, closing.Reference }).IsUnique();
                entity.HasOne(closing => closing.Card)
                    .WithMany()
                    .HasForeignKey(closing => closing.CardId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AccountsPostingApplicationDetails>(entity =>
            {
                entity.ToTable("AccountsPostingApplicationDetails", "dbo");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
                entity.Property(x => x.GrossAmount).HasColumnType("decimal(18,2)");
                entity.Property(x => x.TotalGrossBalance).HasColumnType("decimal(18,2)");
                entity.Property(x => x.TotalBalance).HasColumnType("decimal(18,2)");
                entity.Property(x => x.TotalIOF).HasColumnType("decimal(18,2)");
                entity.Property(x => x.TotalIR).HasColumnType("decimal(18,2)");
                entity.HasIndex(x => new { x.AccountPostingId, x.AccountApplicationId }).IsUnique();
                entity.HasIndex(x => x.AccountApplicationId);
                entity.HasOne(x => x.AccountPosting).WithMany(x => x.ApplicationDetails).HasForeignKey(x => x.AccountPostingId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.AccountApplication).WithMany().HasForeignKey(x => x.AccountApplicationId).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.HasDbFunction(typeof(BudgetContext).GetMethod(nameof(GetAccountTotals), new[] { typeof(int), typeof(string), typeof(int) }));
            modelBuilder.HasDbFunction(typeof(BudgetContext).GetMethod(nameof(GetAccountsSummary), new[] { typeof(string), typeof(int) }));
            modelBuilder.HasDbFunction(typeof(BudgetContext).GetMethod(nameof(GetTotalsAccountsSummary), new[] { typeof(string), typeof(int) }));
            modelBuilder.HasDbFunction(typeof(BudgetContext).GetMethod(nameof(GetCardsPostingsPeople), new[] { typeof(int), typeof(string), typeof(int) }));
            modelBuilder.HasDbFunction(typeof(BudgetContext).GetMethod(nameof(GetBudgetTotals), new[] { typeof(string), typeof(int) }));
            modelBuilder.HasDbFunction(typeof(BudgetContext).GetMethod(nameof(GetExpensesByCategories), new[] { typeof(string), typeof(int), typeof(int) }));
            modelBuilder.HasDbFunction(typeof(BudgetContext).GetMethod(nameof(GetMyExpenses), new[] { typeof(string), typeof(int) }));
            modelBuilder.HasDbFunction(typeof(BudgetContext).GetMethod(nameof(GetAccountsYields), new[] { typeof(string), typeof(int?), typeof(int) }));
            modelBuilder.HasDbFunction(typeof(BudgetContext).GetMethod(nameof(GetAnnualSavings), new[] { typeof(int), typeof(int), typeof(bool), typeof(bool) }));
            modelBuilder.HasDbFunction(typeof(BudgetContext).GetMethod(nameof(GetAnnualSavingsConsolidated), new[] { typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }));
        }

        public IQueryable<AccountsDTO> GetAccountTotals(int accountId, string reference, int userId) => FromExpression(() => GetAccountTotals(accountId, reference, userId));
        public IQueryable<AccountsSummary> GetAccountsSummary(string reference, int userId) => FromExpression(() => GetAccountsSummary(reference, userId));
        public IQueryable<AccountsSummaryTotals> GetTotalsAccountsSummary(string reference, int userId) => FromExpression(() => GetTotalsAccountsSummary(reference, userId));
        public IQueryable<CardsPostingsPeople> GetCardsPostingsPeople(int cardId, string reference, int userId) => FromExpression(() => GetCardsPostingsPeople(cardId, reference, userId));
        public IQueryable<BudgetTotals> GetBudgetTotals(string reference, int userId) => FromExpression(() => GetBudgetTotals(reference, userId));
        public IQueryable<ExpensesByCategories> GetExpensesByCategories(string reference, int cardId, int userId) => FromExpression(() => GetExpensesByCategories(reference, cardId, userId));
        public IQueryable<ExpensesDTO> GetMyExpenses(string reference, int userId) => FromExpression(() => GetMyExpenses(reference, userId));
        public IQueryable<AccountsYieldsDTO> GetAccountsYields(string? reference, int? accountId, int userId) => FromExpression(() => GetAccountsYields(reference, accountId, userId));
        public IQueryable<AnnualSavingsMonthProjectionDTO> GetAnnualSavings(int year, int userId, bool includeCurrentMonth, bool includeNextMonths) => FromExpression(() => GetAnnualSavings(year, userId, includeCurrentMonth, includeNextMonths));
        public IQueryable<AnnualSavingsConsolidatedDTO> GetAnnualSavingsConsolidated(int userId, bool includeCurrentYear, bool includeNextYears, bool includeCurrentMonth, bool includeNextMonths) => FromExpression(() => GetAnnualSavingsConsolidated(userId, includeCurrentYear, includeNextYears, includeCurrentMonth, includeNextMonths));

        public DbSet<Accounts> Accounts { get; set; }
        public DbSet<Cards> Cards { get; set; }
        public DbSet<Users> Users { get; set; }
        public DbSet<AccountsPostings> AccountsPostings { get; set; }
        public DbSet<CardsPostings> CardsPostings { get; set; }
        public DbSet<Expenses> Expenses { get; set; }
        public DbSet<Incomes> Incomes { get; set; }
        public DbSet<People> People { get; set; }
        public DbSet<CardsReceipts> CardsReceipts { get; set; }
        public DbSet<Categories> Categories { get; set; }
        public DbSet<AccountsApplications> AccountsApplications { get; set; }
        public DbSet<AccountYieldRanges> AccountYieldRanges { get; set; }
        public DbSet<CardsInvoiceClosing> CardsInvoiceClosings { get; set; } = null!;
        public DbSet<AccountsPostingApplicationDetails> AccountsPostingApplicationDetails { get; set; } = null!;
    }
}
