using System.Globalization;
using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface IFinancialHealthService
    {
        Task<FinancialHealthReportDTO> GetReport(
            string initialReference,
            string finalReference,
            int reserveTargetMonths = 9,
            int futureMonths = 12,
            bool includeCurrentMonth = false);
    }

    public class FinancialHealthService : IFinancialHealthService
    {
        private readonly BudgetContext _context;
        private readonly Users _user;

        public FinancialHealthService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _user = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
        }

        public async Task<FinancialHealthReportDTO> GetReport(
            string initialReference,
            string finalReference,
            int reserveTargetMonths = 9,
            int futureMonths = 12,
            bool includeCurrentMonth = false)
        {
            DateTime requestedStart = ParseReference(initialReference, "Referência inicial");
            DateTime requestedEnd = ParseReference(finalReference, "Referência final");

            if (requestedStart > requestedEnd)
            {
                throw new ArgumentException("A referência inicial não pode ser maior que a referência final.");
            }

            if (reserveTargetMonths < 3 || reserveTargetMonths > 24)
            {
                throw new ArgumentException("A meta de reserva deve estar entre 3 e 24 meses.");
            }

            if (futureMonths < 3 || futureMonths > 36)
            {
                throw new ArgumentException("A quantidade de meses futuros deve estar entre 3 e 36.");
            }

            DateTime userToday = GetUserToday();
            DateTime currentMonth = new(userToday.Year, userToday.Month, 1);
            DateTime effectiveEnd = requestedEnd > currentMonth ? currentMonth : requestedEnd;

            if (!includeCurrentMonth && effectiveEnd >= currentMonth)
            {
                effectiveEnd = currentMonth.AddMonths(-1);
            }

            if (requestedStart > effectiveEnd)
            {
                throw new ArgumentException(
                    "O período não possui meses concluídos. Marque a opção para incluir o mês atual ou informe um período anterior.");
            }

            List<string> references = BuildReferences(requestedStart, effectiveEnd);
            int periodMonths = references.Count;

            DateTime previousEnd = requestedStart.AddMonths(-1);
            DateTime previousStart = previousEnd.AddMonths(-(periodMonths - 1));
            List<string> previousReferences = BuildReferences(previousStart, previousEnd);
            List<int> fixedCategoryIds = await LoadFixedCategoryIdsAsync();

            List<FinancialHealthMonthlyDTO> monthlyEvolution = await LoadMonthlyDataAsync(
                references,
                fixedCategoryIds);
            List<FinancialHealthMonthlyDTO> previousMonthly = await LoadMonthlyDataAsync(
                previousReferences,
                fixedCategoryIds);

            await ApplyClosingBalancesAsync(monthlyEvolution);
            MarkOutliers(monthlyEvolution);

            List<FinancialHealthAccountDTO> accounts = await LoadAccountsAsync(userToday);
            List<FinancialHealthInstitutionDTO> institutions = BuildInstitutions(accounts);

            List<CategoryAmount> currentCategoryAmounts = await LoadCategoryTotalsAsync(references);
            List<CategoryAmount> previousCategoryAmounts = await LoadCategoryTotalsAsync(previousReferences);
            List<FinancialHealthCategoryDTO> categories = BuildCategoryRows(
                currentCategoryAmounts,
                previousCategoryAmounts,
                periodMonths);

            DateTime futureStart = currentMonth.AddMonths(1);
            DateTime futureEnd = futureStart.AddMonths(futureMonths - 1);
            List<string> futureReferences = BuildReferences(futureStart, futureEnd);

            List<FinancialHealthMonthlyDTO> futureMonthly = await LoadMonthlyDataAsync(
                futureReferences,
                fixedCategoryIds);
            FutureInstallmentLoadResult futureInstallments = await LoadFutureInstallmentsAsync(futureReferences);

            decimal historicalMedianExpenses = Median(monthlyEvolution.Select(m => m.Expenses));
            List<FinancialHealthFutureProjectionDTO> futureProjection = BuildFutureProjection(
                futureMonthly,
                futureInstallments,
                historicalMedianExpenses);

            decimal futureInstallmentTotal = futureInstallments.Monthly.Values.Sum(m => m.Card + m.Direct);

            FinancialHealthSummaryDTO summary = BuildSummary(
                monthlyEvolution,
                accounts,
                institutions,
                categories,
                reserveTargetMonths,
                futureInstallmentTotal);

            FinancialHealthComparisonDTO comparison = BuildComparison(
                monthlyEvolution,
                previousMonthly,
                previousStart,
                previousEnd);

            List<string> qualityReferences = references
                .Concat(futureReferences)
                .Distinct()
                .ToList();

            FinancialHealthDataQualityDTO dataQuality = await LoadDataQualityAsync(
                qualityReferences,
                references,
                futureProjection);

            List<FinancialHealthInstallmentCategoryDTO> futureInstallmentCategories =
                BuildInstallmentCategoryRows(futureInstallments.CategoryTotals);

            List<FinancialHealthScoreComponentDTO> scoreComponents = BuildScoreComponents(summary);
            int score = Convert.ToInt32(Math.Round(scoreComponents.Sum(c => c.WeightedScore), 0));
            string classification = GetClassification(score);

            List<FinancialHealthInsightDTO> insights = BuildInsights(
                summary,
                comparison,
                monthlyEvolution,
                categories,
                futureProjection,
                futureInstallmentCategories,
                dataQuality,
                classification);

            string executiveSummary = BuildExecutiveSummary(
                summary,
                classification,
                score);

            return new FinancialHealthReportDTO
            {
                InitialReference = initialReference,
                FinalReference = finalReference,
                EffectiveFinalReference = ToReference(effectiveEnd),
                PeriodMonths = periodMonths,
                IncludeCurrentMonth = includeCurrentMonth,
                FutureMonths = futureMonths,
                GeneratedAt = DateTime.UtcNow,
                Score = score,
                Classification = classification,
                ExecutiveSummary = executiveSummary,
                Summary = summary,
                Comparison = comparison,
                MonthlyEvolution = monthlyEvolution,
                Accounts = accounts,
                Institutions = institutions,
                Categories = categories,
                FutureProjection = futureProjection,
                FutureInstallmentCategories = futureInstallmentCategories,
                DataQuality = dataQuality,
                Insights = insights.OrderBy(i => i.Priority).ThenBy(i => i.Section).ToList(),
                ScoreComponents = scoreComponents,
                Legends = BuildLegends()
            };
        }

        private async Task<List<int>> LoadFixedCategoryIdsAsync()
        {
            return await _context.Categories
                .FromSqlInterpolated(
                    $"SELECT Id, Name, UserId FROM dbo.Categories WHERE UserId = {_user.Id} AND Fixed = 1")
                .AsNoTracking()
                .Select(category => category.Id)
                .ToListAsync();
        }

        private async Task<List<FinancialHealthMonthlyDTO>> LoadMonthlyDataAsync(
            List<string> references,
            List<int> fixedCategoryIds)
        {
            Dictionary<string, FinancialHealthMonthlyDTO> rows = references.ToDictionary(
                reference => reference,
                reference => new FinancialHealthMonthlyDTO
                {
                    Reference = reference,
                    Label = FormatReference(reference)
                });

            List<MonthlyAmount> directExpenses = await _context.Expenses
                .AsNoTracking()
                .Where(e =>
                    e.UserId == _user.Id &&
                    references.Contains(e.Reference) &&
                    e.CardId == null &&
                    e.PeopleId == null)
                .GroupBy(e => e.Reference)
                .Select(group => new MonthlyAmount
                {
                    Reference = group.Key,
                    Amount = group.Sum(e => e.ToPay)
                })
                .ToListAsync();

            foreach (MonthlyAmount item in directExpenses)
            {
                rows[item.Reference].Expenses += item.Amount;
            }

            List<MonthlyAmount> cardExpenses = await (
                from posting in _context.CardsPostings.AsNoTracking()
                join card in _context.Cards.AsNoTracking() on posting.CardId equals card.Id
                where card.UserId == _user.Id
                      && posting.Reference != null
                      && references.Contains(posting.Reference)
                      && !posting.Others
                group posting by posting.Reference into grouped
                select new MonthlyAmount
                {
                    Reference = grouped.Key!,
                    Amount = grouped.Sum(posting => posting.Amount)
                })
                .ToListAsync();

            foreach (MonthlyAmount item in cardExpenses)
            {
                rows[item.Reference].Expenses += item.Amount;
            }

            List<MonthlyIncome> incomes = await _context.Incomes
                .AsNoTracking()
                .Where(i =>
                    i.UserId == _user.Id &&
                    references.Contains(i.Reference) &&
                    i.CardId == null &&
                    i.PeopleId == null)
                .GroupBy(i => i.Reference)
                .Select(group => new MonthlyIncome
                {
                    Reference = group.Key,
                    Amount = group.Sum(i => i.ToReceive),
                    Yields = group.Sum(i => i.Type == "Y" || i.Type == "y" ? i.ToReceive : 0)
                })
                .ToListAsync();

            foreach (MonthlyIncome item in incomes)
            {
                rows[item.Reference].Income = item.Amount;
                rows[item.Reference].Yields = item.Yields;
            }

            List<MonthlyAmount> netCashChanges = await (
                from posting in _context.AccountsPostings.AsNoTracking()
                join account in _context.Accounts.AsNoTracking() on posting.AccountId equals account.Id
                where account.UserId == _user.Id
                      && account.CalcInGeneral == true
                      && references.Contains(posting.Reference)
                group posting by posting.Reference into grouped
                select new MonthlyAmount
                {
                    Reference = grouped.Key,
                    Amount = grouped.Sum(posting => posting.Amount)
                })
                .ToListAsync();

            foreach (MonthlyAmount item in netCashChanges)
            {
                rows[item.Reference].NetCashChange = item.Amount;
            }

            List<MonthlyAmount> fixedDirectExpenses = await _context.Expenses
                .AsNoTracking()
                .Where(expense =>
                    expense.UserId == _user.Id &&
                    references.Contains(expense.Reference) &&
                    expense.CardId == null &&
                    expense.PeopleId == null &&
                    (
                        (expense.Parcels ?? 0) > 3 ||
                        expense.Fixed == true ||
                        (
                            expense.CategoryId.HasValue &&
                            fixedCategoryIds.Contains(expense.CategoryId.Value)
                        )
                    ))
                .GroupBy(expense => expense.Reference)
                .Select(grouped => new MonthlyAmount
                {
                    Reference = grouped.Key,
                    Amount = grouped.Sum(expense => expense.ToPay)
                })
                .ToListAsync();

            foreach (MonthlyAmount item in fixedDirectExpenses)
            {
                rows[item.Reference].FixedCommitments += item.Amount;
            }

            List<MonthlyAmount> fixedCardExpenses = await (
                from posting in _context.CardsPostings.AsNoTracking()
                join card in _context.Cards.AsNoTracking() on posting.CardId equals card.Id
                where card.UserId == _user.Id
                      && posting.Reference != null
                      && references.Contains(posting.Reference)
                      && !posting.Others
                      && (
                          (posting.Parcels ?? 0) > 3 ||
                          posting.Fixed == true ||
                          (
                              posting.CategoryId.HasValue &&
                              fixedCategoryIds.Contains(posting.CategoryId.Value)
                          )
                      )
                group posting by posting.Reference into grouped
                select new MonthlyAmount
                {
                    Reference = grouped.Key!,
                    Amount = grouped.Sum(posting => posting.Amount)
                })
                .ToListAsync();

            foreach (MonthlyAmount item in fixedCardExpenses)
            {
                rows[item.Reference].FixedCommitments += item.Amount;
            }

            foreach (FinancialHealthMonthlyDTO row in rows.Values)
            {
                row.Surplus = row.Income - row.Expenses;
                row.SurplusWithoutYields = row.Surplus - row.Yields;
                row.SavingsRate = Percentage(row.Surplus, row.Income);
            }

            return references.Select(reference => rows[reference]).ToList();
        }

        private async Task ApplyClosingBalancesAsync(List<FinancialHealthMonthlyDTO> rows)
        {
            if (rows.Count == 0)
            {
                return;
            }

            string firstReference = rows.First().Reference;

            decimal openingBalance = await (
                from posting in _context.AccountsPostings.AsNoTracking()
                join account in _context.Accounts.AsNoTracking() on posting.AccountId equals account.Id
                where account.UserId == _user.Id
                      && account.CalcInGeneral == true
                      && string.Compare(posting.Reference, firstReference) < 0
                select (decimal?)posting.Amount)
                .SumAsync() ?? 0;

            decimal runningBalance = openingBalance;

            foreach (FinancialHealthMonthlyDTO row in rows)
            {
                runningBalance += row.NetCashChange;
                row.ClosingBalance = runningBalance;
            }
        }

        private async Task<List<FinancialHealthAccountDTO>> LoadAccountsAsync(
            DateTime userToday)
        {
            List<AccountBalanceRaw> rawAccounts = await _context.Accounts
                .AsNoTracking()
                .Where(account =>
                    account.UserId == _user.Id &&
                    account.CalcInGeneral == true &&
                    (account.Disabled == false || account.Disabled == null))
                .Select(account => new AccountBalanceRaw
                {
                    Id = account.Id,
                    Name = account.Name,
                    Balance = _context.AccountsPostings
                        .Where(posting => posting.AccountId == account.Id)
                        .Sum(posting => (decimal?)posting.Amount) ?? 0,
                    TotalBalanceGross = account.TotalBalanceGross,
                    YieldPercent = account.YieldPercent,
                    YieldIndex = account.YieldIndex
                })
                .ToListAsync();

            List<int> accountIds = rawAccounts.Select(account => account.Id).ToList();

            Dictionary<int, DateTime?> maturities = await _context.AccountsApplications
                .AsNoTracking()
                .Where(application =>
                    accountIds.Contains(application.AccountId) &&
                    application.MaturityDate.HasValue &&
                    application.MaturityDate.Value >= userToday)
                .GroupBy(application => application.AccountId)
                .Select(group => new AccountMaturityRaw
                {
                    AccountId = group.Key,
                    MaturityDate = group.Min(application => application.MaturityDate)
                })
                .ToDictionaryAsync(item => item.AccountId, item => item.MaturityDate);

            List<FinancialHealthAccountDTO> accounts = rawAccounts
                .Select(account =>
                {
                    decimal grossBalance =
                        account.TotalBalanceGross.HasValue &&
                        account.TotalBalanceGross.Value != 0
                            ? account.TotalBalanceGross.Value
                            : account.Balance;

                    return new FinancialHealthAccountDTO
                    {
                        Id = account.Id,
                        Name = account.Name,
                        Institution = NormalizeInstitution(account.Name),
                        Balance = account.Balance,
                        GrossBalance = grossBalance,
                        GrossDifference = grossBalance - account.Balance,
                        YieldPercent = account.YieldPercent,
                        YieldIndex = account.YieldIndex ?? string.Empty,
                        MaturityDate = maturities.GetValueOrDefault(account.Id)
                    };
                })
                .Where(account => account.Balance != 0 || account.GrossBalance != 0)
                .OrderByDescending(account => account.Balance)
                .ThenBy(account => account.Name)
                .ToList();

            decimal total = accounts.Sum(account => account.Balance);

            foreach (FinancialHealthAccountDTO account in accounts)
            {
                account.Share = Percentage(account.Balance, total);
            }

            return accounts;
        }

        private static List<FinancialHealthInstitutionDTO> BuildInstitutions(
            List<FinancialHealthAccountDTO> accounts)
        {
            decimal total = accounts.Sum(account => account.Balance);

            return accounts
                .GroupBy(account => account.Institution)
                .Select(group => new FinancialHealthInstitutionDTO
                {
                    Name = group.Key,
                    Balance = group.Sum(account => account.Balance),
                    Accounts = group.Count()
                })
                .OrderByDescending(institution => institution.Balance)
                .ThenBy(institution => institution.Name)
                .Select(institution =>
                {
                    institution.Share = Percentage(institution.Balance, total);
                    return institution;
                })
                .ToList();
        }

        private async Task<List<CategoryAmount>> LoadCategoryTotalsAsync(List<string> references)
        {
            List<CategoryAmount> direct = await (
                from expense in _context.Expenses.AsNoTracking()
                join category in _context.Categories.AsNoTracking()
                    on expense.CategoryId equals category.Id into categoryJoin
                from category in categoryJoin.DefaultIfEmpty()
                where expense.UserId == _user.Id
                      && references.Contains(expense.Reference)
                      && expense.CardId == null
                      && expense.PeopleId == null
                group expense by new
                {
                    expense.CategoryId,
                    Name = category == null ? "Sem categoria" : category.Name
                }
                into grouped
                select new CategoryAmount
                {
                    CategoryId = grouped.Key.CategoryId,
                    Name = grouped.Key.Name,
                    Amount = grouped.Sum(expense => expense.ToPay)
                })
                .ToListAsync();

            List<CategoryAmount> card = await (
                from posting in _context.CardsPostings.AsNoTracking()
                join cardAccount in _context.Cards.AsNoTracking()
                    on posting.CardId equals cardAccount.Id
                join category in _context.Categories.AsNoTracking()
                    on posting.CategoryId equals category.Id into categoryJoin
                from category in categoryJoin.DefaultIfEmpty()
                where cardAccount.UserId == _user.Id
                      && posting.Reference != null
                      && references.Contains(posting.Reference)
                      && !posting.Others
                group posting by new
                {
                    posting.CategoryId,
                    Name = category == null ? "Sem categoria" : category.Name
                }
                into grouped
                select new CategoryAmount
                {
                    CategoryId = grouped.Key.CategoryId,
                    Name = grouped.Key.Name,
                    Amount = grouped.Sum(posting => posting.Amount)
                })
                .ToListAsync();

            return direct
                .Concat(card)
                .GroupBy(item => new { item.CategoryId, item.Name })
                .Select(group => new CategoryAmount
                {
                    CategoryId = group.Key.CategoryId,
                    Name = group.Key.Name,
                    Amount = group.Sum(item => item.Amount)
                })
                .ToList();
        }

        private static List<FinancialHealthCategoryDTO> BuildCategoryRows(
            List<CategoryAmount> current,
            List<CategoryAmount> previous,
            int periodMonths)
        {
            decimal total = current.Sum(item => item.Amount);

            Dictionary<string, CategoryAmount> previousByKey = previous
                .ToDictionary(CategoryKey, item => item);

            return current
                .Select(item =>
                {
                    decimal previousAmount = previousByKey
                        .GetValueOrDefault(CategoryKey(item))?.Amount ?? 0;

                    return new FinancialHealthCategoryDTO
                    {
                        CategoryId = item.CategoryId,
                        Name = item.Name,
                        Amount = item.Amount,
                        PreviousAmount = previousAmount,
                        ChangeAmount = item.Amount - previousAmount,
                        ChangePercent = ChangePercent(item.Amount, previousAmount),
                        Average = periodMonths > 0
                            ? Math.Round(item.Amount / periodMonths, 2)
                            : 0,
                        Share = Percentage(item.Amount, total)
                    };
                })
                .OrderByDescending(item => item.Amount)
                .ThenBy(item => item.Name)
                .ToList();
        }

        private async Task<FutureInstallmentLoadResult> LoadFutureInstallmentsAsync(
            List<string> references)
        {
            FutureInstallmentLoadResult result = new();

            foreach (string reference in references)
            {
                result.Monthly[reference] = new InstallmentMonthAmount();
            }

            List<InstallmentAmount> cardRows = await (
                from posting in _context.CardsPostings.AsNoTracking()
                join card in _context.Cards.AsNoTracking() on posting.CardId equals card.Id
                join category in _context.Categories.AsNoTracking()
                    on posting.CategoryId equals category.Id into categoryJoin
                from category in categoryJoin.DefaultIfEmpty()
                where card.UserId == _user.Id
                      && posting.Reference != null
                      && references.Contains(posting.Reference)
                      && !posting.Others
                      && (posting.Parcels ?? 0) > 1
                group posting by new
                {
                    Reference = posting.Reference,
                    Name = category == null ? "Sem categoria" : category.Name
                }
                into grouped
                select new InstallmentAmount
                {
                    Reference = grouped.Key.Reference!,
                    Category = grouped.Key.Name,
                    Amount = grouped.Sum(posting => posting.Amount)
                })
                .ToListAsync();

            foreach (InstallmentAmount row in cardRows)
            {
                result.Monthly[row.Reference].Card += row.Amount;
                result.CategoryTotals[row.Category] =
                    result.CategoryTotals.GetValueOrDefault(row.Category) + row.Amount;
            }

            List<InstallmentAmount> directRows = await (
                from expense in _context.Expenses.AsNoTracking()
                join category in _context.Categories.AsNoTracking()
                    on expense.CategoryId equals category.Id into categoryJoin
                from category in categoryJoin.DefaultIfEmpty()
                where expense.UserId == _user.Id
                      && references.Contains(expense.Reference)
                      && expense.CardId == null
                      && expense.PeopleId == null
                      && (expense.Parcels ?? 0) > 1
                group expense by new
                {
                    expense.Reference,
                    Name = category == null ? "Sem categoria" : category.Name
                }
                into grouped
                select new InstallmentAmount
                {
                    Reference = grouped.Key.Reference,
                    Category = grouped.Key.Name,
                    Amount = grouped.Sum(expense => expense.ToPay)
                })
                .ToListAsync();

            foreach (InstallmentAmount row in directRows)
            {
                result.Monthly[row.Reference].Direct += row.Amount;
                result.CategoryTotals[row.Category] =
                    result.CategoryTotals.GetValueOrDefault(row.Category) + row.Amount;
            }

            return result;
        }

        private static List<FinancialHealthFutureProjectionDTO> BuildFutureProjection(
            List<FinancialHealthMonthlyDTO> futureMonthly,
            FutureInstallmentLoadResult futureInstallments,
            decimal historicalMedianExpenses)
        {
            return futureMonthly
                .Select(month =>
                {
                    InstallmentMonthAmount installments =
                        futureInstallments.Monthly.GetValueOrDefault(month.Reference)
                        ?? new InstallmentMonthAmount();

                    bool noData = month.Income == 0 && month.Expenses == 0;
                    bool missingIncome = month.Income <= 0;
                    bool unusuallyLowExpenses =
                        historicalMedianExpenses > 0 &&
                        month.Expenses < historicalMedianExpenses * 0.45m;

                    return new FinancialHealthFutureProjectionDTO
                    {
                        Reference = month.Reference,
                        Label = month.Label,
                        Income = month.Income,
                        Expenses = month.Expenses,
                        Surplus = month.Surplus,
                        CardInstallments = installments.Card,
                        DirectInstallments = installments.Direct,
                        TotalInstallments = installments.Card + installments.Direct,
                        IsPossiblyIncomplete = noData || missingIncome || unusuallyLowExpenses
                    };
                })
                .ToList();
        }

        private async Task<FinancialHealthDataQualityDTO> LoadDataQualityAsync(
            List<string> qualityReferences,
            List<string> historicalReferences,
            List<FinancialHealthFutureProjectionDTO> futureProjection)
        {
            List<DuplicateRaw> expenseDuplicates = await _context.Expenses
                .AsNoTracking()
                .Where(expense =>
                    expense.UserId == _user.Id &&
                    qualityReferences.Contains(expense.Reference) &&
                    expense.CardId == null &&
                    expense.PeopleId == null)
                .GroupBy(expense => new
                {
                    expense.Reference,
                    expense.Description,
                    expense.ToPay,
                    expense.DueDate,
                    expense.CategoryId,
                    expense.CardId,
                    expense.PeopleId
                })
                .Where(grouped => grouped.Count() > 1)
                .Select(grouped => new DuplicateRaw
                {
                    Reference = grouped.Key.Reference,
                    Description = grouped.Key.Description ?? string.Empty,
                    Date = grouped.Key.DueDate,
                    Amount = grouped.Key.ToPay,
                    Count = grouped.Count()
                })
                .ToListAsync();

            foreach (DuplicateRaw duplicate in expenseDuplicates)
            {
                duplicate.Source = "Despesa";
            }

            List<DuplicateRaw> cardDuplicates = await (
                from posting in _context.CardsPostings.AsNoTracking()
                join card in _context.Cards.AsNoTracking() on posting.CardId equals card.Id
                where card.UserId == _user.Id
                      && posting.Reference != null
                      && qualityReferences.Contains(posting.Reference)
                      && !posting.Others
                group posting by new
                {
                    posting.CardId,
                    posting.Reference,
                    posting.Description,
                    posting.Amount,
                    posting.Date,
                    posting.DueDate,
                    posting.CategoryId,
                    posting.PeopleId
                }
                into grouped
                where grouped.Count() > 1
                select new DuplicateRaw
                {
                    Reference = grouped.Key.Reference!,
                    Description = grouped.Key.Description ?? string.Empty,
                    Date = grouped.Key.Date,
                    Amount = grouped.Key.Amount,
                    Count = grouped.Count()
                })
                .ToListAsync();

            foreach (DuplicateRaw duplicate in cardDuplicates)
            {
                duplicate.Source = "Cartão";
            }

            List<FinancialHealthPotentialDuplicateDTO> duplicateRows = expenseDuplicates
                .Concat(cardDuplicates)
                .Select(duplicate => new FinancialHealthPotentialDuplicateDTO
                {
                    Source = duplicate.Source,
                    Reference = duplicate.Reference,
                    Description = duplicate.Description,
                    Date = duplicate.Date,
                    Amount = duplicate.Amount,
                    Count = duplicate.Count,
                    ExtraRows = duplicate.Count - 1,
                    ExtraAmount = (duplicate.Count - 1) * duplicate.Amount
                })
                .OrderByDescending(duplicate => duplicate.ExtraAmount)
                .ThenBy(duplicate => duplicate.Reference)
                .Take(10)
                .ToList();

            int expensesWithoutCategory = await _context.Expenses
                .AsNoTracking()
                .CountAsync(expense =>
                    expense.UserId == _user.Id &&
                    historicalReferences.Contains(expense.Reference) &&
                    expense.CardId == null &&
                    expense.PeopleId == null &&
                    expense.CategoryId == null);

            int cardPostingsWithoutCategory = await (
                from posting in _context.CardsPostings.AsNoTracking()
                join card in _context.Cards.AsNoTracking() on posting.CardId equals card.Id
                where card.UserId == _user.Id
                      && posting.Reference != null
                      && historicalReferences.Contains(posting.Reference)
                      && !posting.Others
                      && posting.CategoryId == null
                select posting.Id)
                .CountAsync();

            int expensesWithoutDueDate = await _context.Expenses
                .AsNoTracking()
                .CountAsync(expense =>
                    expense.UserId == _user.Id &&
                    historicalReferences.Contains(expense.Reference) &&
                    expense.CardId == null &&
                    expense.PeopleId == null &&
                    expense.DueDate == null);

            int incomesWithoutReceiptDate = await _context.Incomes
                .AsNoTracking()
                .CountAsync(income =>
                    income.UserId == _user.Id &&
                    historicalReferences.Contains(income.Reference) &&
                    income.CardId == null &&
                    income.PeopleId == null &&
                    income.ReceiptDate == null);

            int allDuplicateGroups = expenseDuplicates.Count + cardDuplicates.Count;
            int allDuplicateRows = expenseDuplicates
                .Concat(cardDuplicates)
                .Sum(duplicate => duplicate.Count - 1);
            decimal allDuplicateAmount = expenseDuplicates
                .Concat(cardDuplicates)
                .Sum(duplicate => (duplicate.Count - 1) * duplicate.Amount);

            return new FinancialHealthDataQualityDTO
            {
                PotentialDuplicateGroups = allDuplicateGroups,
                PotentialDuplicateRows = allDuplicateRows,
                PotentialDuplicateAmount = allDuplicateAmount,
                ExpensesWithoutCategory = expensesWithoutCategory,
                CardPostingsWithoutCategory = cardPostingsWithoutCategory,
                ExpensesWithoutDueDate = expensesWithoutDueDate,
                IncomesWithoutReceiptDate = incomesWithoutReceiptDate,
                FutureMonthsPossiblyIncomplete =
                    futureProjection.Count(month => month.IsPossiblyIncomplete),
                PotentialDuplicates = duplicateRows
            };
        }

        private static FinancialHealthSummaryDTO BuildSummary(
            List<FinancialHealthMonthlyDTO> monthly,
            List<FinancialHealthAccountDTO> accounts,
            List<FinancialHealthInstitutionDTO> institutions,
            List<FinancialHealthCategoryDTO> categories,
            int reserveTargetMonths,
            decimal futureInstallments)
        {
            int months = monthly.Count;
            decimal totalIncome = monthly.Sum(month => month.Income);
            decimal totalExpenses = monthly.Sum(month => month.Expenses);
            decimal totalYields = monthly.Sum(month => month.Yields);
            decimal totalSurplus = totalIncome - totalExpenses;
            decimal surplusWithoutYields = totalSurplus - totalYields;

            List<FinancialHealthMonthlyDTO> normalizedMonths = monthly
                .Where(month => !month.IsOutlier)
                .ToList();

            if (normalizedMonths.Count < Math.Min(3, months))
            {
                normalizedMonths = monthly;
            }

            decimal normalizedIncome = normalizedMonths.Count > 0
                ? normalizedMonths.Average(month => month.Income)
                : 0;
            decimal normalizedExpenses = normalizedMonths.Count > 0
                ? normalizedMonths.Average(month => month.Expenses)
                : 0;
            decimal normalizedSurplus = normalizedIncome - normalizedExpenses;

            decimal liquidBalance = accounts.Sum(account => account.Balance);
            decimal grossBalance = accounts.Sum(account => account.GrossBalance);
            decimal averageFixedCommitments = months > 0
                ? monthly.Average(month => month.FixedCommitments)
                : 0;
            decimal reserveCoverage = averageFixedCommitments > 0
                ? Math.Round(liquidBalance / averageFixedCommitments, 2)
                : 0;
            decimal reserveTargetValue = averageFixedCommitments * reserveTargetMonths;

            FinancialHealthInstitutionDTO? topInstitution = institutions.FirstOrDefault();
            FinancialHealthCategoryDTO? topCategory = categories.FirstOrDefault();

            return new FinancialHealthSummaryDTO
            {
                TotalIncome = totalIncome,
                TotalExpenses = totalExpenses,
                TotalYields = totalYields,
                TotalSurplus = totalSurplus,
                SurplusWithoutYields = surplusWithoutYields,
                AverageIncome = months > 0 ? Math.Round(totalIncome / months, 2) : 0,
                AverageExpenses = months > 0 ? Math.Round(totalExpenses / months, 2) : 0,
                AverageSurplus = months > 0 ? Math.Round(totalSurplus / months, 2) : 0,
                MedianIncome = Median(monthly.Select(month => month.Income)),
                MedianExpenses = Median(monthly.Select(month => month.Expenses)),
                MedianSurplus = Median(monthly.Select(month => month.Surplus)),
                SavingsRate = Percentage(totalSurplus, totalIncome),
                SavingsRateWithoutYields = Percentage(
                    surplusWithoutYields,
                    totalIncome - totalYields),
                YieldShareOfIncome = Percentage(totalYields, totalIncome),
                YieldShareOfSurplus = totalSurplus > 0
                    ? Percentage(totalYields, totalSurplus)
                    : totalYields > 0 ? 100 : 0,
                NormalizedAverageIncome = Math.Round(normalizedIncome, 2),
                NormalizedAverageExpenses = Math.Round(normalizedExpenses, 2),
                NormalizedAverageSurplus = Math.Round(normalizedSurplus, 2),
                NormalizedSavingsRate = Percentage(normalizedSurplus, normalizedIncome),
                NormalizedMonths = normalizedMonths.Count,
                PositiveMonths = monthly.Count(month => month.Surplus > 0),
                NegativeMonths = monthly.Count(month => month.Surplus < 0),
                NeutralMonths = monthly.Count(month => month.Surplus == 0),
                NetCashChange = monthly.Sum(month => month.NetCashChange),
                LiquidBalance = liquidBalance,
                GrossBalance = grossBalance,
                GrossDifference = grossBalance - liquidBalance,
                AverageFixedCommitments = Math.Round(averageFixedCommitments, 2),
                ReserveCoverageMonths = reserveCoverage,
                ReserveTargetMonths = reserveTargetMonths,
                ReserveTargetValue = Math.Round(reserveTargetValue, 2),
                ReserveGap = Math.Max(Math.Round(reserveTargetValue - liquidBalance, 2), 0),
                FutureInstallments = futureInstallments,
                InstallmentPressurePercent = liquidBalance > 0
                    ? Percentage(futureInstallments, liquidBalance)
                    : futureInstallments > 0 ? 100 : 0,
                TopInstitutionName = topInstitution?.Name ?? string.Empty,
                TopInstitutionShare = topInstitution?.Share ?? 0,
                TopCategoryName = topCategory?.Name ?? string.Empty,
                TopCategoryShare = topCategory?.Share ?? 0
            };
        }

        private static FinancialHealthComparisonDTO BuildComparison(
            List<FinancialHealthMonthlyDTO> current,
            List<FinancialHealthMonthlyDTO> previous,
            DateTime previousStart,
            DateTime previousEnd)
        {
            bool hasPreviousData = previous.Any(month =>
                month.Income != 0 ||
                month.Expenses != 0 ||
                month.NetCashChange != 0);

            decimal currentAverageIncome = current.Count > 0
                ? current.Average(month => month.Income)
                : 0;
            decimal currentAverageExpenses = current.Count > 0
                ? current.Average(month => month.Expenses)
                : 0;
            decimal currentAverageSurplus = currentAverageIncome - currentAverageExpenses;
            decimal currentRate = Percentage(currentAverageSurplus, currentAverageIncome);

            decimal previousAverageIncome = previous.Count > 0
                ? previous.Average(month => month.Income)
                : 0;
            decimal previousAverageExpenses = previous.Count > 0
                ? previous.Average(month => month.Expenses)
                : 0;
            decimal previousAverageSurplus = previousAverageIncome - previousAverageExpenses;
            decimal previousRate = Percentage(previousAverageSurplus, previousAverageIncome);

            return new FinancialHealthComparisonDTO
            {
                HasPreviousData = hasPreviousData,
                PreviousInitialReference = ToReference(previousStart),
                PreviousFinalReference = ToReference(previousEnd),
                PreviousAverageIncome = Math.Round(previousAverageIncome, 2),
                PreviousAverageExpenses = Math.Round(previousAverageExpenses, 2),
                PreviousAverageSurplus = Math.Round(previousAverageSurplus, 2),
                PreviousSavingsRate = previousRate,
                IncomeChangePercent = hasPreviousData
                    ? ChangePercent(currentAverageIncome, previousAverageIncome)
                    : null,
                ExpensesChangePercent = hasPreviousData
                    ? ChangePercent(currentAverageExpenses, previousAverageExpenses)
                    : null,
                SurplusChangePercent = hasPreviousData
                    ? ChangePercent(currentAverageSurplus, previousAverageSurplus)
                    : null,
                SavingsRateChangePoints = hasPreviousData
                    ? Math.Round(currentRate - previousRate, 2)
                    : 0
            };
        }

        private static List<FinancialHealthInstallmentCategoryDTO>
            BuildInstallmentCategoryRows(Dictionary<string, decimal> categoryTotals)
        {
            decimal total = categoryTotals.Values.Sum();

            return categoryTotals
                .Select(item => new FinancialHealthInstallmentCategoryDTO
                {
                    Name = item.Key,
                    Amount = item.Value,
                    Share = Percentage(item.Value, total)
                })
                .OrderByDescending(item => item.Amount)
                .ThenBy(item => item.Name)
                .ToList();
        }

        private static List<FinancialHealthScoreComponentDTO> BuildScoreComponents(
            FinancialHealthSummaryDTO summary)
        {
            decimal reserveScore = summary.AverageFixedCommitments > 0
                ? Clamp(summary.ReserveCoverageMonths / summary.ReserveTargetMonths * 100)
                : 0;

            decimal savingsScore = Clamp(summary.NormalizedSavingsRate / 20m * 100);

            int totalMonths =
                summary.PositiveMonths +
                summary.NegativeMonths +
                summary.NeutralMonths;

            decimal consistencyScore = totalMonths > 0
                ? Clamp((decimal)summary.PositiveMonths / totalMonths * 100)
                : 0;

            decimal installmentScore = summary.LiquidBalance <= 0
                ? 0
                : summary.InstallmentPressurePercent <= 10
                    ? 100
                    : Clamp(100 - ((summary.InstallmentPressurePercent - 10) / 65m * 100));

            decimal concentrationScore = summary.TopInstitutionShare <= 35
                ? 100
                : Clamp(100 - ((summary.TopInstitutionShare - 35) / 50m * 100));

            List<FinancialHealthScoreComponentDTO> components = new()
            {
                CreateScoreComponent(
                    "Reserva financeira",
                    30,
                    reserveScore,
                    $"Cobertura de {summary.ReserveCoverageMonths:N2} meses para uma meta de {summary.ReserveTargetMonths} meses."),
                CreateScoreComponent(
                    "Capacidade de poupança",
                    25,
                    savingsScore,
                    $"Taxa de poupança normalizada de {summary.NormalizedSavingsRate:N2}%."),
                CreateScoreComponent(
                    "Consistência mensal",
                    20,
                    consistencyScore,
                    $"{summary.PositiveMonths} de {totalMonths} meses tiveram resultado positivo."),
                CreateScoreComponent(
                    "Pressão de parcelas",
                    15,
                    installmentScore,
                    $"Parcelas futuras equivalem a {summary.InstallmentPressurePercent:N2}% do saldo líquido."),
                CreateScoreComponent(
                    "Concentração",
                    10,
                    concentrationScore,
                    $"Maior instituição concentra {summary.TopInstitutionShare:N2}% do saldo.")
            };

            return components;
        }

        private static FinancialHealthScoreComponentDTO CreateScoreComponent(
            string name,
            decimal weight,
            decimal score,
            string explanation)
        {
            decimal roundedScore = Math.Round(Clamp(score), 2);

            return new FinancialHealthScoreComponentDTO
            {
                Name = name,
                Weight = weight,
                Score = roundedScore,
                WeightedScore = Math.Round(roundedScore * weight / 100m, 2),
                Explanation = explanation
            };
        }

        private static List<FinancialHealthInsightDTO> BuildInsights(
            FinancialHealthSummaryDTO summary,
            FinancialHealthComparisonDTO comparison,
            List<FinancialHealthMonthlyDTO> monthly,
            List<FinancialHealthCategoryDTO> categories,
            List<FinancialHealthFutureProjectionDTO> futureProjection,
            List<FinancialHealthInstallmentCategoryDTO> installmentCategories,
            FinancialHealthDataQualityDTO dataQuality,
            string classification)
        {
            List<FinancialHealthInsightDTO> insights = new();

            AddInsight(
                insights,
                "Visão geral",
                GetClassificationSeverity(classification),
                "monitor_heart",
                $"Saúde financeira: {classification}",
                $"O diagnóstico combina reserva, poupança, consistência, parcelas futuras e concentração. " +
                $"O saldo líquido atual é {Currency(summary.LiquidBalance)}.",
                1);

            if (summary.ReserveCoverageMonths >= summary.ReserveTargetMonths)
            {
                AddInsight(
                    insights,
                    "Reserva",
                    "success",
                    "verified",
                    "Meta de reserva atingida",
                    $"A reserva cobre {summary.ReserveCoverageMonths:N2} meses de compromissos fixos, " +
                    $"acima da meta de {summary.ReserveTargetMonths} meses.",
                    10);
            }
            else if (summary.ReserveCoverageMonths >= 6)
            {
                AddInsight(
                    insights,
                    "Reserva",
                    "info",
                    "savings",
                    "Reserva em nível intermediário",
                    $"A reserva cobre {summary.ReserveCoverageMonths:N2} meses. " +
                    $"Faltam {Currency(summary.ReserveGap)} para alcançar a meta configurada.",
                    10);
            }
            else if (summary.ReserveCoverageMonths >= 3)
            {
                AddInsight(
                    insights,
                    "Reserva",
                    "warning",
                    "savings",
                    "Reserva abaixo da meta",
                    $"A cobertura atual é de {summary.ReserveCoverageMonths:N2} meses. " +
                    $"A prioridade é completar mais {Currency(summary.ReserveGap)}.",
                    10);
            }
            else
            {
                AddInsight(
                    insights,
                    "Reserva",
                    "danger",
                    "warning",
                    "Reserva vulnerável",
                    $"A cobertura é de apenas {summary.ReserveCoverageMonths:N2} meses de compromissos fixos. " +
                    $"Evite ampliar obrigações até elevar a reserva.",
                    10);
            }

            if (summary.NormalizedSavingsRate >= 20)
            {
                AddInsight(
                    insights,
                    "Fluxo mensal",
                    "success",
                    "trending_up",
                    "Boa capacidade recorrente de poupança",
                    $"Desconsiderando meses atípicos, a taxa estimada é de {summary.NormalizedSavingsRate:N2}% " +
                    $"e a sobra média é {Currency(summary.NormalizedAverageSurplus)}.",
                    20);
            }
            else if (summary.NormalizedSavingsRate >= 10)
            {
                AddInsight(
                    insights,
                    "Fluxo mensal",
                    "info",
                    "trending_up",
                    "Poupança recorrente moderada",
                    $"A taxa normalizada é de {summary.NormalizedSavingsRate:N2}%. " +
                    $"Há geração de patrimônio, mas ainda com margem limitada.",
                    20);
            }
            else if (summary.NormalizedSavingsRate > 0)
            {
                AddInsight(
                    insights,
                    "Fluxo mensal",
                    "warning",
                    "trending_flat",
                    "Margem mensal apertada",
                    $"A taxa normalizada é de {summary.NormalizedSavingsRate:N2}% " +
                    $"e a sobra média recorrente é {Currency(summary.NormalizedAverageSurplus)}.",
                    20);
            }
            else
            {
                AddInsight(
                    insights,
                    "Fluxo mensal",
                    "danger",
                    "trending_down",
                    "Resultado recorrente negativo",
                    $"A leitura normalizada indica déficit médio de " +
                    $"{Currency(Math.Abs(summary.NormalizedAverageSurplus))} por mês.",
                    20);
            }

            if (summary.SurplusWithoutYields <= 0 && summary.TotalYields > 0)
            {
                AddInsight(
                    insights,
                    "Rendimentos",
                    "danger",
                    "show_chart",
                    "Rendimentos estão sustentando o resultado",
                    $"Sem os rendimentos, o período teria resultado de {Currency(summary.SurplusWithoutYields)}. " +
                    $"O orçamento operacional precisa gerar sobra própria.",
                    25);
            }
            else if (summary.YieldShareOfSurplus >= 70 && summary.TotalSurplus > 0)
            {
                AddInsight(
                    insights,
                    "Rendimentos",
                    "warning",
                    "show_chart",
                    "Alta dependência dos rendimentos",
                    $"Os rendimentos representam {summary.YieldShareOfSurplus:N2}% da sobra do período. " +
                    $"A evolução patrimonial está mais ligada aos juros do que à economia mensal.",
                    25);
            }
            else if (summary.TotalYields > 0)
            {
                AddInsight(
                    insights,
                    "Rendimentos",
                    "success",
                    "show_chart",
                    "Rendimentos complementam a poupança",
                    $"Os investimentos geraram {Currency(summary.TotalYields)}, " +
                    $"equivalentes a {summary.YieldShareOfIncome:N2}% da renda do período.",
                    25);
            }

            if (summary.NegativeMonths == 0)
            {
                AddInsight(
                    insights,
                    "Consistência",
                    "success",
                    "event_available",
                    "Todos os meses fecharam positivos",
                    $"Os {summary.PositiveMonths} meses analisados apresentaram sobra.",
                    30);
            }
            else if (summary.NegativeMonths <= Math.Max(1, monthly.Count / 4))
            {
                AddInsight(
                    insights,
                    "Consistência",
                    "info",
                    "event_note",
                    "Oscilações pontuais",
                    $"{summary.NegativeMonths} de {monthly.Count} meses fecharam negativos.",
                    30);
            }
            else
            {
                AddInsight(
                    insights,
                    "Consistência",
                    "warning",
                    "event_busy",
                    "Déficits mensais frequentes",
                    $"{summary.NegativeMonths} de {monthly.Count} meses fecharam negativos. " +
                    $"Isso reduz previsibilidade e aumenta a dependência da reserva.",
                    30);
            }

            List<FinancialHealthMonthlyDTO> incomeOutliers = monthly
                .Where(month => month.IsIncomeOutlier)
                .ToList();

            if (incomeOutliers.Count > 0)
            {
                AddInsight(
                    insights,
                    "Meses atípicos",
                    "info",
                    "auto_graph",
                    "Receitas fora do padrão detectadas",
                    $"Os meses {string.Join(", ", incomeOutliers.Select(month => month.Label))} " +
                    $"foram retirados da média normalizada por estarem distantes da mediana.",
                    35);
            }

            List<FinancialHealthMonthlyDTO> expenseOutliers = monthly
                .Where(month => month.IsExpenseOutlier)
                .ToList();

            if (expenseOutliers.Count > 0)
            {
                AddInsight(
                    insights,
                    "Meses atípicos",
                    "warning",
                    "auto_graph",
                    "Despesas fora do padrão detectadas",
                    $"Os meses {string.Join(", ", expenseOutliers.Select(month => month.Label))} " +
                    $"tiveram gastos muito diferentes do comportamento habitual.",
                    36);
            }

            if (comparison.HasPreviousData)
            {
                if (comparison.ExpensesChangePercent.HasValue)
                {
                    decimal change = comparison.ExpensesChangePercent.Value;

                    AddInsight(
                        insights,
                        "Comparação",
                        change <= -5 ? "success" : change >= 10 ? "warning" : "info",
                        change <= 0 ? "south_east" : "north_east",
                        change <= 0 ? "Despesas médias recuaram" : "Despesas médias aumentaram",
                        $"Na comparação com o período anterior, a despesa média variou {SignedPercent(change)}.",
                        40);
                }

                AddInsight(
                    insights,
                    "Comparação",
                    comparison.SavingsRateChangePoints >= 3
                        ? "success"
                        : comparison.SavingsRateChangePoints <= -3
                            ? "warning"
                            : "info",
                    "compare_arrows",
                    "Evolução da taxa de poupança",
                    $"A taxa de poupança mudou {SignedPoints(comparison.SavingsRateChangePoints)} " +
                    $"em relação ao período anterior.",
                    41);
            }

            FinancialHealthCategoryDTO? topCategory = categories.FirstOrDefault();

            if (topCategory != null)
            {
                AddInsight(
                    insights,
                    "Despesas",
                    topCategory.Share >= 30 ? "warning" : "info",
                    "category",
                    $"Maior categoria: {topCategory.Name}",
                    $"A categoria consumiu {Currency(topCategory.Amount)}, " +
                    $"ou {topCategory.Share:N2}% das despesas do período.",
                    50);
            }

            FinancialHealthCategoryDTO? fastestGrowingCategory = categories
                .Where(category =>
                    category.ChangeAmount > 500 &&
                    category.ChangePercent.HasValue &&
                    category.ChangePercent.Value >= 20)
                .OrderByDescending(category => category.ChangeAmount)
                .FirstOrDefault();

            if (fastestGrowingCategory != null)
            {
                AddInsight(
                    insights,
                    "Despesas",
                    "warning",
                    "trending_up",
                    $"Crescimento em {fastestGrowingCategory.Name}",
                    $"O gasto aumentou {Currency(fastestGrowingCategory.ChangeAmount)} " +
                    $"({fastestGrowingCategory.ChangePercent:N2}%) frente ao período anterior.",
                    51);
            }

            if (summary.InstallmentPressurePercent >= 50)
            {
                AddInsight(
                    insights,
                    "Parcelas",
                    "danger",
                    "credit_card",
                    "Parcelas futuras elevadas",
                    $"As parcelas futuras somam {Currency(summary.FutureInstallments)}, " +
                    $"equivalentes a {summary.InstallmentPressurePercent:N2}% do saldo líquido.",
                    60);
            }
            else if (summary.InstallmentPressurePercent >= 25)
            {
                AddInsight(
                    insights,
                    "Parcelas",
                    "warning",
                    "credit_card",
                    "Parcelas exigem cautela",
                    $"Há {Currency(summary.FutureInstallments)} comprometidos em parcelas futuras. " +
                    $"Evite novos parcelamentos relevantes enquanto essa curva não cair.",
                    60);
            }
            else
            {
                AddInsight(
                    insights,
                    "Parcelas",
                    "success",
                    "credit_score",
                    "Pressão de parcelas controlada",
                    $"As parcelas futuras representam {summary.InstallmentPressurePercent:N2}% " +
                    $"do saldo líquido.",
                    60);
            }

            FinancialHealthFutureProjectionDTO? peakInstallmentMonth = futureProjection
                .OrderByDescending(month => month.TotalInstallments)
                .FirstOrDefault();

            if (peakInstallmentMonth != null && peakInstallmentMonth.TotalInstallments > 0)
            {
                AddInsight(
                    insights,
                    "Parcelas",
                    "info",
                    "calendar_month",
                    $"Pico de parcelas em {peakInstallmentMonth.Label}",
                    $"O maior compromisso mensal projetado é {Currency(peakInstallmentMonth.TotalInstallments)}.",
                    61);
            }

            FinancialHealthInstallmentCategoryDTO? topInstallmentCategory =
                installmentCategories.FirstOrDefault();

            if (topInstallmentCategory != null)
            {
                AddInsight(
                    insights,
                    "Parcelas",
                    "info",
                    "sell",
                    $"Parcelas concentradas em {topInstallmentCategory.Name}",
                    $"Essa categoria representa {topInstallmentCategory.Share:N2}% " +
                    $"das parcelas futuras.",
                    62);
            }

            if (summary.TopInstitutionShare >= 70)
            {
                AddInsight(
                    insights,
                    "Patrimônio",
                    "danger",
                    "account_balance",
                    "Concentração patrimonial alta",
                    $"{summary.TopInstitutionName} concentra {summary.TopInstitutionShare:N2}% " +
                    $"do saldo líquido. Avalie diversificação por instituição e tipo de produto.",
                    70);
            }
            else if (summary.TopInstitutionShare >= 50)
            {
                AddInsight(
                    insights,
                    "Patrimônio",
                    "warning",
                    "account_balance",
                    "Concentração relevante",
                    $"{summary.TopInstitutionName} concentra {summary.TopInstitutionShare:N2}% " +
                    $"do saldo líquido.",
                    70);
            }
            else if (!string.IsNullOrWhiteSpace(summary.TopInstitutionName))
            {
                AddInsight(
                    insights,
                    "Patrimônio",
                    "success",
                    "account_balance",
                    "Distribuição por instituição equilibrada",
                    $"A maior concentração é de {summary.TopInstitutionShare:N2}% " +
                    $"em {summary.TopInstitutionName}.",
                    70);
            }

            if (summary.GrossDifference > 0)
            {
                AddInsight(
                    insights,
                    "Patrimônio",
                    "info",
                    "payments",
                    "Saldo bruto acima do líquido",
                    $"Há {Currency(summary.GrossDifference)} de diferença entre saldos brutos " +
                    $"e líquidos registrados, associada principalmente a rendimentos e tributos.",
                    71);
            }

            if (dataQuality.FutureMonthsPossiblyIncomplete > 0)
            {
                AddInsight(
                    insights,
                    "Projeção",
                    "warning",
                    "pending_actions",
                    "Projeção futura possivelmente incompleta",
                    $"{dataQuality.FutureMonthsPossiblyIncomplete} dos meses futuros têm renda ausente, " +
                    $"despesa zerada ou gasto muito abaixo da mediana histórica.",
                    80);
            }

            if (dataQuality.PotentialDuplicateGroups > 0)
            {
                AddInsight(
                    insights,
                    "Qualidade dos dados",
                    "danger",
                    "content_copy",
                    "Possíveis duplicidades encontradas",
                    $"{dataQuality.PotentialDuplicateGroups} grupos podem conter " +
                    $"{dataQuality.PotentialDuplicateRows} registros extras, somando " +
                    $"{Currency(dataQuality.PotentialDuplicateAmount)}.",
                    90);
            }
            else
            {
                AddInsight(
                    insights,
                    "Qualidade dos dados",
                    "success",
                    "fact_check",
                    "Nenhuma duplicidade exata detectada",
                    "Não foram encontrados registros idênticos nos meses analisados e projetados.",
                    90);
            }

            int missingCategories =
                dataQuality.ExpensesWithoutCategory +
                dataQuality.CardPostingsWithoutCategory;

            if (missingCategories > 0)
            {
                AddInsight(
                    insights,
                    "Qualidade dos dados",
                    "warning",
                    "label_off",
                    "Lançamentos sem categoria",
                    $"{missingCategories} lançamentos não possuem categoria e reduzem a precisão " +
                    $"dos gráficos e comparações.",
                    91);
            }

            int missingDates =
                dataQuality.ExpensesWithoutDueDate +
                dataQuality.IncomesWithoutReceiptDate;

            if (missingDates > 0)
            {
                AddInsight(
                    insights,
                    "Qualidade dos dados",
                    "warning",
                    "event_busy",
                    "Datas financeiras ausentes",
                    $"{missingDates} lançamentos estão sem data de vencimento ou recebimento. " +
                    $"Isso pode prejudicar relatórios de fluxo previsto.",
                    92);
            }

            if (summary.ReserveGap > 0 && summary.NormalizedAverageSurplus > 0)
            {
                decimal monthsToTarget = Math.Ceiling(
                    summary.ReserveGap / summary.NormalizedAverageSurplus);

                AddInsight(
                    insights,
                    "Plano de ação",
                    "info",
                    "flag",
                    "Caminho estimado para a meta de reserva",
                    $"Mantendo a sobra normalizada de {Currency(summary.NormalizedAverageSurplus)} por mês, " +
                    $"a meta pode ser alcançada em aproximadamente {monthsToTarget:N0} meses.",
                    100);
            }

            return insights;
        }

        private static string BuildExecutiveSummary(
            FinancialHealthSummaryDTO summary,
            string classification,
            int score)
        {
            string reserveText = summary.AverageFixedCommitments > 0
                ? $"A reserva cobre {summary.ReserveCoverageMonths:N2} meses de compromissos fixos"
                : "Não foi possível estimar a cobertura da reserva por falta de compromissos fixos classificados";

            string savingsText = summary.NormalizedAverageSurplus >= 0
                ? $"a sobra recorrente estimada é {Currency(summary.NormalizedAverageSurplus)} por mês"
                : $"o déficit recorrente estimado é {Currency(Math.Abs(summary.NormalizedAverageSurplus))} por mês";

            string installmentText = summary.FutureInstallments > 0
                ? $"e existem {Currency(summary.FutureInstallments)} em parcelas futuras"
                : "e não há parcelas futuras relevantes registradas";

            return $"Pontuação {score}/100, classificada como {classification}. " +
                   $"{reserveText}; {savingsText}; {installmentText}.";
        }

        private static List<FinancialHealthLegendDTO> BuildLegends()
        {
            return new List<FinancialHealthLegendDTO>
            {
                new()
                {
                    Title = "Pontuação de saúde",
                    Text = "Índice de 0 a 100 composto por reserva (30%), capacidade de poupança (25%), consistência mensal (20%), parcelas futuras (15%) e concentração patrimonial (10%)."
                },
                new()
                {
                    Title = "Taxa de poupança",
                    Text = "Resultado do período dividido pela renda do período. Valores positivos indicam que parte da renda permaneceu disponível."
                },
                new()
                {
                    Title = "Poupança sem rendimentos",
                    Text = "Remove os rendimentos financeiros da sobra para mostrar quanto o orçamento operacional gerou por conta própria."
                },
                new()
                {
                    Title = "Resultado normalizado",
                    Text = "Média calculada sem meses muito distantes da mediana de renda ou despesa. É uma estimativa automática do comportamento recorrente."
                },
                new()
                {
                    Title = "Compromissos fixos",
                    Text = "Despesas marcadas como fixas, categorias fixas ou parcelamentos com mais de três parcelas, sem contar valores de terceiros."
                },
                new()
                {
                    Title = "Cobertura da reserva",
                    Text = "Saldo líquido atual dividido pela média mensal de compromissos fixos."
                },
                new()
                {
                    Title = "Pressão de parcelas",
                    Text = "Total das parcelas futuras dividido pelo saldo líquido atual. Quanto maior, menor a flexibilidade financeira."
                },
                new()
                {
                    Title = "Variação de caixa",
                    Text = "Soma dos lançamentos reais das contas no mês. Transferências entre contas tendem a se anular."
                },
                new()
                {
                    Title = "Concentração por instituição",
                    Text = "As contas são agrupadas pelo trecho do nome antes dos parênteses. Revise nomes inconsistentes se o agrupamento não representar a instituição correta."
                },
                new()
                {
                    Title = "Possível duplicidade",
                    Text = "Registros com mesma referência, descrição, valor, data e vínculos principais. É um alerta para revisão, não uma exclusão automática."
                },
                new()
                {
                    Title = "Projeção possivelmente incompleta",
                    Text = "Mês futuro sem renda, sem despesas ou com despesas abaixo de 45% da mediana histórica."
                },
                new()
                {
                    Title = "Análise automática",
                    Text = "Os insights são produzidos por regras objetivas e comparações estatísticas. Eles se adaptam aos dados, mas não substituem a conferência dos lançamentos."
                }
            };
        }

        private static void MarkOutliers(List<FinancialHealthMonthlyDTO> rows)
        {
            decimal incomeMedian = Median(rows.Select(row => row.Income));
            decimal expenseMedian = Median(rows.Select(row => row.Expenses));

            foreach (FinancialHealthMonthlyDTO row in rows)
            {
                row.IsIncomeOutlier = IsOutlier(row.Income, incomeMedian);
                row.IsExpenseOutlier = IsOutlier(row.Expenses, expenseMedian);
            }
        }

        private static bool IsOutlier(decimal value, decimal median)
        {
            if (median <= 0)
            {
                return false;
            }

            decimal absoluteDifference = Math.Abs(value - median);
            decimal minimumDifference = Math.Max(1000m, median * 0.35m);

            return absoluteDifference >= minimumDifference &&
                   (value > median * 1.5m || value < median * 0.5m);
        }

        private static decimal Median(IEnumerable<decimal> source)
        {
            List<decimal> values = source.OrderBy(value => value).ToList();

            if (values.Count == 0)
            {
                return 0;
            }

            int middle = values.Count / 2;

            if (values.Count % 2 == 1)
            {
                return values[middle];
            }

            return Math.Round((values[middle - 1] + values[middle]) / 2m, 2);
        }

        private DateTime GetUserToday()
        {
            if (!string.IsNullOrWhiteSpace(_user.TimezoneId))
            {
                try
                {
                    TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(_user.TimezoneId);
                    return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone).Date;
                }
                catch (TimeZoneNotFoundException)
                {
                    // Usa a data local do servidor como fallback.
                }
                catch (InvalidTimeZoneException)
                {
                    // Usa a data local do servidor como fallback.
                }
            }

            return DateTime.Today;
        }

        private static DateTime ParseReference(string reference, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(reference) ||
                reference.Length != 6 ||
                !int.TryParse(reference[..4], out int year) ||
                !int.TryParse(reference[4..], out int month) ||
                month < 1 ||
                month > 12)
            {
                throw new ArgumentException($"{fieldName} inválida. Use o formato yyyyMM.");
            }

            return new DateTime(year, month, 1);
        }

        private static List<string> BuildReferences(DateTime start, DateTime end)
        {
            List<string> references = new();

            for (DateTime month = start; month <= end; month = month.AddMonths(1))
            {
                references.Add(ToReference(month));
            }

            return references;
        }

        private static string ToReference(DateTime date)
        {
            return date.ToString("yyyyMM", CultureInfo.InvariantCulture);
        }

        private static string FormatReference(string reference)
        {
            DateTime date = ParseReference(reference, "Referência");
            return date.ToString("MMM/yy", new CultureInfo("pt-BR"));
        }

        private static string NormalizeInstitution(string accountName)
        {
            int parenthesisIndex = accountName.IndexOf(" (", StringComparison.Ordinal);

            return parenthesisIndex > 0
                ? accountName[..parenthesisIndex].Trim()
                : accountName.Trim();
        }

        private static string CategoryKey(CategoryAmount item)
        {
            return $"{item.CategoryId?.ToString() ?? "0"}|{item.Name}";
        }

        private static decimal Percentage(decimal value, decimal total)
        {
            return total == 0
                ? 0
                : Math.Round(value / total * 100m, 2);
        }

        private static decimal? ChangePercent(decimal current, decimal previous)
        {
            if (previous == 0)
            {
                return null;
            }

            return Math.Round((current - previous) / Math.Abs(previous) * 100m, 2);
        }

        private static decimal Clamp(decimal value)
        {
            return Math.Min(Math.Max(value, 0), 100);
        }

        private static string GetClassification(int score)
        {
            return score switch
            {
                >= 85 => "Excelente",
                >= 70 => "Saudável",
                >= 50 => "Atenção",
                _ => "Frágil"
            };
        }

        private static string GetClassificationSeverity(string classification)
        {
            return classification switch
            {
                "Excelente" => "success",
                "Saudável" => "success",
                "Atenção" => "warning",
                _ => "danger"
            };
        }

        private static void AddInsight(
            List<FinancialHealthInsightDTO> insights,
            string section,
            string severity,
            string icon,
            string title,
            string text,
            int priority)
        {
            insights.Add(new FinancialHealthInsightDTO
            {
                Section = section,
                Severity = severity,
                Icon = icon,
                Title = title,
                Text = text,
                Priority = priority
            });
        }

        private static string Currency(decimal value)
        {
            return value.ToString("C2", new CultureInfo("pt-BR"));
        }

        private static string SignedPercent(decimal value)
        {
            return $"{(value >= 0 ? "+" : string.Empty)}{value:N2}%";
        }

        private static string SignedPoints(decimal value)
        {
            return $"{(value >= 0 ? "+" : string.Empty)}{value:N2} pontos percentuais";
        }

        private sealed class MonthlyAmount
        {
            public string Reference { get; set; } = string.Empty;
            public decimal Amount { get; set; }
        }

        private sealed class MonthlyIncome
        {
            public string Reference { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public decimal Yields { get; set; }
        }

        private sealed class AccountBalanceRaw
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Balance { get; set; }
            public decimal? TotalBalanceGross { get; set; }
            public decimal? YieldPercent { get; set; }
            public string? YieldIndex { get; set; }
        }

        private sealed class AccountMaturityRaw
        {
            public int AccountId { get; set; }
            public DateTime? MaturityDate { get; set; }
        }

        private sealed class CategoryAmount
        {
            public int? CategoryId { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Amount { get; set; }
        }

        private sealed class InstallmentAmount
        {
            public string Reference { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public decimal Amount { get; set; }
        }

        private sealed class InstallmentMonthAmount
        {
            public decimal Card { get; set; }
            public decimal Direct { get; set; }
        }

        private sealed class FutureInstallmentLoadResult
        {
            public Dictionary<string, InstallmentMonthAmount> Monthly { get; } = new();
            public Dictionary<string, decimal> CategoryTotals { get; } = new();
        }

        private sealed class DuplicateRaw
        {
            public string Source { get; set; } = string.Empty;
            public string Reference { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public DateTime? Date { get; set; }
            public decimal Amount { get; set; }
            public int Count { get; set; }
        }
    }
}
