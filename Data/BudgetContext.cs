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
            modelBuilder.Entity<AccountVerificationCode>(entity =>
            {
                entity.ToTable("AccountVerificationCodes", "dbo");
                entity.HasKey(code => code.Id);
                entity.Property(code => code.Purpose).HasMaxLength(32).IsRequired();
                entity.Property(code => code.ValueHash).HasColumnType("char(64)").HasMaxLength(64).IsRequired();
                entity.Property(code => code.Email).HasMaxLength(254);
                entity.Property(code => code.CreatedAtUtc).HasColumnType("datetime2(0)").IsRequired();
                entity.Property(code => code.ExpiresAtUtc).HasColumnType("datetime2(0)").IsRequired();
                entity.HasIndex(code => code.ValueHash).IsUnique();
                entity.HasIndex(code => new { code.UserId, code.Purpose, code.CreatedAtUtc });
            });


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

            modelBuilder.Entity<Cards>()
                .Property(card => card.StatementPasswordProtected)
                .HasColumnType("nvarchar(2048)")
                .HasMaxLength(2048);

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

            modelBuilder.Entity<AccountsPostings>(entity =>
            {
                entity.Property(x => x.DiRatePercent).HasColumnType("decimal(18,10)");
                entity.Property(x => x.DiRateDate).HasColumnType("date");
            });

            modelBuilder.Entity<CardsPhysicalCard>(entity =>
            {
                entity.ToTable("CardsPhysicalCards", "dbo");
                entity.HasKey(card => card.Id);
                entity.Property(card => card.NameOnCard).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
                entity.Property(card => card.LastFourDigits).HasColumnType("char(4)").HasMaxLength(4).IsRequired();
                entity.Property(card => card.IsActive).IsRequired();
                entity.Property(card => card.CreatedAt).HasColumnType("datetime2(0)").IsRequired();
                entity.HasIndex(card => new { card.CardId, card.LastFourDigits, card.NameOnCard }).IsUnique();
                entity.HasOne(card => card.Card).WithMany().HasForeignKey(card => card.CardId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CardStatementDescriptionAlias>(entity =>
            {
                entity.ToTable("CardStatementDescriptionAliases", "dbo");
                entity.HasKey(alias => alias.Id);
                entity.Property(alias => alias.StatementDescription).HasColumnType("varchar(150)").HasMaxLength(150).IsRequired();
                entity.Property(alias => alias.NormalizedStatementDescription).HasColumnType("varchar(150)").HasMaxLength(150).IsRequired();
                entity.Property(alias => alias.BudgetDescription).HasColumnType("varchar(150)").HasMaxLength(150).IsRequired();
                entity.Property(alias => alias.CreatedAt).HasColumnType("datetime2(0)").IsRequired();
                entity.Property(alias => alias.UpdatedAt).HasColumnType("datetime2(0)").IsRequired();
                entity.HasIndex(alias => new { alias.CardId, alias.PhysicalCardId, alias.NormalizedStatementDescription }).IsUnique();
                entity.HasOne(alias => alias.Card).WithMany().HasForeignKey(alias => alias.CardId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(alias => alias.PhysicalCard).WithMany().HasForeignKey(alias => alias.PhysicalCardId).OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<CardStatementReconciliationMatch>(entity =>
            {
                entity.ToTable("CardStatementReconciliationMatches", "dbo");
                entity.HasKey(match => match.Id);
                entity.Property(match => match.Reference).HasColumnType("varchar(6)").HasMaxLength(6).IsRequired();
                entity.Property(match => match.TransactionFingerprint).HasColumnType("char(64)").HasMaxLength(64).IsRequired();
                entity.Property(match => match.PurchaseDate).HasColumnType("date").IsRequired();
                entity.Property(match => match.LastFourDigits).HasColumnType("char(4)").HasMaxLength(4).IsRequired();
                entity.Property(match => match.NormalizedStatementDescription).HasColumnType("varchar(150)").HasMaxLength(150).IsRequired();
                entity.Property(match => match.Installment).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
                entity.Property(match => match.AmountBrl).HasColumnType("decimal(18,2)").IsRequired();
                entity.Property(match => match.IsIgnored).IsRequired();
                entity.Property(match => match.IsResolved).IsRequired();
                entity.Property(match => match.CreatedAt).HasColumnType("datetime2(0)").IsRequired();
                entity.Property(match => match.UpdatedAt).HasColumnType("datetime2(0)").IsRequired();
                entity.HasIndex(match => new { match.CardId, match.Reference, match.TransactionFingerprint }).IsUnique();
                entity.HasOne(match => match.Card).WithMany().HasForeignKey(match => match.CardId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CardsPostings>()
                .HasOne(posting => posting.PhysicalCard)
                .WithMany()
                .HasForeignKey(posting => posting.PhysicalCardId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.HasDbFunction(GetDbFunctionMethod(nameof(GetAccountTotals), typeof(int), typeof(string), typeof(int)));
            modelBuilder.HasDbFunction(GetDbFunctionMethod(nameof(GetAccountsSummary), typeof(string), typeof(int)));
            modelBuilder.HasDbFunction(GetDbFunctionMethod(nameof(GetTotalsAccountsSummary), typeof(string), typeof(int)));
            modelBuilder.HasDbFunction(GetDbFunctionMethod(nameof(GetCardsPostingsPeople), typeof(int), typeof(string), typeof(int)));
            modelBuilder.HasDbFunction(GetDbFunctionMethod(nameof(GetBudgetTotals), typeof(string), typeof(int)));
            modelBuilder.HasDbFunction(GetDbFunctionMethod(nameof(GetExpensesByCategories), typeof(string), typeof(int), typeof(int)));
            modelBuilder.HasDbFunction(GetDbFunctionMethod(nameof(GetMyExpenses), typeof(string), typeof(int)));
            modelBuilder.HasDbFunction(GetDbFunctionMethod(nameof(GetAccountsYields), typeof(string), typeof(int?), typeof(int)));
            modelBuilder.HasDbFunction(GetDbFunctionMethod(nameof(GetAnnualSavings), typeof(int), typeof(int), typeof(bool), typeof(bool)));
            modelBuilder.HasDbFunction(GetDbFunctionMethod(nameof(GetAnnualSavingsConsolidated), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool)));
        }

        private static System.Reflection.MethodInfo GetDbFunctionMethod(string methodName, params Type[] parameterTypes)
        {
            return typeof(BudgetContext).GetMethod(methodName, parameterTypes)
                ?? throw new InvalidOperationException($"Método de DbFunction não encontrado: {methodName}.");
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

        public DbSet<Accounts> Accounts { get; set; } = null!;
        public DbSet<Cards> Cards { get; set; } = null!;
        public DbSet<Users> Users { get; set; } = null!;
        public DbSet<AccountVerificationCode> AccountVerificationCodes { get; set; } = null!;
        public DbSet<AccountsPostings> AccountsPostings { get; set; } = null!;
        public DbSet<CardsPostings> CardsPostings { get; set; } = null!;
        public DbSet<CardsPhysicalCard> CardsPhysicalCards { get; set; } = null!;
        public DbSet<CardStatementDescriptionAlias> CardStatementDescriptionAliases { get; set; } = null!;
        public DbSet<CardStatementReconciliationMatch> CardStatementReconciliationMatches { get; set; } = null!;
        public DbSet<Expenses> Expenses { get; set; } = null!;
        public DbSet<Incomes> Incomes { get; set; } = null!;
        public DbSet<People> People { get; set; } = null!;
        public DbSet<CardsReceipts> CardsReceipts { get; set; } = null!;
        public DbSet<Categories> Categories { get; set; } = null!;
        public DbSet<AccountsApplications> AccountsApplications { get; set; } = null!;
        public DbSet<AccountYieldRanges> AccountYieldRanges { get; set; } = null!;
        public DbSet<CardsInvoiceClosing> CardsInvoiceClosings { get; set; } = null!;
        public DbSet<AccountsPostingApplicationDetails> AccountsPostingApplicationDetails { get; set; } = null!;
    }
}
