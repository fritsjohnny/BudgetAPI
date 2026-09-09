using System.Globalization;
using System.Text.Json;
using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services;

public interface IInvestmentStrategyService
{
    Task<InvestmentStrategyReportDTO> GetReport(InvestmentStrategyRequestDTO request);
}

public sealed class InvestmentStrategyService : IInvestmentStrategyService
{
    private readonly BudgetContext _context;
    private readonly Users _user;
    private readonly IHttpClientFactory _httpClientFactory;

    public InvestmentStrategyService(BudgetContext context, IHttpContextAccessor accessor, IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _user = accessor.HttpContext?.Items["User"] as Users ?? new Users();
        _httpClientFactory = httpClientFactory;
    }

    public async Task<InvestmentStrategyReportDTO> GetReport(InvestmentStrategyRequestDTO request)
    {
        if (request.InitialDate.Date > request.FinalDate.Date)
        {
            throw new ArgumentException("Initial date cannot be greater than final date.");
        }

        Accounts main = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.AccountId && x.UserId == _user.Id && x.Disabled != true)
            ?? throw new InvalidOperationException("Main account was not found or is disabled.");

        decimal balance = await _context.AccountsPostings
            .Where(x => x.AccountId == main.Id)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        DateTime historyEnd = CurrentBrazilDate();
        DateTime historyStart = historyEnd.AddDays(-89);
        List<Move> paidRows = await _context.Expenses.AsNoTracking()
            .Where(x => x.UserId == _user.Id && x.DueDate >= historyStart && x.DueDate <= historyEnd && x.Paid != 0)
            .Select(x => new Move(x.DueDate!.Value.Date, Math.Abs(x.Paid), false))
            .ToListAsync();
        decimal historicalPaid = paidRows.Sum(x => x.Amount);
        DateTime historicalStart = paidRows.Count == 0 ? historyStart : paidRows.Min(x => x.Date);
        DateTime historicalEnd = paidRows.Count == 0 ? historyEnd : paidRows.Max(x => x.Date);
        int historicalDays = paidRows.Count == 0 ? 0 : Math.Max(1, (historyEnd - historyStart).Days + 1);
        decimal average = historicalDays == 0 ? 0 : historicalPaid / historicalDays;

        List<Move> expenses = await _context.Expenses.AsNoTracking()
            .Where(x => x.UserId == _user.Id && x.DueDate <= request.FinalDate && x.ToPay - Math.Abs(x.Paid) != 0 && x.DueDate >= request.InitialDate)
            .Select(x => new Move(x.DueDate!.Value.Date, x.ToPay - Math.Abs(x.Paid), false))
            .ToListAsync();
        List<Move> overdueExpenses = await _context.Expenses.AsNoTracking()
            .Where(x => x.UserId == _user.Id && x.DueDate < request.InitialDate && x.ToPay - Math.Abs(x.Paid) != 0)
            .Select(x => new Move(request.InitialDate.Date, x.ToPay - Math.Abs(x.Paid), false))
            .ToListAsync();
        expenses.AddRange(overdueExpenses);

        List<Move> incomes = await _context.Incomes.AsNoTracking()
            .Where(x => x.UserId == _user.Id && x.ReceiptDate >= request.InitialDate && x.ReceiptDate <= request.FinalDate && x.ToReceive - x.Received != 0)
            .Select(x => new Move(x.ReceiptDate!.Value.Date, x.ToReceive - x.Received, true))
            .ToListAsync();

        int overdueIncomeCount = await _context.Incomes.CountAsync(x => x.UserId == _user.Id && x.ReceiptDate < request.InitialDate && x.ToReceive - x.Received != 0);

        InvestmentStrategyReportDTO report = new()
        {
            CurrentBalance = balance,
            TotalIncome = incomes.Sum(x => x.Amount),
            TotalExpense = expenses.Sum(x => x.Amount),
            HistoricalPaidAmount = historicalPaid,
            HistoricalDays = historicalDays,
            HistoricalStartDate = historicalStart,
            HistoricalEndDate = historicalEnd,
            HistoricalDailyExpenseAverage = Math.Round(average, 2),
            ReserveCoverageDays = 7,
            SuggestedReserve = historicalDays > 0 ? Math.Round(average * 7, 2) : Math.Round(expenses.Sum(x => x.Amount) * .10m, 2)
        };

        report.Reserve = Math.Max(0, request.OperationalReserve ?? report.SuggestedReserve);
        report.ReserveExplanation = historicalDays > 0
            ? $"Reserva baseada na média diária de {average.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))} durante {historicalDays} dias, com 7 dias de cobertura."
            : "Não há histórico pago suficiente. Foi utilizado o fallback de 10% das despesas pendentes.";

        if (overdueIncomeCount > 0)
        {
            report.Warnings.Add("Existem receitas vencidas ainda não recebidas. Elas não foram consideradas como disponíveis na estratégia.");
        }

        decimal running = balance;

        foreach (IGrouping<DateTime, Move> day in incomes.Concat(expenses).GroupBy(x => x.Date).OrderBy(x => x.Key))
        {
            decimal income = day.Where(x => x.Income).Sum(x => x.Amount);
            decimal expense = day.Where(x => !x.Income).Sum(x => x.Amount);
            running += income - expense;

            report.Timeline.Add(new InvestmentTimelineRowDTO
            {
                Date = day.Key,
                Income = income,
                Expense = expense,
                BaseBalance = running
            });
        }

        report.FinalBalance = running;
        InvestmentTimelineRowDTO? critical = report.Timeline.OrderBy(x => x.BaseBalance).FirstOrDefault();
        report.LowestBalance = critical?.BaseBalance ?? balance;
        report.CriticalDate = critical?.Date;
        report.SafeSurplus = Math.Max(0, report.LowestBalance - report.Reserve);

        DateTime projectionStart = historyEnd;
        DateTime projectionDate = request.FinalDate.Date < projectionStart ? projectionStart : request.FinalDate.Date;
        decimal? cdiDailyPercent = await GetLatestCdiDailyPercent();
        report.ProjectionDate = projectionDate;
        report.CdiDailyPercentUsed = cdiDailyPercent;
        report.ProjectionBusinessDays = BusinessDaysBetween(projectionStart, projectionDate);

