using System.Globalization;
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

    public InvestmentStrategyService(BudgetContext context, IHttpContextAccessor accessor)
    {
        _context = context;
        _user    = accessor.HttpContext?.Items["User"] as Users ?? new Users();
    }

    public async Task<InvestmentStrategyReportDTO> GetReport(InvestmentStrategyRequestDTO request)
    {
        if (request.InitialDate.Date > request.FinalDate.Date)
        {
            throw new ArgumentException("Initial date cannot be greater than final date.");
        }

        var main = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.AccountId &&
                                      x.UserId == _user.Id &&
                                      x.Disabled != true)
            ?? throw new InvalidOperationException(
                "Main account was not found or is disabled.");

        var balance = await _context.AccountsPostings
            .Where(x => x.AccountId == main.Id)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        DateTime historyEnd      = DateTime.Today;
        DateTime historyStart    = historyEnd.AddDays(-89);
        List<Move> paidRows      = await _context.Expenses.AsNoTracking().Where(x => x.UserId == _user.Id && x.DueDate >= historyStart && x.DueDate <= historyEnd && x.Paid != 0).Select(x => new Move(x.DueDate!.Value.Date, Math.Abs(x.Paid), false)).ToListAsync();
        decimal historicalPaid   = paidRows.Sum(x => x.Amount);
        DateTime historicalStart = paidRows.Count == 0 ? historyStart : paidRows.Min(x => x.Date);
        DateTime historicalEnd   = paidRows.Count == 0 ? historyEnd : paidRows.Max(x => x.Date);
        int historicalDays       = paidRows.Count == 0 ? 0 : Math.Max(1, (historyEnd - historyStart).Days + 1);
        decimal average          = historicalDays == 0 ? 0 : historicalPaid / historicalDays;

        List<Move> expenses        = await _context.Expenses.AsNoTracking().Where(x => x.UserId == _user.Id && x.DueDate <= request.FinalDate && x.ToPay - Math.Abs(x.Paid) != 0 && x.DueDate >= request.InitialDate).Select(x => new Move(x.DueDate!.Value.Date, x.ToPay - Math.Abs(x.Paid), false)).ToListAsync();
        List<Move> overdueExpenses = await _context.Expenses.AsNoTracking().Where(x => x.UserId == _user.Id && x.DueDate < request.InitialDate && x.ToPay - Math.Abs(x.Paid) != 0).Select(x => new Move(request.InitialDate.Date, x.ToPay - Math.Abs(x.Paid), false)).ToListAsync(); expenses.AddRange(overdueExpenses);
        List<Move> incomes         = await _context.Incomes.AsNoTracking().Where(x => x.UserId == _user.Id && x.ReceiptDate >= request.InitialDate && x.ReceiptDate <= request.FinalDate && x.ToReceive - x.Received != 0).Select(x => new Move(x.ReceiptDate!.Value.Date, x.ToReceive - x.Received, true)).ToListAsync();

        int overdueIncomeCount = await _context.Incomes.CountAsync(x => x.UserId == _user.Id && x.ReceiptDate < request.InitialDate && x.ToReceive - x.Received != 0);

        InvestmentStrategyReportDTO report = new InvestmentStrategyReportDTO { CurrentBalance = balance, TotalIncome = incomes.Sum(x => x.Amount), TotalExpense = expenses.Sum(x => x.Amount), HistoricalPaidAmount = historicalPaid, HistoricalDays = historicalDays, HistoricalStartDate = historicalStart, HistoricalEndDate = historicalEnd, HistoricalDailyExpenseAverage = Math.Round(average, 2), ReserveCoverageDays = 7, SuggestedReserve = historicalDays > 0 ? Math.Round(average * 7, 2) : Math.Round(expenses.Sum(x => x.Amount) * .10m, 2) };

        report.Reserve            = Math.Max(0, request.OperationalReserve ?? report.SuggestedReserve);
        report.ReserveExplanation = historicalDays > 0 ? $"Reserva baseada na média diária de {average.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))} durante {historicalDays} dias, com 7 dias de cobertura." : "Não há histórico pago suficiente. Foi utilizado o fallback de 10% das despesas pendentes.";

        if (overdueIncomeCount > 0)
            report.Warnings.Add("Existem receitas vencidas ainda não recebidas. Elas não foram consideradas como disponíveis na estratégia.");

        decimal running = balance;

        foreach (var day in incomes.Concat(expenses).GroupBy(x => x.Date).OrderBy(x => x.Key))
        {
            decimal income  = day.Where(x => x.Income).Sum(x => x.Amount);
            decimal expense = day.Where(x => !x.Income).Sum(x => x.Amount);

            running += income - expense;

            report.Timeline.Add(new() { Date = day.Key, Income = income, Expense = expense, BaseBalance = running });
        }

        report.FinalBalance = running;

        InvestmentTimelineRowDTO? critical = report.Timeline.OrderBy(x => x.BaseBalance).FirstOrDefault();

        report.LowestBalance = critical?.BaseBalance ?? balance;
        report.CriticalDate  = critical?.Date;
        report.SafeSurplus   = Math.Max(0, report.LowestBalance - report.Reserve);

        List<AccountYieldRanges> mainRanges = await Ranges(main.Id);
        List<State> destinations            = new List<State>();
        List<Accounts> accounts             = await _context.Accounts.AsNoTracking()
                                                                     .Where(x => x.UserId == _user.Id && x.Id != main.Id && x.Disabled != true)
                                                                     .ToListAsync();

        foreach (Accounts? account in accounts)
        {
            decimal current                 = await _context.AccountsPostings.Where(x => x.AccountId == account.Id).SumAsync(x => (decimal?)x.Amount) ?? 0m;
            List<AccountsApplications> apps = await _context.AccountsApplications.AsNoTracking().Where(x => x.AccountId == account.Id && !x.Disabled).ToListAsync();
            List<AccountYieldRanges> ranges = await Ranges(account.Id);
            List<decimal> limits            = apps.Where(x => x.MaximumAmount.HasValue).Select(x => x.MaximumAmount!.Value).Distinct().ToList();

            if (limits.Count > 1)
            {
                decimal safeLimit = limits.Where(x => x > 0).DefaultIfEmpty().Min();
               
                report.Warnings.Add($"A conta {account.Name} possui limites máximos divergentes; foi usado o menor limite positivo ({safeLimit:C}).");
                
                limits = safeLimit > 0 ? new List<decimal> { safeLimit } : new List<decimal>();
            }

            int rateKeys = apps.Where(x => x.CdiPercent.HasValue || x.FixedRate.HasValue).Select(x => x.CdiPercent.HasValue ? $"CDI:{Normalize(x.CdiPercent.Value):0.####}" : $"FIXED:{x.FixedRate:0.####}").Distinct().Count();

            if (rateKeys > 1 && ranges.Count == 0)
            {
                Exclude(report, account, "Condições de rendimento das aplicações ativas conflitantes.");
                continue;
            }

            if (apps.Count == 0 && ranges.Count == 0 && !account.YieldPercent.HasValue)
            {
                Exclude(report, account, "Rendimento do destino não configurado.");
                continue;
            }

            decimal? maximum = limits.Count == 1 ? limits[0] : null;
            
            destinations.Add(new(account, current, apps, maximum, ranges));
        }

        decimal remaining     = report.SafeSurplus;
        decimal sourceBalance = balance;
        int guard             = 0;
       
        while (remaining > 0 && guard++ < 1000)
        {
            AccountYieldRanges? sourceRange = FindRange(mainRanges, sourceBalance);
            
            if (mainRanges.Count > 0 && sourceRange == null)
            {
                const string limitation = "O saldo da conta principal não pertence a nenhuma faixa de rendimento configurada.";
               
                if (!report.Limitations.Contains(limitation))
                {
                    report.Limitations.Add(limitation);
                }

                break;
            }

            decimal sourceGross = sourceRange?.YieldPercent ?? main.YieldPercent ?? 0m;
            decimal sourceNet = Net(sourceGross, main);
            decimal sourceCapacity = Math.Max(0, sourceBalance - report.Reserve);
            List<Candidate> candidates = destinations
                .Select(destination => CandidateFor(
                    destination,
                    main,
                    sourceCapacity,
                    sourceGross,
                    sourceNet))
                .Where(candidate => candidate != null)
                .Cast<Candidate>()
                .OrderByDescending(candidate => candidate.Advantage)
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
                break;

            decimal amount = new[]
            {
                remaining,
                sourceCapacity,
                best.AppCapacity ?? remaining,
                best.RangeCapacity ?? remaining
            }.Min();
           
            if (amount <= 0) 
                break;

            decimal before     = best.State.SimulatedBalance;
            decimal? appBefore = best.State.RemainingCapacity;
            
            best.State.SimulatedBalance += amount;
            best.State.RemainingCapacity = best.State.RemainingCapacity.HasValue
                ? best.State.RemainingCapacity - amount
                : null;
            
            sourceBalance -= amount;
            remaining -= amount;
           
            report.Recommendations.Add(ToDto(best, amount, before, appBefore));
        }

        if (remaining > 0 && guard >= 1000) 
            throw new InvalidOperationException("A alocação da Estratégia de Investimentos não apresentou progresso.");
        
        report.RecommendedInvestment = report.Recommendations.Sum(
            recommendation => recommendation.RecommendedAmount);
        report.SafeSurplusWithoutDestination = Math.Max(
            0,
            report.SafeSurplus - report.RecommendedInvestment);
        report.KeptInMainAccount = balance - report.RecommendedInvestment;
        report.FinalBalance -= report.RecommendedInvestment;

        foreach (var row in report.Timeline)
        {
            row.StrategyBalance = row.BaseBalance - report.RecommendedInvestment;
            row.ReserveMargin = row.StrategyBalance - report.Reserve;
            row.IsCritical = row.Date == report.CriticalDate;
        }

        decimal minMargin = report.Timeline.Count == 0
            ? balance - report.RecommendedInvestment - report.Reserve
            : report.Timeline.Min(row => row.ReserveMargin);

        report.Classification = report.RecommendedInvestment == 0 || minMargin < 0
            ? "Não recomendado"
            : minMargin < Math.Max(1m, report.Reserve * .10m)
                ? "Seguro com margem reduzida"
                : "Seguro";

        return report;
    }

    private async Task<List<AccountYieldRanges>> Ranges(int id)
    {
        return await _context.AccountYieldRanges
            .AsNoTracking()
            .Where(x => x.AccountId == id)
            .OrderBy(x => x.StartAmount)
            .ToListAsync();
    }

    private static Candidate? CandidateFor(
        State state,
        Accounts source,
        decimal sourceCapacity,
        decimal sourceGross,
        decimal sourceNet)
    {
        AccountYieldRanges? range = FindRange(
            state.Ranges,
            state.SimulatedBalance);

        if (state.Ranges.Count > 0 && range == null)
        {
            return null;
        }

        AccountsApplications? application = state.Applications.FirstOrDefault();
        decimal gross = range?.YieldPercent
            ?? (application?.CdiPercent is decimal cdi
                ? Normalize(cdi)
                : application?.FixedRate is decimal fixedRate
                    ? Normalize(fixedRate)
                    : state.Account.YieldPercent ?? 0);
        string index = range != null ||
                       application?.CdiPercent.HasValue != true &&
                       application?.FixedRate.HasValue != true
            ? NormalizeIndex(state.Account.YieldIndex)
            : application.CdiPercent.HasValue
                ? "CDI"
                : "PREFIXADO";
        string sourceIndex = sourceGross == 0
            ? "SEM RENDIMENTO"
            : NormalizeIndex(source.YieldIndex);
        decimal net = Net(gross, state.Account);

        if (gross <= 0 || net <= sourceNet || sourceCapacity <= 0 ||
            state.RemainingCapacity is 0)
        {
            return null;
        }

        decimal? rangeCapacity = range?.EndAmount is decimal end
            ? Math.Max(0, end - state.SimulatedBalance)
            : null;

        return new Candidate(
            state,
            application?.Id,
            range,
            gross,
            net,
            sourceGross,
            sourceNet,
            net - sourceNet,
            state.RemainingCapacity,
            rangeCapacity,
            index,
            sourceIndex);
    }

    private static InvestmentRecommendationDTO ToDto(
        Candidate candidate,
        decimal amount,
        decimal before,
        decimal? applicationBefore)
    {
        return new InvestmentRecommendationDTO
        {
            AccountId = candidate.State.Account.Id,
            ApplicationId = candidate.ApplicationId,
            AccountName = candidate.State.Account.Name,
            CurrentBalance = candidate.State.InitialBalance,
            Capacity = applicationBefore,
            RecommendedAmount = amount,
            YieldPercent = candidate.Gross,
            MainAccountYieldPercent = candidate.SourceGross,
            AdvantagePercent = candidate.Advantage,
            ApplicationCapacity = applicationBefore,
            RangeCapacity = candidate.RangeCapacity,
            RangeStart = candidate.Range?.StartAmount ?? 0,
            RangeEnd = candidate.Range?.EndAmount,
            DestinationGrossYield = candidate.Gross,
            DestinationNetYield = candidate.Net,
            SourceGrossYield = candidate.SourceGross,
            SourceNetYield = candidate.SourceNet,
            CapacityAfter = candidate.State.RemainingCapacity,
            DestinationBalanceBefore = before,
            DestinationBalanceAfter = candidate.State.SimulatedBalance,
            MaximumAmount = candidate.State.Maximum,
            OccupiedAmount = candidate.State.Occupied,
            ApplicationCapacityBefore = applicationBefore,
            ApplicationCapacityAfter = candidate.State.RemainingCapacity,
            RangeId = candidate.Range?.Id,
            RangeCapacityBefore = candidate.RangeCapacity,
            RangeCapacityAfter = candidate.Range?.EndAmount is decimal end
                ? Math.Max(0, end - candidate.State.SimulatedBalance)
                : null,
            DestinationYieldIndex = candidate.Index,
            SourceYieldIndex = candidate.SourceIndex,
            CapacityBasis = CapacityBasis(candidate),
            Reason = Reason(candidate, amount),
            IsDestinationTaxExempt = candidate.State.Account.IsTaxExempt,
            DestinationIrPercent = candidate.State.Account.IrPercent ?? 0m
        };
    }

    private static string CapacityBasis(Candidate candidate)
    {
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

        return "Sem limite máximo ou superior de faixa; limitado pelo excedente seguro e pela capacidade da origem.";
    }

    private static string Reason(Candidate candidate, decimal amount)
    {
        string limitation = candidate.RangeCapacity.HasValue && candidate.AppCapacity.HasValue &&
                             candidate.RangeCapacity.Value <= candidate.AppCapacity.Value
            ? "O bloco foi limitado pelo final da faixa vigente."
            : candidate.AppCapacity.HasValue
                ? "O bloco foi limitado pelo limite máximo compartilhado."
                : "O bloco foi limitado pelo excedente seguro ou pela capacidade marginal da conta principal.";

        return $"Origem {candidate.SourceNet:0.####}% ({candidate.SourceIndex}); " +
               $"destino {candidate.Net:0.####}% ({candidate.Index}); " +
               $"vantagem líquida {candidate.Advantage:0.####}%. {limitation}";
    }

    private static AccountYieldRanges? FindRange(
        List<AccountYieldRanges> ranges,
        decimal balance)
    {
        return ranges.FirstOrDefault(range =>
            range.StartAmount <= balance &&
            (!range.EndAmount.HasValue || balance < range.EndAmount.Value));
    }

    private static decimal Normalize(decimal value)
    {
        return value <= 2 ? value * 100 : value;
    }

    private static string NormalizeIndex(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "DESCONHECIDO"
            : value.Trim().ToUpperInvariant();
    }

    private static decimal Net(decimal gross, Accounts account)
    {
        return account.IsTaxExempt
            ? gross
            : gross * (1 - Math.Max(0, account.IrPercent ?? 0) / 100);
    }

    private static void Exclude(
        InvestmentStrategyReportDTO report,
        Accounts account,
        string reason)
    {
        report.Exclusions.Add(new()
        {
            AccountName = account.Name,
            Reason = reason
        });
    }

    private sealed record State(
        Accounts Account,
        decimal InitialBalance,
        List<AccountsApplications> Applications,
        decimal? Maximum,
        List<AccountYieldRanges> Ranges)
    {
        public decimal SimulatedBalance { get; set; } = InitialBalance;
        public decimal Occupied => Applications.Sum(application => application.AmountApplied);
        public decimal? RemainingCapacity { get; set; } = Maximum.HasValue
            ? Math.Max(0, Maximum.Value - Applications.Sum(application => application.AmountApplied))
            : null;
    }

    private sealed record Candidate(
        State State,
        int? ApplicationId,
        AccountYieldRanges? Range,
        decimal Gross,
        decimal Net,
        decimal SourceGross,
        decimal SourceNet,
        decimal Advantage,
        decimal? AppCapacity,
        decimal? RangeCapacity,
        string Index,
        string SourceIndex);

    private sealed record Move(DateTime Date, decimal Amount, bool Income);
}
