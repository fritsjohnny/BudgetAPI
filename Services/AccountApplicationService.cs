using BudgetAPI.Data;
using BudgetAPI.Models;
using BudgetAPI.Helpers;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface IAccountApplicationService
    {
        IQueryable<AccountsApplications> GetApplications();
        IQueryable<AccountsApplications> GetApplicationsByAccount(int accountId);
        IQueryable<AccountsApplications> GetApplication(int id);

        Task<int> PostApplication(AccountsApplications application);
        Task<int> PutApplication(AccountsApplications application);
        Task<int> DeleteApplication(AccountsApplications application);
        Task<int> BulkInsertApplications(List<AccountsApplications> applications);

        bool ValidateAccountOwnership(int accountId);
        bool ApplicationExists(int id);
    }

    public class AccountApplicationService : IAccountApplicationService
    {
        private readonly BudgetContext _context;
        private readonly Users _user;

        public AccountApplicationService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _user    = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
        }

        public IQueryable<AccountsApplications> GetApplications()
        {
            IQueryable<AccountsApplications> query =
                _context.Set<AccountsApplications>()
                        .Join(_context.Accounts,
                              app => app.AccountId,
                              acc => acc.Id,
                              (app, acc) => new { app, acc })
                        .Where(x => x.acc.UserId == _user.Id)
                        .Select(x => x.app);

            return query;
        }

        public IQueryable<AccountsApplications> GetApplicationsByAccount(int accountId)
        {
            IQueryable<AccountsApplications> query =
                _context.Set<AccountsApplications>()
                        .Where(a => a.AccountId == accountId)
                        .Join(_context.Accounts,
                              app => app.AccountId,
                              acc => acc.Id,
                              (app, acc) => new { app, acc })
                        .Where(x => x.acc.UserId == _user.Id)
                        .Select(x => x.app);

            return query;
        }

        public IQueryable<AccountsApplications> GetApplication(int id)
        {
            IQueryable<AccountsApplications> query =
                _context.Set<AccountsApplications>()
                        .Where(a => a.Id == id)
                        .Join(_context.Accounts,
                              app => app.AccountId,
                              acc => acc.Id,
                              (app, acc) => new { app, acc })
                        .Where(x => x.acc.UserId == _user.Id)
                        .Select(x => x.app);

            return query;
        }

        public async Task<int> PostApplication(AccountsApplications application)
        {
            // segurança: não deixa criar para conta que não pertence ao usuário
            if (!ValidateAccountOwnership(application.AccountId))
                throw new UnauthorizedAccessException("Conta não pertence ao usuário.");

            await FinancialResourceValidator.ValidateAccountForCreateAsync(
                _context,
                _user.Id,
                application.AccountId);

            // CreatedAt é setado no ctor do DTO, mas reforço caso venha default
            if (application.CreatedAt == default(DateTime))
                application.CreatedAt = DateTime.UtcNow;

            _context.Set<AccountsApplications>().Add(application);

            return await _context.SaveChangesAsync();
        }

        public async Task<int> PutApplication(AccountsApplications application)
        {
            AccountsApplications? saved =
                await _context.Set<AccountsApplications>()
                    .Join(
                        _context.Accounts,
                        app => app.AccountId,
                        account => account.Id,
                        (app, account) => new
                        {
                            Application = app,
                            Account = account
                        })
                    .Where(x =>
                        x.Application.Id == application.Id &&
                        x.Account.UserId == _user.Id)
                    .Select(x => x.Application)
                    .FirstOrDefaultAsync();

            if (saved == null)
            {
                throw new InvalidOperationException(
                    "Aplicação financeira não encontrada para o usuário atual.");
            }

            await FinancialResourceValidator.ValidateAccountForUpdateAsync(
                _context,
                _user.Id,
                saved.AccountId,
                application.AccountId);

            _context.Entry(saved)
                .CurrentValues
                .SetValues(application);

            return await _context.SaveChangesAsync();
        }

        public Task<int> DeleteApplication(AccountsApplications application)
        {
            // valida propriedade antes de remover
            if (!ValidateAccountOwnership(application.AccountId))
                throw new UnauthorizedAccessException("Conta não pertence ao usuário.");

            _context.Set<AccountsApplications>().Remove(application);

            return _context.SaveChangesAsync();
        }

        public async Task<int> BulkInsertApplications(List<AccountsApplications> applications)
        {
            if (applications == null || applications.Count == 0)
                return 0;

            // Verifica todas as contas envolvidas pertencem ao usuário
            IEnumerable<int> accountIds = applications.Select(a => a.AccountId).Distinct();
            var accounts = _context.Accounts.Where(a => accountIds.Contains(a.Id) && a.UserId == _user.Id).ToList();

            if (accounts.Count != accountIds.Count())
                throw new UnauthorizedAccessException("Uma ou mais contas não pertencem ao usuário.");

            if (accounts.Any(a => a.Disabled == true))
                throw new InvalidOperationException("Uma ou mais contas alvo estão desativadas. Não é permitido inserir aplicações em conta desativada.");

            foreach (AccountsApplications app in applications)
            {
                if (app.CreatedAt == default(DateTime))
                    app.CreatedAt = DateTime.UtcNow;

                _context.Set<AccountsApplications>().Add(app);
            }

            return await _context.SaveChangesAsync();
        }

        public bool ValidateAccountOwnership(int accountId)
        {
            return _context.Accounts.Any(a => a.Id == accountId && a.UserId == _user.Id);
        }

        public bool ApplicationExists(int id)
        {
            // restringe ao usuário atual via join
            bool exists = _context.Set<AccountsApplications>()
                                  .Where(a => a.Id == id)
                                  .Join(_context.Accounts,
                                        app => app.AccountId,
                                        acc => acc.Id,
                                        (app, acc) => new { acc.UserId })
                                  .Any(x => x.UserId == _user.Id);

            return exists;
        }
    }
}
