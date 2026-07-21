using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface IAccountService
    {
        IQueryable<Accounts> GetAccount();
        IQueryable<Accounts> GetAccount(int id);
        IQueryable<AccountsDTO> GetAccountTotals(int account, string reference);
        IQueryable<AccountsSummary> GetAccountsSummary(string reference);
        IQueryable<AccountsSummaryTotals> GetAccountsSummaryTotals(string reference);
        Task<AccountForecastBalanceReportDTO> GetForecastBalanceReport(int accountId, DateTime initialDate, DateTime finalDate);
        Task<int> PutAccount(Accounts account);
        Task<int> PostAccount(Accounts account);
        Task<int> SetPositions(List<Accounts> accounts);
        Task<int> DeleteAccount(Accounts account);
        bool AccountExists(int id);
        bool ValidarUsuario(int id);
        IQueryable<Accounts> GetAvailableAccounts(string reference);
    }

    public class AccountService : IAccountService
    {
        private readonly BudgetContext _context;
        private readonly Users _user;

        public AccountService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _user    = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
        }

        public IQueryable<Accounts> GetAccount()
        {
            IQueryable<Accounts> query = _context.Accounts.Where(a => a.UserId == _user.Id);

            return query;
        }

        public IQueryable<AccountsDTO> GetAccountTotals(int accountId, string reference)
        {
            IQueryable<AccountsDTO> accountDto = Enumerable.Empty<AccountsDTO>().AsQueryable();

            try
            {
                accountDto = _context.GetAccountTotals(accountId, reference, _user.Id);
            }
            catch
            {
                /**/
            }

            return accountDto;
        }

        public IQueryable<AccountsSummary> GetAccountsSummary(string reference)
        {
            IQueryable<AccountsSummary> query = Enumerable.Empty<AccountsSummary>().AsQueryable();

            try
            {
                query = _context.GetAccountsSummary(reference, _user.Id);
            }
            catch
            {
                /**/
            }

            return query;
        }

        public IQueryable<AccountsSummaryTotals> GetAccountsSummaryTotals(string reference)
        {
            IQueryable<AccountsSummaryTotals> accountsSummaryTotals = Enumerable.Empty<AccountsSummaryTotals>().AsQueryable();

            try
            {
                accountsSummaryTotals = _context.GetTotalsAccountsSummary(reference, _user.Id);
            }
            catch
            {
                /**/
            }

            return accountsSummaryTotals;
        }

        public IQueryable<Accounts> GetAccount(int id)
        {
            IQueryable<Accounts> accounts = _context.Accounts.Where(a => a.UserId == _user.Id && a.Id == id);

            return accounts;
        }

        public async Task<AccountForecastBalanceReportDTO> GetForecastBalanceReport(
            int accountId,
            DateTime initialDate,
            DateTime finalDate)
        {
            DateTime startDate = initialDate.Date;
            DateTime endDate   = finalDate.Date;

            if (startDate > endDate)
            {
                throw new ArgumentException("A data inicial não pode ser maior que a data final.");
            }

            Accounts? account = await _context.Accounts
                                              .AsNoTracking()
                                              .FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == _user.Id);

            if (account == null)
            {
                throw new InvalidOperationException("Conta não encontrada para o usuário atual.");
            }

            decimal currentBalance = await _context.AccountsPostings
                                                   .Where(ap => ap.AccountId == accountId)
                                                   .SumAsync(ap => (decimal?)ap.Amount) ?? 0;

            List<AccountForecastMovement> incomes = await _context.Incomes
                .AsNoTracking()
                .Where(i => i.UserId == _user.Id &&
                            i.ReceiptDate.HasValue &&
                            i.ReceiptDate.Value >= startDate &&
                            i.ReceiptDate.Value <= endDate &&
                            (i.ToReceive - i.Received) != 0)
                .Select(i => new AccountForecastMovement
                {
                    Id          = i.Id,
                    Date        = i.ReceiptDate!.Value,
                    Description = i.Description,
                    Amount      = i.ToReceive - i.Received,
                    Reference   = i.Reference,
                    Type        = "R",
                    TypeOrder   = 0,
                    Position    = i.Position
                })
                .ToListAsync();

            List<AccountForecastMovement> expenses = await _context.Expenses
                .AsNoTracking()
                .Where(e => e.UserId == _user.Id &&
                            e.DueDate.HasValue &&
                            e.DueDate.Value >= startDate &&
                            e.DueDate.Value <= endDate &&
                            (e.ToPay - Math.Abs(e.Paid)) != 0)
                .Select(e => new AccountForecastMovement
                {
                    Id          = e.Id,
                    Date        = e.DueDate!.Value,
                    Description = e.Description ?? string.Empty,
                    Amount      = (e.ToPay - Math.Abs(e.Paid)) * -1,
                    Reference   = e.Reference,
                    Type        = "P",
                    TypeOrder   = 1,
                    Position    = e.Position
                })
                .ToListAsync();

            List<AccountForecastMovement> movements = incomes
                .Concat(expenses)
                .OrderBy(m => m.Date)
                .ThenBy(m => m.TypeOrder)
                .ThenBy(m => m.Position ?? short.MaxValue)
                .ThenBy(m => m.Id)
                .ToList();

            decimal runningBalance = currentBalance;
            int sequence           = 1;

            List<AccountForecastBalanceReportRowDTO> rows = new();

            foreach (AccountForecastMovement movement in movements)
            {
                runningBalance += movement.Amount;

                rows.Add(new AccountForecastBalanceReportRowDTO
                {
                    Id          = movement.Id,
                    Sequence    = sequence++,
                    Date        = movement.Date,
                    Description = movement.Description,
                    Amount      = movement.Amount,
                    Balance     = runningBalance,
                    Reference   = movement.Reference,
                    Type        = movement.Type
                });
            }

            return new AccountForecastBalanceReportDTO
            {
                AccountId     = account.Id,
                AccountName   = account.Name,
                CurrentBalance = currentBalance,
                FinalBalance   = runningBalance,
                Rows           = rows
            };
        }

        public Task<int> PutAccount(Accounts account)
        {
            _context.Entry(account).State = EntityState.Modified;

            return _context.SaveChangesAsync();
        }

        public Task<int> PostAccount(Accounts account)
        {
            account.UserId = _user.Id;

            _context.Accounts.Add(account);

            return _context.SaveChangesAsync();
        }

        public Task<int> DeleteAccount(Accounts account)
        {
            _context.Accounts.Remove(account);

            return _context.SaveChangesAsync();
        }

        public bool AccountExists(int id)
        {
            return _context.Accounts.Any(e => e.Id == id);
        }

        public bool ValidarUsuario(int id)
        {
            return id == _user.Id;
        }

        public Task<int> SetPositions(List<Accounts> accounts)
        {
            foreach (Accounts account in accounts)
            {
                _context.Entry(account).State = EntityState.Modified;
            }

            return _context.SaveChangesAsync();
        }

        public IQueryable<Accounts> GetAvailableAccounts(string reference)
        {
            IQueryable<Accounts> accounts = _context.Accounts
                .Where(a =>
                    a.UserId == _user.Id &&
                    (
                        a.Disabled != true ||
                        _context.AccountsPostings.Any(ap =>
                            ap.AccountId == a.Id &&
                            ap.Reference == reference)
                    ))
                .OrderBy(a => a.Position ?? short.MaxValue)
                .ThenBy(a => a.Name);

            return accounts;
        }

        private sealed class AccountForecastMovement
        {
            public int Id { get; set; }
            public DateTime Date { get; set; }
            public string Description { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public string Reference { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public int TypeOrder { get; set; }
            public short? Position { get; set; }
        }
    }
}
