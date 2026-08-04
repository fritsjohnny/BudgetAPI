using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace BudgetAPI.Services;

public interface IInvestmentStrategyService { Task<InvestmentStrategyReportDTO> GetReport(InvestmentStrategyRequestDTO request); }

public sealed class InvestmentStrategyService : IInvestmentStrategyService
{
    private readonly BudgetContext _context;
    private readonly Users _user;
    public InvestmentStrategyService(BudgetContext context, IHttpContextAccessor accessor) { _context = context; _user = accessor.HttpContext?.Items["User"] as Users ?? new Users(); }

    public async Task<InvestmentStrategyReportDTO> GetReport(InvestmentStrategyRequestDTO request)
    {
        if (request.InitialDate.Date > request.FinalDate.Date) throw new ArgumentException("Initial date cannot be greater than final date.");
        var main = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.AccountId && x.UserId == _user.Id && x.Disabled != true) ?? throw new InvalidOperationException("Main account was not found or is disabled.");
        var balance = await _context.AccountsPostings.Where(x => x.AccountId == main.Id).SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var historyEnd = DateTime.Today; var historyStart = historyEnd.AddDays(-89);
        var paidRows = await _context.Expenses.AsNoTracking().Where(x => x.UserId == _user.Id && x.DueDate >= historyStart && x.DueDate <= historyEnd && x.Paid != 0).Select(x => new Move(x.DueDate!.Value.Date, Math.Abs(x.Paid), false)).ToListAsync();
        var historicalPaid = paidRows.Sum(x => x.Amount); var historicalStart = paidRows.Count == 0 ? historyStart : paidRows.Min(x => x.Date); var historicalEnd = paidRows.Count == 0 ? historyEnd : paidRows.Max(x => x.Date); var historicalDays = paidRows.Count == 0 ? 0 : Math.Max(1, (historyEnd - historyStart).Days + 1); var average = historicalDays == 0 ? 0 : historicalPaid / historicalDays;
        var expenses = await _context.Expenses.AsNoTracking().Where(x => x.UserId == _user.Id && x.DueDate <= request.FinalDate && x.ToPay - Math.Abs(x.Paid) != 0 && x.DueDate >= request.InitialDate).Select(x => new Move(x.DueDate!.Value.Date, x.ToPay - Math.Abs(x.Paid), false)).ToListAsync();
        var overdueExpenses = await _context.Expenses.AsNoTracking().Where(x => x.UserId == _user.Id && x.DueDate < request.InitialDate && x.ToPay - Math.Abs(x.Paid) != 0).Select(x => new Move(request.InitialDate.Date, x.ToPay - Math.Abs(x.Paid), false)).ToListAsync(); expenses.AddRange(overdueExpenses);
        var incomes = await _context.Incomes.AsNoTracking().Where(x => x.UserId == _user.Id && x.ReceiptDate >= request.InitialDate && x.ReceiptDate <= request.FinalDate && x.ToReceive - x.Received != 0).Select(x => new Move(x.ReceiptDate!.Value.Date, x.ToReceive - x.Received, true)).ToListAsync();
        var overdueIncomeCount = await _context.Incomes.CountAsync(x => x.UserId == _user.Id && x.ReceiptDate < request.InitialDate && x.ToReceive - x.Received != 0);
        var report = new InvestmentStrategyReportDTO { CurrentBalance = balance, TotalIncome = incomes.Sum(x => x.Amount), TotalExpense = expenses.Sum(x => x.Amount), HistoricalPaidAmount = historicalPaid, HistoricalDays = historicalDays, HistoricalStartDate = historicalStart, HistoricalEndDate = historicalEnd, HistoricalDailyExpenseAverage = Math.Round(average, 2), ReserveCoverageDays = 7, SuggestedReserve = historicalDays > 0 ? Math.Round(average * 7, 2) : Math.Round(expenses.Sum(x => x.Amount) * .10m, 2) };
        report.Reserve = Math.Max(0, request.OperationalReserve ?? report.SuggestedReserve); report.ReserveExplanation = historicalDays > 0 ? $"Reserva baseada na média diária de {average.ToString("C", CultureInfo.GetCultureInfo("pt-BR"))} durante {historicalDays} dias, com 7 dias de cobertura." : "Não há histórico pago suficiente. Foi utilizado o fallback de 10% das despesas pendentes."; if (overdueIncomeCount > 0) report.Warnings.Add("Existem receitas vencidas ainda não recebidas. Elas não foram consideradas como disponíveis na estratégia.");
        var running = balance; foreach (var day in incomes.Concat(expenses).GroupBy(x => x.Date).OrderBy(x => x.Key)) { var income = day.Where(x => x.Income).Sum(x => x.Amount); var expense = day.Where(x => !x.Income).Sum(x => x.Amount); running += income - expense; report.Timeline.Add(new() { Date = day.Key, Income = income, Expense = expense, BaseBalance = running }); } report.FinalBalance = running; var critical = report.Timeline.OrderBy(x => x.BaseBalance).FirstOrDefault(); report.LowestBalance = critical?.BaseBalance ?? balance; report.CriticalDate = critical?.Date; report.SafeSurplus = Math.Max(0, report.LowestBalance - report.Reserve);
        var mainRanges = await Ranges(main.Id); var destinations = new List<State>();
        var accounts = await _context.Accounts.AsNoTracking().Where(x => x.UserId == _user.Id && x.Id != main.Id && x.Disabled != true).ToListAsync();
        foreach (var account in accounts)
        {
            var current = await _context.AccountsPostings.Where(x => x.AccountId == account.Id).SumAsync(x => (decimal?)x.Amount) ?? 0m;
            var apps = await _context.AccountsApplications.AsNoTracking().Where(x => x.AccountId == account.Id && !x.Disabled).ToListAsync();
            var ranges = await Ranges(account.Id);
            var limits = apps.Where(x => x.MaximumAmount.HasValue).Select(x => x.MaximumAmount!.Value).Distinct().ToList();
            if (limits.Count > 1) { Exclude(report, account, "Conflito entre limites máximos das aplicações ativas."); continue; }
            var rateKeys = apps.Where(x => x.CdiPercent.HasValue || x.FixedRate.HasValue).Select(x => x.CdiPercent.HasValue ? $"CDI:{Normalize(x.CdiPercent.Value):0.####}" : $"FIXED:{x.FixedRate:0.####}").Distinct().Count();
            if (rateKeys > 1 && ranges.Count == 0) { Exclude(report, account, "Condições de rendimento das aplicações ativas conflitantes."); continue; }
            if (apps.Count == 0 && ranges.Count == 0 && !account.YieldPercent.HasValue) { Exclude(report, account, "Rendimento do destino não configurado."); continue; }
            decimal? maximum = limits.Count == 1 ? limits[0] : null;
            destinations.Add(new(account, current, apps, maximum, ranges));
        }
        var remaining = report.SafeSurplus; var sourceBalance = balance; var guard = 0;
        while (remaining > 0 && guard++ < 1000)
        {
            var sourceRange = FindRange(mainRanges, sourceBalance); if (mainRanges.Count > 0 && sourceRange == null) { const string limitation = "O saldo da conta principal não pertence a nenhuma faixa de rendimento configurada."; if (!report.Limitations.Contains(limitation)) report.Limitations.Add(limitation); break; } var sourceGross = sourceRange?.YieldPercent ?? main.YieldPercent ?? 0m; var sourceNet = Net(sourceGross, main); var sourceCapacity = Math.Max(0, sourceBalance - report.Reserve);
            var candidates = destinations.Select(d => CandidateFor(d, main, sourceCapacity, sourceGross, sourceNet)).Where(x => x != null).Cast<Candidate>().OrderByDescending(x => x.Advantage).ThenByDescending(x => x.Net).ThenBy(x => new[] { x.AppCapacity ?? decimal.MaxValue, x.RangeCapacity ?? decimal.MaxValue }.Min()).ThenBy(x => x.State.Account.Id).ThenBy(x => x.ApplicationId ?? int.MaxValue).ToList(); var best = candidates.FirstOrDefault(); if (best == null) break;
            var amount = new[] { remaining, sourceCapacity, best.AppCapacity ?? remaining, best.RangeCapacity ?? remaining }.Min(); if (amount <= 0) break;
            var before = best.State.SimulatedBalance; var appBefore = best.State.RemainingCapacity; best.State.SimulatedBalance += amount; best.State.RemainingCapacity = best.State.RemainingCapacity.HasValue ? best.State.RemainingCapacity - amount : null; sourceBalance -= amount; remaining -= amount;
            report.Recommendations.Add(ToDto(best, amount, before, appBefore));
        }
        if (remaining > 0 && guard >= 1000) throw new InvalidOperationException("A alocação da Estratégia de Investimentos não apresentou progresso.");
        report.RecommendedInvestment = report.Recommendations.Sum(x => x.RecommendedAmount); report.SafeSurplusWithoutDestination = Math.Max(0, report.SafeSurplus - report.RecommendedInvestment); report.KeptInMainAccount = balance - report.RecommendedInvestment; report.FinalBalance -= report.RecommendedInvestment; foreach (var row in report.Timeline) { row.StrategyBalance = row.BaseBalance - report.RecommendedInvestment; row.ReserveMargin = row.StrategyBalance - report.Reserve; row.IsCritical = row.Date == report.CriticalDate; } var minMargin = report.Timeline.Count == 0 ? balance - report.RecommendedInvestment - report.Reserve : report.Timeline.Min(x => x.ReserveMargin); report.Classification = report.RecommendedInvestment == 0 || minMargin < 0 ? "Não recomendado" : minMargin < Math.Max(1m, report.Reserve * .10m) ? "Seguro com margem reduzida" : "Seguro"; return report;
    }

    private async Task<List<AccountYieldRanges>> Ranges(int id) => await _context.AccountYieldRanges.AsNoTracking().Where(x => x.AccountId == id).OrderBy(x => x.StartAmount).ToListAsync();
    private static Candidate? CandidateFor(State state, Accounts source, decimal sourceCapacity, decimal sourceGross, decimal sourceNet)
    { var range = FindRange(state.Ranges, state.SimulatedBalance); if (state.Ranges.Count > 0 && range == null) return null; var app = state.Applications.FirstOrDefault(); var gross = range?.YieldPercent ?? (app?.CdiPercent is decimal cdi ? Normalize(cdi) : app?.FixedRate is decimal fixedRate ? Normalize(fixedRate) : state.Account.YieldPercent ?? 0); var index = range != null || app?.CdiPercent.HasValue != true && app?.FixedRate.HasValue != true ? NormalizeIndex(state.Account.YieldIndex) : app.CdiPercent.HasValue ? "CDI" : "PREFIXADO"; var sourceIndex = sourceGross == 0 ? "SEM RENDIMENTO" : NormalizeIndex(source.YieldIndex); if (gross <= 0 || Net(gross, state.Account) <= sourceNet || sourceCapacity <= 0 || state.RemainingCapacity is 0) return null; decimal? rangeCapacity = range?.EndAmount is decimal end ? Math.Max(0, end - state.SimulatedBalance) : null; return new(state, app?.Id, range, gross, Net(gross, state.Account), sourceGross, sourceNet, Net(gross, state.Account) - sourceNet, state.RemainingCapacity, rangeCapacity, index, sourceIndex); }
    private static InvestmentRecommendationDTO ToDto(Candidate x, decimal amount, decimal before, decimal? appBefore) => new() { AccountId = x.State.Account.Id, ApplicationId = x.ApplicationId, AccountName = x.State.Account.Name, CurrentBalance = x.State.InitialBalance, Capacity = appBefore, RecommendedAmount = amount, YieldPercent = x.Gross, MainAccountYieldPercent = x.SourceGross, AdvantagePercent = x.Advantage, ApplicationCapacity = appBefore, RangeCapacity = x.RangeCapacity, RangeStart = x.Range?.StartAmount ?? 0, RangeEnd = x.Range?.EndAmount, DestinationGrossYield = x.Gross, DestinationNetYield = x.Net, SourceGrossYield = x.SourceGross, SourceNetYield = x.SourceNet, CapacityAfter = x.State.RemainingCapacity, DestinationBalanceBefore = before, DestinationBalanceAfter = x.State.SimulatedBalance, MaximumAmount = x.State.Maximum, OccupiedAmount = x.State.Occupied, ApplicationCapacityBefore = appBefore, ApplicationCapacityAfter = x.State.RemainingCapacity, RangeId = x.Range?.Id, RangeCapacityBefore = x.RangeCapacity, RangeCapacityAfter = x.Range?.EndAmount is decimal end ? Math.Max(0, end - x.State.SimulatedBalance) : null, DestinationYieldIndex = x.Index, SourceYieldIndex = x.SourceIndex, CapacityBasis = CapacityBasis(x), Reason = Reason(x, amount), IsDestinationTaxExempt = x.State.Account.IsTaxExempt, DestinationIrPercent = x.State.Account.IrPercent ?? 0m };
    private static string CapacityBasis(Candidate x) => x.State.Maximum.HasValue && x.Range?.EndAmount.HasValue == true ? "Menor capacidade entre o limite máximo compartilhado e a faixa vigente." : x.State.Maximum.HasValue ? "Limite máximo compartilhado menos a soma dos aportes ativos." : x.Range?.EndAmount.HasValue == true ? "Capacidade da faixa de rendimento vigente." : "Sem limite máximo ou superior de faixa; limitado pelo excedente seguro e pela capacidade da origem.";
    private static string Reason(Candidate x, decimal amount) => $"Origem {x.SourceNet:0.####}% ({x.SourceIndex}); destino {x.Net:0.####}% ({x.Index}); vantagem líquida {x.Advantage:0.####}%. {(x.RangeCapacity.HasValue && x.AppCapacity.HasValue && x.RangeCapacity.Value <= x.AppCapacity.Value ? "O bloco foi limitado pelo final da faixa vigente." : x.AppCapacity.HasValue ? "O bloco foi limitado pelo limite máximo compartilhado." : "O bloco foi limitado pelo excedente seguro ou pela capacidade marginal da conta principal.")}";
    private static AccountYieldRanges? FindRange(List<AccountYieldRanges> ranges, decimal balance) => ranges.FirstOrDefault(x => x.StartAmount <= balance && (!x.EndAmount.HasValue || balance < x.EndAmount.Value));
    private static decimal Normalize(decimal value) => value <= 2 ? value * 100 : value;
    private static string NormalizeIndex(string? value) => string.IsNullOrWhiteSpace(value) ? "DESCONHECIDO" : value.Trim().ToUpperInvariant();
    private static decimal Net(decimal gross, Accounts account) => account.IsTaxExempt ? gross : gross * (1 - Math.Max(0, account.IrPercent ?? 0) / 100);
    private static void Exclude(InvestmentStrategyReportDTO report, Accounts account, string reason) => report.Exclusions.Add(new() { AccountName = account.Name, Reason = reason });
    private sealed record State(Accounts Account, decimal InitialBalance, List<AccountsApplications> Applications, decimal? Maximum, List<AccountYieldRanges> Ranges) { public decimal SimulatedBalance { get; set; } = InitialBalance; public decimal Occupied => Applications.Sum(x => x.AmountApplied); public decimal? RemainingCapacity { get; set; } = Maximum.HasValue ? Math.Max(0, Maximum.Value - Applications.Sum(x => x.AmountApplied)) : null; }
    private sealed record Candidate(State State, int? ApplicationId, AccountYieldRanges? Range, decimal Gross, decimal Net, decimal SourceGross, decimal SourceNet, decimal Advantage, decimal? AppCapacity, decimal? RangeCapacity, string Index, string SourceIndex);
    private sealed record Move(DateTime Date, decimal Amount, bool Income);
}