        if (cdiDailyPercent.HasValue)
        {
            report.Limitations.Add($"A projeção compara os valores líquidos até {projectionDate:dd/MM/yyyy} usando o último CDI diário disponível ({cdiDailyPercent.Value:0.######}% ao dia) como taxa constante. Feriados futuros não são projetados; sábados e domingos não rendem CDI.");
        }
        else
        {
            report.Limitations.Add("Não foi possível obter o CDI diário para projetar o valor líquido futuro. Aplicações CDI não serão recomendadas sem essa base de comparação.");
        }

        List<Accounts> accounts = await _context.Accounts.AsNoTracking()
            .Where(x => x.UserId == _user.Id && x.Disabled != true)
            .ToListAsync();
        List<int> accountIds = accounts.Select(x => x.Id).ToList();
        InvestmentTargetConfigurationDTO? targetConfiguration = NormalizeTargetConfiguration(request.TargetConfiguration);

        if (targetConfiguration != null && !accounts.Any(account => account.Id == targetConfiguration.AccountId))
        {
            throw new InvalidOperationException("A conta alvo não foi encontrada ou está desativada.");
        }


        Dictionary<int, decimal> balances = await _context.AccountsPostings.AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId))
            .GroupBy(x => x.AccountId)
            .Select(group => new { AccountId = group.Key, Balance = group.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Balance);
        balances[main.Id] = balance;

        List<AccountsApplications> applications = await _context.AccountsApplications.AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId) && !x.Disabled)
            .ToListAsync();
        Dictionary<int, List<AccountsApplications>> applicationsByAccount = applications
            .GroupBy(x => x.AccountId)
            .ToDictionary(group => group.Key, group => group.OrderBy(x => x.DateApplied).ThenBy(x => x.Id).ToList());

        List<AccountYieldRanges> allRanges = await _context.AccountYieldRanges.AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId))
            .OrderBy(x => x.StartAmount)
            .ToListAsync();
        Dictionary<int, List<AccountYieldRanges>> rangesByAccount = allRanges
            .GroupBy(x => x.AccountId)
            .ToDictionary(group => group.Key, group => group.ToList());

        List<State> destinations = BuildDestinations(report, accounts, balances, applicationsByAccount, rangesByAccount, targetConfiguration);
        List<SourceState> sources = await BuildSources(accounts, balances, applicationsByAccount, rangesByAccount, main.Id, projectionStart);

        List<string> taxableAccountsWithoutApplications = accounts
            .Where(account => account.Id != main.Id && !account.IsTaxExempt && balances.GetValueOrDefault(account.Id) > 0)
            .Where(account => !applicationsByAccount.ContainsKey(account.Id) || applicationsByAccount[account.Id].Count == 0)
            .Select(account => account.Name)
            .OrderBy(name => name)
            .ToList();

        if (taxableAccountsWithoutApplications.Count > 0)
        {
            report.Limitations.Add($"Saldos tributáveis sem aplicação vinculada não foram usados como origem porque a idade fiscal não é conhecida: {string.Join(", ", taxableAccountsWithoutApplications)}.");
        }

        if (applications.Any(x => x.MaturityDate.HasValue && x.MaturityDate.Value.Date > projectionStart))
        {
            report.Limitations.Add("O cadastro de aplicações não informa carência ou liquidez de resgate. Aplicações com vencimento futuro foram consideradas resgatáveis; confirme a liquidez antes de executar a transferência.");
        }

        if (targetConfiguration?.LockedUntilMaturity == true && targetConfiguration.MaturityDate.HasValue && targetConfiguration.MaturityDate.Value.Date > projectionStart)
        {
            report.Limitations.Add($"A conta alvo foi configurada sem liquidez garantida até {targetConfiguration.MaturityDate.Value:dd/MM/yyyy}. O valor recomendado deve ser considerado indisponível até essa data.");
        }

        if (targetConfiguration?.MaturityDate.HasValue == true && targetConfiguration.MaturityDate.Value.Date <= projectionStart)
        {
            report.Warnings.Add("A condição configurada para a conta alvo já venceu. Nenhuma nova alocação será recomendada para esse produto.");
        }

        Dictionary<int, decimal> sourceAccountRemaining = accounts.ToDictionary(
            account => account.Id,
            account => account.Id == main.Id
                ? report.SafeSurplus
                : Math.Max(0, balances.GetValueOrDefault(account.Id)));

        int guard = 0;

        while (guard++ < 5000)
        {
            List<Candidate> candidates = sources
                .Where(source => source.RemainingCapacity > 0 && sourceAccountRemaining.GetValueOrDefault(source.Account.Id) > 0)
                .SelectMany(source => destinations.Select(destination => CandidateFor(
                    destination,
                    source,
                    Math.Min(source.RemainingCapacity, sourceAccountRemaining.GetValueOrDefault(source.Account.Id)),
                    projectionStart,
                    projectionDate,
                    cdiDailyPercent)))
                .Where(candidate => candidate != null)
                .Cast<Candidate>()
                .OrderByDescending(candidate => candidate.FutureGainAmountPerReal)
                .ThenByDescending(candidate => candidate.Advantage)
                .ThenByDescending(candidate => candidate.Source.IrPercent)
                .ThenBy(candidate => candidate.Source.AgeDays ?? int.MaxValue)
                .ThenByDescending(candidate => candidate.Net)
                .ThenBy(candidate => new[]
                {
                    candidate.AppCapacity ?? decimal.MaxValue,
                    candidate.RangeCapacity ?? decimal.MaxValue
                }.Min())
                .ThenBy(candidate => candidate.State.Account.Id)
                .ThenBy(candidate => candidate.ApplicationId ?? int.MaxValue)
                .ToList();

            Candidate? best = candidates.FirstOrDefault();
            if (best == null)
            {
                break;
            }

            decimal sourceAvailable = Math.Min(best.Source.RemainingCapacity, sourceAccountRemaining.GetValueOrDefault(best.Source.Account.Id));
            decimal amount = new[]
            {
                sourceAvailable,
                best.AppCapacity ?? sourceAvailable,
                best.RangeCapacity ?? sourceAvailable
            }.Min();

            if (amount <= 0)
            {
                break;
            }

            decimal destinationBefore = best.State.SimulatedBalance;
            decimal? appBefore = best.State.RemainingCapacity;
            decimal sourceBefore = best.Source.RemainingCapacity;
            decimal taxCost = best.Source.InitialCapacity > 0
                ? Math.Round(best.Source.EstimatedTaxCost * amount / best.Source.InitialCapacity, 2)
                : 0m;

            best.State.SimulatedBalance += amount;
            best.State.RemainingCapacity = best.State.RemainingCapacity.HasValue
                ? Math.Max(0, best.State.RemainingCapacity.Value - amount)
                : null;
            best.Source.RemainingCapacity = Math.Max(0, best.Source.RemainingCapacity - amount);
            sourceAccountRemaining[best.Source.Account.Id] = Math.Max(0, sourceAccountRemaining.GetValueOrDefault(best.Source.Account.Id) - amount);

            report.Recommendations.Add(ToDto(best, amount, destinationBefore, appBefore, sourceBefore, taxCost));
        }

        if (guard >= 5000)
        {
            throw new InvalidOperationException("A alocação da Estratégia de Investimentos não apresentou progresso.");
        }

        if (targetConfiguration?.MinimumAmount is decimal minimumAmount && minimumAmount > 0)
        {
            decimal targetTotal = report.Recommendations.Where(x => x.AccountId == targetConfiguration.AccountId).Sum(x => x.RecommendedAmount);
            if (targetTotal > 0 && targetTotal < minimumAmount)
            {
                report.Recommendations.RemoveAll(x => x.AccountId == targetConfiguration.AccountId);
                report.Warnings.Add($"A realocação encontrada para a conta alvo ficou abaixo do investimento mínimo configurado ({minimumAmount.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))}) e foi descartada.");
            }
        }

        report.RecommendedInvestment = report.Recommendations.Sum(x => x.RecommendedAmount);
        report.MainAccountOutflow = report.Recommendations.Where(x => x.SourceAccountId == main.Id).Sum(x => x.RecommendedAmount);
        report.MainAccountInflow = report.Recommendations.Where(x => x.AccountId == main.Id).Sum(x => x.RecommendedAmount);
        report.OtherAccountsRecommendedInvestment = Math.Max(0, report.RecommendedInvestment - report.MainAccountOutflow);
        report.SafeSurplusWithoutDestination = Math.Max(0, report.SafeSurplus - report.MainAccountOutflow);
        report.KeptInMainAccount = balance - report.MainAccountOutflow + report.MainAccountInflow;
        report.FinalBalance = running - report.MainAccountOutflow + report.MainAccountInflow;

        foreach (InvestmentTimelineRowDTO row in report.Timeline)
        {
            row.StrategyBalance = row.BaseBalance - report.MainAccountOutflow + report.MainAccountInflow;
            row.ReserveMargin = row.StrategyBalance - report.Reserve;
            row.IsCritical = row.Date == report.CriticalDate;
        }

        decimal minMargin = report.Timeline.Count == 0
            ? report.KeptInMainAccount - report.Reserve
            : report.Timeline.Min(row => row.ReserveMargin);

        report.Classification = report.RecommendedInvestment == 0 || minMargin < 0
            ? "Não recomendado"
            : minMargin < Math.Max(1m, report.Reserve * .10m)
                ? "Seguro com margem reduzida"
                : "Seguro";

        return report;
    }

    private static List<State> BuildDestinations(
        InvestmentStrategyReportDTO report,
        List<Accounts> accounts,
        Dictionary<int, decimal> balances,
        Dictionary<int, List<AccountsApplications>> applicationsByAccount,
        Dictionary<int, List<AccountYieldRanges>> rangesByAccount,
        InvestmentTargetConfigurationDTO? targetConfiguration)
    {
        List<State> destinations = new();

        foreach (Accounts account in accounts)
        {
            if (targetConfiguration != null && account.Id != targetConfiguration.AccountId)
            {
                continue;
            }

            decimal current = balances.GetValueOrDefault(account.Id);

            if (targetConfiguration != null)
            {
                decimal? targetCapacity = TargetCapacity(targetConfiguration);
                destinations.Add(new State(account, current, new List<AccountsApplications>(), targetConfiguration.MaximumAmount, new List<AccountYieldRanges>(), targetConfiguration, targetCapacity));
                continue;
            }
            List<AccountsApplications> apps = applicationsByAccount.GetValueOrDefault(account.Id) ?? new List<AccountsApplications>();
            List<AccountYieldRanges> ranges = rangesByAccount.GetValueOrDefault(account.Id) ?? new List<AccountYieldRanges>();
            List<decimal> limits = apps.Where(x => x.MaximumAmount.HasValue).Select(x => x.MaximumAmount!.Value).Distinct().ToList();

            if (limits.Count > 1)
            {
                decimal safeLimit = limits.Where(x => x > 0).DefaultIfEmpty().Min();
                report.Warnings.Add($"A conta {account.Name} possui limites máximos divergentes; foi usado o menor limite positivo ({safeLimit:C}).");
                limits = safeLimit > 0 ? new List<decimal> { safeLimit } : new List<decimal>();
            }

            int rateKeys = apps
                .Where(x => x.CdiPercent.HasValue || x.FixedRate.HasValue)
                .Select(x => x.CdiPercent.HasValue ? $"CDI:{Normalize(x.CdiPercent.Value):0.####}" : $"FIXED:{Normalize(x.FixedRate ?? 0):0.####}")
                .Distinct()
                .Count();

            if (rateKeys > 1 && ranges.Count == 0)
            {
                Exclude(report, account, "Condições de rendimento das aplicações ativas conflitantes para novos aportes.");
                continue;
            }

            if (apps.Count == 0 && ranges.Count == 0 && !account.YieldPercent.HasValue)
            {
                Exclude(report, account, "Rendimento do destino não configurado.");
                continue;
            }

            decimal? maximum = limits.Count == 1 ? limits[0] : null;
            destinations.Add(new State(account, current, apps, maximum, ranges, null, null));
        }

        return destinations;
    }

    private async Task<List<SourceState>> BuildSources(
        List<Accounts> accounts,
        Dictionary<int, decimal> balances,
        Dictionary<int, List<AccountsApplications>> applicationsByAccount,
        Dictionary<int, List<AccountYieldRanges>> rangesByAccount,
        int mainAccountId,
        DateTime valuationDate)
    {
        List<int> applicationIds = applicationsByAccount.Values.SelectMany(x => x).Select(x => x.Id).ToList();
        Dictionary<int, AccountsPostingApplicationDetails> latestDetails = new();

        if (applicationIds.Count > 0)
        {
            List<AccountsPostingApplicationDetails> details = await _context.AccountsPostingApplicationDetails.AsNoTracking()
                .Include(x => x.AccountPosting)
                .Where(x => applicationIds.Contains(x.AccountApplicationId) && x.AccountPosting != null && (x.AccountPosting.Type == "Y" || x.AccountPosting.Type == "y"))
                .OrderByDescending(x => x.AccountPosting!.Date)
                .ThenByDescending(x => x.AccountPostingId)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            latestDetails = details
                .GroupBy(x => x.AccountApplicationId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        DateTime today = valuationDate.Date;
        List<SourceState> sources = new();

        foreach (Accounts account in accounts)
        {
            decimal accountBalance = Math.Max(0, balances.GetValueOrDefault(account.Id));
            if (accountBalance <= 0)
            {
                continue;
            }

            List<AccountsApplications> apps = applicationsByAccount.GetValueOrDefault(account.Id) ?? new List<AccountsApplications>();
            List<AccountYieldRanges> ranges = rangesByAccount.GetValueOrDefault(account.Id) ?? new List<AccountYieldRanges>();
            decimal representedBalance = 0m;

            foreach (AccountsApplications app in apps)
            {
                Liquidation liquidation = LiquidationFor(app, latestDetails.GetValueOrDefault(app.Id), account, today);
                if (liquidation.NetBalance <= 0)
                {
                    continue;
                }

                YieldInfo yield = SourceYield(account, app, ranges, accountBalance, today);
                decimal sourceCapacity = Math.Min(liquidation.NetBalance, accountBalance);
                decimal representedRatio = liquidation.NetBalance > 0 ? sourceCapacity / liquidation.NetBalance : 0m;
                decimal sourcePrincipal = liquidation.Principal * representedRatio;
                decimal sourceGrossBalance = liquidation.GrossBalance * representedRatio;
                representedBalance += sourceCapacity;

                sources.Add(new SourceState(
                    account,
                    app,
                    sourceCapacity,
                    sourcePrincipal,
                    sourceGrossBalance,
                    yield.Gross,
                    yield.Net,
                    yield.Index,
                    yield.IrPercent,
                    liquidation.IofPercent,
                    liquidation.EstimatedTaxCost,
                    yield.AgeDays,
                    account.Id == mainAccountId));
            }

            decimal residual = Math.Max(0, accountBalance - representedBalance);
            bool canUseUntrackedBalanceAsSource = account.Id == mainAccountId || account.IsTaxExempt;

            if ((residual > 0.01m || apps.Count == 0) && canUseUntrackedBalanceAsSource)
            {
                YieldInfo yield = SourceYield(account, null, ranges, accountBalance, today);
                decimal capacity = apps.Count == 0 ? accountBalance : residual;

                sources.Add(new SourceState(
                    account,
                    null,
                    capacity,
                    capacity,
                    capacity,
                    yield.Gross,
                    yield.Net,
                    yield.Index,
                    yield.IrPercent,
                    0m,
                    0m,
                    null,
                    account.Id == mainAccountId));
            }
        }

        return sources;
    }

    private static Candidate? CandidateFor(
        State state,
        SourceState source,
        decimal sourceCapacity,
        DateTime projectionStart,
        DateTime projectionDate,
        decimal? cdiDailyPercent)
    {
        if (state.Account.Id == source.Account.Id || sourceCapacity <= 0)
        {
            return null;
        }

        if (state.TargetConfiguration?.MaturityDate.HasValue == true && state.TargetConfiguration.MaturityDate.Value.Date <= projectionStart.Date)
        {
            return null;
        }

        AccountYieldRanges? range = state.TargetConfiguration == null ? FindRange(state.Ranges, state.SimulatedBalance) : null;
        if (state.TargetConfiguration == null && state.Ranges.Count > 0 && range == null)
        {
            return null;
        }

        AccountsApplications? application = state.TargetConfiguration == null ? state.Applications.FirstOrDefault() : null;
        decimal gross = state.TargetConfiguration?.YieldPercent
            ?? range?.YieldPercent
            ?? (application?.CdiPercent is decimal cdi
                ? Normalize(cdi)
                : application?.FixedRate is decimal fixedRate
                    ? Normalize(fixedRate)
                    : state.Account.YieldPercent ?? 0);
        string index = state.TargetConfiguration != null
            ? NormalizeIndex(state.TargetConfiguration.YieldIndex)
            : range != null || application?.CdiPercent.HasValue != true && application?.FixedRate.HasValue != true
                ? NormalizeIndex(state.Account.YieldIndex)
                : application.CdiPercent.HasValue
                    ? "CDI"
                    : "PREFIXADO";
        decimal destinationIr = state.Account.IsTaxExempt ? 0m : 22.5m;
        decimal net = Net(gross, destinationIr, state.Account.IsTaxExempt);

        if (gross <= 0 || state.RemainingCapacity is 0)
        {
            return null;
        }

        Projection? keep = ProjectSource(source, sourceCapacity, projectionStart, projectionDate, cdiDailyPercent);
        Projection? transfer = ProjectDestination(state, sourceCapacity, gross, index, projectionStart, projectionDate, cdiDailyPercent);

        if (keep == null || transfer == null)
        {
            return null;
        }

        decimal futureGain = transfer.NetValue - keep.NetValue;
        if (futureGain <= 0.01m)
        {
            return null;
        }

        decimal futureGainPercent = keep.NetValue > 0
            ? futureGain / keep.NetValue * 100m
            : 0m;
        decimal? rangeCapacity = state.TargetConfiguration == null && range?.EndAmount is decimal end
            ? Math.Max(0, end - state.SimulatedBalance)
            : null;

        return new Candidate(
            state,
            source,
            application?.Id,
            range,
            gross,
            net,
            futureGainPercent,
            state.RemainingCapacity,
            rangeCapacity,
            index,
            destinationIr,
            keep.NetValue / sourceCapacity,
            transfer.NetValue / sourceCapacity,
            futureGain / sourceCapacity,
            keep.IrPercent,
            transfer.IrPercent,
            projectionDate);
    }

    private static InvestmentRecommendationDTO ToDto(
        Candidate candidate,
        decimal amount,
        decimal destinationBefore,
        decimal? applicationBefore,
        decimal sourceBefore,
        decimal sourceEstimatedTaxCost)
    {
        return new InvestmentRecommendationDTO
        {
            SourceAccountId = candidate.Source.Account.Id,
            SourceApplicationId = candidate.Source.Application?.Id,
            SourceAccountName = candidate.Source.Account.Name,
            SourceDateApplied = candidate.Source.Application?.DateApplied,
            SourceAgeDays = candidate.Source.AgeDays,
            SourceIrPercent = candidate.Source.IrPercent,
            SourceIofPercent = candidate.Source.IofPercent,
            SourceEstimatedTaxCost = sourceEstimatedTaxCost,
            SourceBalanceBefore = sourceBefore,
            SourceBalanceAfter = candidate.Source.RemainingCapacity,
            IsMainAccountSource = candidate.Source.IsMainAccount,
            AccountId = candidate.State.Account.Id,
            ApplicationId = candidate.ApplicationId,
            AccountName = candidate.State.Account.Name,
            CurrentBalance = candidate.State.InitialBalance,
            Capacity = applicationBefore,
            RecommendedAmount = amount,
            YieldPercent = candidate.Gross,
            MainAccountYieldPercent = candidate.Source.Gross,
            AdvantagePercent = candidate.Advantage,
            ApplicationCapacity = applicationBefore,
            RangeCapacity = candidate.RangeCapacity,
            RangeStart = candidate.Range?.StartAmount ?? 0,
            RangeEnd = candidate.Range?.EndAmount,
            DestinationGrossYield = candidate.Gross,
            DestinationNetYield = candidate.Net,
            SourceGrossYield = candidate.Source.Gross,
            SourceNetYield = candidate.Source.Net,
            CapacityAfter = candidate.State.RemainingCapacity,
            DestinationBalanceBefore = destinationBefore,
            DestinationBalanceAfter = candidate.State.SimulatedBalance,
            MaximumAmount = candidate.State.Maximum,
            OccupiedAmount = candidate.State.Occupied,
            ApplicationCapacityBefore = applicationBefore,
            ApplicationCapacityAfter = candidate.State.RemainingCapacity,
            RangeId = candidate.Range?.Id,
            RangeCapacityBefore = candidate.RangeCapacity,
            RangeCapacityAfter = candidate.Range?.EndAmount is decimal end ? Math.Max(0, end - candidate.State.SimulatedBalance) : null,
            DestinationYieldIndex = candidate.Index,
            SourceYieldIndex = candidate.Source.Index,
            EvaluationDate = candidate.EvaluationDate,
            ProjectedKeepValue = Math.Round(candidate.FutureKeepFactor * amount, 2),
            ProjectedTransferValue = Math.Round(candidate.FutureTransferFactor * amount, 2),
            ProjectedFutureGain = Math.Round(candidate.FutureGainAmountPerReal * amount, 2),
            ProjectedFutureGainPercent = candidate.Advantage,
            SourceIrPercentAtEvaluation = candidate.SourceIrPercentAtEvaluation,
            DestinationIrPercentAtEvaluation = candidate.DestinationIrPercentAtEvaluation,
            CapacityBasis = CapacityBasis(candidate),
            Reason = Reason(candidate, amount),
            IsDestinationTaxExempt = candidate.State.Account.IsTaxExempt,
            DestinationIrPercent = candidate.DestinationIrPercent
        };
    }

    private static YieldInfo SourceYield(Accounts account, AccountsApplications? application, List<AccountYieldRanges> ranges, decimal balance, DateTime today)
    {
        AccountYieldRanges? range = FindRange(ranges, balance);
        decimal gross = application?.CdiPercent is decimal cdi
            ? Normalize(cdi)
            : application?.FixedRate is decimal fixedRate
                ? Normalize(fixedRate)
                : range?.YieldPercent ?? account.YieldPercent ?? 0m;
        string index = application?.CdiPercent.HasValue == true
            ? "CDI"
            : application?.FixedRate.HasValue == true
                ? "PREFIXADO"
                : NormalizeIndex(account.YieldIndex);
        int? ageDays = application == null ? null : Math.Max(0, (today - application.DateApplied.Date).Days);
        decimal irPercent = account.IsTaxExempt
            ? 0m
            : application == null
                ? Math.Max(0, account.IrPercent ?? 22.5m)
                : IrPercent(ageDays ?? 0);
        decimal net = Net(gross, irPercent, account.IsTaxExempt);

        return new YieldInfo(gross, net, index, irPercent, ageDays);
    }

    private static Liquidation LiquidationFor(AccountsApplications application, AccountsPostingApplicationDetails? detail, Accounts account, DateTime today)
    {
        decimal principal = Math.Max(0, application.AmountApplied);
        decimal grossBalance = Math.Max(0, detail?.TotalGrossBalance ?? principal);
        int ageDays = Math.Max(0, (today - application.DateApplied.Date).Days);
        decimal iofRate = account.IsTaxExempt ? 0m : IofRate(ageDays);
        decimal irPercent = account.IsTaxExempt ? 0m : IrPercent(ageDays);
        decimal grossYield = Math.Max(0, grossBalance - principal);
        decimal iof = Math.Round(grossYield * iofRate, 2);
        decimal irBase = Math.Max(0, grossYield - iof);
        decimal ir = account.IsTaxExempt ? 0m : Math.Round(irBase * irPercent / 100m, 2);
        decimal netBalance = Math.Max(0, grossBalance - iof - ir);

        return new Liquidation(netBalance, grossBalance, principal, iofRate * 100m, iof + ir);
    }

    private static decimal IrPercent(int ageDays)
    {
        if (ageDays <= 180) return 22.5m;
        if (ageDays <= 360) return 20m;
        if (ageDays <= 720) return 17.5m;
        return 15m;
    }

    private static decimal IofRate(int ageDays)
    {
        decimal[] rates =
        {
            .96m, .93m, .90m, .86m, .83m, .80m, .76m, .73m, .70m, .66m,
            .63m, .60m, .56m, .53m, .50m, .46m, .43m, .40m, .36m, .33m,
            .30m, .26m, .23m, .20m, .16m, .13m, .10m, .06m, .03m
        };

        if (ageDays <= 0 || ageDays >= 30)
        {
            return 0m;
        }

        return rates[Math.Min(ageDays, 29) - 1];
    }

    private static DateTime CurrentBrazilDate()
    {
        string[] timeZoneIds = OperatingSystem.IsWindows()
            ? new[] { "E. South America Standard Time", "America/Sao_Paulo" }
            : new[] { "America/Sao_Paulo", "E. South America Standard Time" };

        foreach (string timeZoneId in timeZoneIds)
        {
            try
            {
                TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone).Date;
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return DateTime.Now.Date;
    }

    private async Task<decimal?> GetLatestCdiDailyPercent()
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        try
        {
            const string url = "https://api.bcb.gov.br/dados/serie/bcdata.sgs.12/dados/ultimos/1?formato=json";
            using HttpResponseMessage response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement item = root[0];
            if (!item.TryGetProperty("valor", out JsonElement valueElement))
            {
                return null;
            }

            string? valueText = valueElement.GetString();
            return decimal.TryParse(valueText?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
                ? value
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static Projection? ProjectSource(
        SourceState source,
        decimal amount,
        DateTime projectionStart,
        DateTime projectionDate,
        decimal? cdiDailyPercent)
    {
        if (amount <= 0 || source.InitialCapacity <= 0)
        {
            return null;
        }

        decimal ratio = amount / source.InitialCapacity;

        if (source.Application == null)
        {
            decimal? futureGross = FutureGross(amount, source.Gross, source.Index, projectionStart, projectionDate, cdiDailyPercent);
            if (!futureGross.HasValue)
            {
                return null;
            }

            decimal futureYield = Math.Max(0, futureGross.Value - amount);
            decimal irPercent = source.Account.IsTaxExempt ? 0m : Math.Max(0, source.IrPercent);
            decimal projectedIr = source.Account.IsTaxExempt ? 0m : futureYield * irPercent / 100m;

            return new Projection(Math.Max(0, futureGross.Value - projectedIr), irPercent);
        }

        decimal grossBalance = source.GrossBalance * ratio;
        decimal principal = source.Principal * ratio;
        decimal? projectedGross = FutureGross(grossBalance, source.Gross, source.Index, projectionStart, projectionDate, cdiDailyPercent);
        if (!projectedGross.HasValue)
        {
            return null;
        }

        int futureAgeDays = Math.Max(0, (source.AgeDays ?? 0) + (projectionDate.Date - projectionStart.Date).Days);
        decimal iofRate = source.Account.IsTaxExempt ? 0m : IofRate(futureAgeDays);
        decimal irPercentAtEvaluation = source.Account.IsTaxExempt ? 0m : IrPercent(futureAgeDays);
        decimal accumulatedGrossYield = Math.Max(0, projectedGross.Value - principal);
        decimal iof = accumulatedGrossYield * iofRate;
        decimal irBase = Math.Max(0, accumulatedGrossYield - iof);
        decimal ir = source.Account.IsTaxExempt ? 0m : irBase * irPercentAtEvaluation / 100m;

        return new Projection(Math.Max(0, projectedGross.Value - iof - ir), irPercentAtEvaluation);
    }

    private static Projection? ProjectDestination(
        State state,
        decimal amount,
        decimal gross,
        string index,
        DateTime projectionStart,
        DateTime projectionDate,
        decimal? cdiDailyPercent)
    {
        InvestmentTargetConfigurationDTO? target = state.TargetConfiguration;
        if (target == null || !target.MaturityDate.HasValue || projectionDate.Date <= target.MaturityDate.Value.Date)
        {
            decimal? futureGross = FutureGross(amount, gross, index, projectionStart, projectionDate, cdiDailyPercent);
            return futureGross.HasValue
                ? ApplyTaxes(state.Account, amount, futureGross.Value, Math.Max(0, (projectionDate.Date - projectionStart.Date).Days))
                : null;
        }

        DateTime maturityDate = target.MaturityDate.Value.Date;
        if (maturityDate <= projectionStart.Date)
        {
            return null;
        }

        decimal? firstStageGross = FutureGross(amount, gross, index, projectionStart, maturityDate, cdiDailyPercent);
        if (!firstStageGross.HasValue)
        {
            return null;
        }

        int firstStageAgeDays = Math.Max(0, (maturityDate - projectionStart.Date).Days);

        if (target.PostMaturityRestartsTaxClock)
        {
            Projection firstStage = ApplyTaxes(state.Account, amount, firstStageGross.Value, firstStageAgeDays);
            if (!target.PostMaturityYieldPercent.HasValue || target.PostMaturityYieldPercent.Value <= 0)
            {
                return new Projection(firstStage.NetValue, 0m);
            }

            string postIndex = NormalizeIndex(target.PostMaturityYieldIndex);
            decimal? secondStageGross = FutureGross(firstStage.NetValue, target.PostMaturityYieldPercent.Value, postIndex, maturityDate, projectionDate, cdiDailyPercent);
            if (!secondStageGross.HasValue)
            {
                return null;
            }

            int secondStageAgeDays = Math.Max(0, (projectionDate.Date - maturityDate).Days);
            return ApplyTaxes(state.Account, firstStage.NetValue, secondStageGross.Value, secondStageAgeDays);
        }

        decimal finalGross = firstStageGross.Value;
        if (target.PostMaturityYieldPercent.HasValue && target.PostMaturityYieldPercent.Value > 0)
        {
            string postIndex = NormalizeIndex(target.PostMaturityYieldIndex);
            decimal? secondStageGross = FutureGross(finalGross, target.PostMaturityYieldPercent.Value, postIndex, maturityDate, projectionDate, cdiDailyPercent);
            if (!secondStageGross.HasValue)
            {
                return null;
            }

            finalGross = secondStageGross.Value;
        }

        int totalAgeDays = Math.Max(0, (projectionDate.Date - projectionStart.Date).Days);
        return ApplyTaxes(state.Account, amount, finalGross, totalAgeDays);
    }

    private static Projection ApplyTaxes(Accounts account, decimal principal, decimal grossBalance, int ageDays)
    {
        decimal iofRate = account.IsTaxExempt ? 0m : IofRate(ageDays);
        decimal irPercent = account.IsTaxExempt ? 0m : IrPercent(ageDays);
        decimal grossYield = Math.Max(0, grossBalance - principal);
        decimal iof = grossYield * iofRate;
        decimal irBase = Math.Max(0, grossYield - iof);
        decimal ir = account.IsTaxExempt ? 0m : irBase * irPercent / 100m;

        return new Projection(Math.Max(0, grossBalance - iof - ir), irPercent);
    }

    private static decimal? FutureGross(
        decimal amount,
        decimal grossPercent,
        string index,
        DateTime projectionStart,
        DateTime projectionDate,
        decimal? cdiDailyPercent)
    {
        if (amount <= 0 || projectionDate.Date <= projectionStart.Date)
        {
            return amount;
        }

        if (index == "CDI")
        {
            if (!cdiDailyPercent.HasValue)
            {
                return null;
            }

            int businessDays = BusinessDaysBetween(projectionStart, projectionDate);
            decimal dailyRate = cdiDailyPercent.Value / 100m * grossPercent / 100m;
            double factor = Math.Pow(1d + (double)dailyRate, businessDays);

            return amount * (decimal)factor;
        }

        if (index == "PREFIXADO")
        {
            int calendarDays = Math.Max(0, (projectionDate.Date - projectionStart.Date).Days);
            decimal annualRate = grossPercent / 100m;
            double dailyFactor = Math.Pow(1d + (double)annualRate, 1d / 365d);
            double factor = Math.Pow(dailyFactor, calendarDays);

            return amount * (decimal)factor;
        }

        return null;
    }

    private static int BusinessDaysBetween(DateTime start, DateTime end)
    {
        int businessDays = 0;

        for (DateTime date = start.Date.AddDays(1); date <= end.Date; date = date.AddDays(1))
        {
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
            {
                businessDays++;
            }
        }

        return businessDays;
    }

    private static InvestmentTargetConfigurationDTO? NormalizeTargetConfiguration(InvestmentTargetConfigurationDTO? configuration)
    {
        if (configuration == null || configuration.AccountId <= 0)
        {
            return null;
        }

        if (configuration.YieldPercent <= 0)
        {
            throw new ArgumentException("A rentabilidade inicial da conta alvo deve ser maior que zero.");
        }

        if (configuration.MinimumAmount < 0 || configuration.MaximumAmount < 0 || configuration.AvailableAmount < 0 || configuration.PostMaturityYieldPercent < 0)
        {
            throw new ArgumentException("Os valores configurados para a conta alvo não podem ser negativos.");
        }

        if (configuration.MinimumAmount.HasValue && configuration.MaximumAmount.HasValue && configuration.MinimumAmount.Value > configuration.MaximumAmount.Value)
        {
            throw new ArgumentException("O investimento mínimo da conta alvo não pode ser maior que o máximo.");
        }

        configuration.YieldIndex = NormalizeIndex(configuration.YieldIndex);
        configuration.PostMaturityYieldIndex = NormalizeIndex(configuration.PostMaturityYieldIndex);
        configuration.MaturityDate = configuration.MaturityDate?.Date;

        return configuration;
    }

    private static decimal? TargetCapacity(InvestmentTargetConfigurationDTO configuration)
    {
        List<decimal> limits = new();
        if (configuration.MaximumAmount.HasValue)
        {
            limits.Add(configuration.MaximumAmount.Value);
        }

        if (configuration.AvailableAmount.HasValue)
        {
            limits.Add(configuration.AvailableAmount.Value);
        }

        return limits.Count == 0 ? null : Math.Max(0, limits.Min());
    }

    private static string CapacityBasis(Candidate candidate)
    {
        if (candidate.State.TargetConfiguration != null)
        {
            return "Condições informadas manualmente para a conta alvo; capacidade limitada pelo menor valor entre máximo e disponibilidade configurados.";
        }

        if (candidate.State.Maximum.HasValue && candidate.Range?.EndAmount.HasValue == true)
        {
            return "Menor capacidade entre o limite máximo compartilhado e a faixa vigente.";
        }

        if (candidate.State.Maximum.HasValue)
        {
            return "Limite máximo compartilhado menos a soma dos aportes ativos.";
        }

        if (candidate.Range?.EndAmount.HasValue == true)
        {
            return "Capacidade da faixa de rendimento vigente.";
        }

        return "Sem limite máximo ou superior de faixa; limitado pela capacidade disponível da origem.";
    }

    private static string Reason(Candidate candidate, decimal amount)
    {
        string destinationLimitation = candidate.State.TargetConfiguration != null
            ? "O bloco foi limitado pelas condições configuradas para a conta alvo e pela capacidade disponível da origem."
            : candidate.RangeCapacity.HasValue && candidate.AppCapacity.HasValue && candidate.RangeCapacity.Value <= candidate.AppCapacity.Value
                ? "O bloco foi limitado pelo final da faixa vigente."
                : candidate.AppCapacity.HasValue
                    ? "O bloco foi limitado pelo limite máximo compartilhado."
                    : candidate.Source.IsMainAccount
                        ? "O bloco foi limitado pelo excedente seguro da conta principal."
                        : "O bloco foi limitado pelo saldo líquido estimado disponível na origem.";
        string age = candidate.Source.AgeDays.HasValue
            ? $" aplicação com {candidate.Source.AgeDays.Value} dias e IR atual de {candidate.Source.IrPercent:0.##}%"
            : $" IR considerado de {candidate.Source.IrPercent:0.##}%";
        decimal keepValue = Math.Round(candidate.FutureKeepFactor * amount, 2);
        decimal transferValue = Math.Round(candidate.FutureTransferFactor * amount, 2);
        decimal futureGain = Math.Round(transferValue - keepValue, 2);
        CultureInfo ptBr = CultureInfo.GetCultureInfo("pt-BR");
        string targetRule = string.Empty;
        InvestmentTargetConfigurationDTO? target = candidate.State.TargetConfiguration;
        if (target != null)
        {
            string maturity = target.MaturityDate.HasValue ? $" até {target.MaturityDate.Value:dd/MM/yyyy}" : string.Empty;
            string afterMaturity = target.PostMaturityYieldPercent.HasValue && target.PostMaturityYieldPercent.Value > 0
                ? $"; após o vencimento, {target.PostMaturityYieldPercent.Value:0.####}% de {NormalizeIndex(target.PostMaturityYieldIndex)}{(target.PostMaturityRestartsTaxClock ? " com reinício da contagem tributária" : string.Empty)}"
                : target.MaturityDate.HasValue
                    ? "; após o vencimento, rendimento não informado e valor considerado sem novos rendimentos"
                    : string.Empty;
            targetRule = $" Condição alvo: {target.YieldPercent:0.####}% de {NormalizeIndex(target.YieldIndex)}{maturity}{afterMaturity}.";
        }

        return $"Origem {candidate.Source.Account.Name}:{age}; rendimento de referência {candidate.Source.Gross:0.####}% ({candidate.Source.Index}). " +
               $"Destino {candidate.State.Account.Name}: {candidate.Gross:0.####}% ({candidate.Index}). " +
               $"Em {candidate.EvaluationDate:dd/MM/yyyy}, manter projeta {keepValue.ToString("C", ptBr)} líquidos e transferir projeta {transferValue.ToString("C", ptBr)}, ganho líquido estimado de {futureGain.ToString("C", ptBr)} ({candidate.Advantage:0.####}%). " +
               $"IR estimado nessa data: origem {candidate.SourceIrPercentAtEvaluation:0.##}% e novo aporte {candidate.DestinationIrPercentAtEvaluation:0.##}%.{targetRule} {destinationLimitation}";
    }

    private static AccountYieldRanges? FindRange(List<AccountYieldRanges> ranges, decimal balance)
    {
        return ranges.FirstOrDefault(range => range.StartAmount <= balance && (!range.EndAmount.HasValue || balance < range.EndAmount.Value));
    }

    private static decimal Normalize(decimal value)
    {
        return value <= 2 ? value * 100 : value;
    }

    private static string NormalizeIndex(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "CDI" : value.Trim().ToUpperInvariant();
    }

    private static decimal Net(decimal gross, decimal irPercent, bool taxExempt)
    {
        return taxExempt ? gross : gross * (1 - Math.Max(0, irPercent) / 100m);
    }

    private static void Exclude(InvestmentStrategyReportDTO report, Accounts account, string reason)
    {
        report.Exclusions.Add(new InvestmentExclusionDTO { AccountName = account.Name, Reason = reason });
    }

    private sealed record State(Accounts Account, decimal InitialBalance, List<AccountsApplications> Applications, decimal? Maximum, List<AccountYieldRanges> Ranges, InvestmentTargetConfigurationDTO? TargetConfiguration, decimal? ManualCapacity)
    {
        public decimal SimulatedBalance { get; set; } = InitialBalance;
        public decimal Occupied => TargetConfiguration == null ? Applications.Sum(application => application.AmountApplied) : 0m;
        public decimal? RemainingCapacity { get; set; } = ManualCapacity.HasValue
            ? Math.Max(0, ManualCapacity.Value)
            : Maximum.HasValue
                ? Math.Max(0, Maximum.Value - Applications.Sum(application => application.AmountApplied))
                : null;
    }

    private sealed record SourceState(
        Accounts Account,
        AccountsApplications? Application,
        decimal InitialCapacity,
        decimal Principal,
        decimal GrossBalance,
        decimal Gross,
        decimal Net,
        string Index,
        decimal IrPercent,
        decimal IofPercent,
        decimal EstimatedTaxCost,
        int? AgeDays,
        bool IsMainAccount)
    {
        public decimal RemainingCapacity { get; set; } = InitialCapacity;
    }

    private sealed record Candidate(
        State State,
        SourceState Source,
        int? ApplicationId,
        AccountYieldRanges? Range,
        decimal Gross,
        decimal Net,
        decimal Advantage,
        decimal? AppCapacity,
        decimal? RangeCapacity,
        string Index,
        decimal DestinationIrPercent,
        decimal FutureKeepFactor,
        decimal FutureTransferFactor,
        decimal FutureGainAmountPerReal,
        decimal SourceIrPercentAtEvaluation,
        decimal DestinationIrPercentAtEvaluation,
        DateTime EvaluationDate);

    private sealed record YieldInfo(decimal Gross, decimal Net, string Index, decimal IrPercent, int? AgeDays);
    private sealed record Projection(decimal NetValue, decimal IrPercent);
    private sealed record Liquidation(decimal NetBalance, decimal GrossBalance, decimal Principal, decimal IofPercent, decimal EstimatedTaxCost);
    private sealed record Move(DateTime Date, decimal Amount, bool Income);
}
