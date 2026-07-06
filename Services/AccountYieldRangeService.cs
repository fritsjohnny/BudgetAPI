using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface IAccountYieldRangeService
    {
        IQueryable<AccountYieldRanges> GetByAccount(int accountId);
        Task<int> ReplaceByAccount(int accountId, List<AccountYieldRanges> ranges);
    }

    public class AccountYieldRangeService : IAccountYieldRangeService
    {
        private readonly BudgetContext _context;
        private readonly Users _user;

        public AccountYieldRangeService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _user = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
        }

        public IQueryable<AccountYieldRanges> GetByAccount(int accountId)
        {
            return _context.AccountYieldRanges
                .Include(x => x.Account)
                .Where(x => x.AccountId == accountId && x.Account!.UserId == _user.Id)
                .OrderBy(x => x.Position);
        }

        public async Task<int> ReplaceByAccount(int accountId, List<AccountYieldRanges> ranges)
        {
            bool accountExists = await _context.Accounts.AnyAsync(x => x.Id == accountId && x.UserId == _user.Id);

            if (!accountExists)
                throw new ArgumentException("Conta inválida ou não pertence ao usuário.");

            ranges = ranges ?? new List<AccountYieldRanges>();

            ValidateRanges(ranges);

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                List<AccountYieldRanges> currentRanges = await _context.AccountYieldRanges
                    .Where(x => x.AccountId == accountId)
                    .ToListAsync();

                _context.AccountYieldRanges.RemoveRange(currentRanges);

                short position = 1;

                foreach (AccountYieldRanges range in ranges.OrderBy(x => x.StartAmount))
                {
                    range.Id = 0;
                    range.AccountId = accountId;
                    range.Position = position++;
                    range.CreatedAt = DateTime.Now;
                    range.Account = null;

                    _context.AccountYieldRanges.Add(range);
                }

                int rows = await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return rows;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static void ValidateRanges(List<AccountYieldRanges> ranges)
        {
            if (ranges.Count == 0)
                return;

            List<AccountYieldRanges> orderedRanges = ranges.OrderBy(x => x.StartAmount).ToList();

            if (orderedRanges[0].StartAmount != 0)
                throw new ArgumentException("A primeira faixa deve iniciar em 0.");

            for (int i = 0; i < orderedRanges.Count; i++)
            {
                AccountYieldRanges range = orderedRanges[i];

                if (range.StartAmount < 0)
                    throw new ArgumentException("Valor inicial da faixa não pode ser negativo.");

                if (range.EndAmount.HasValue && range.EndAmount.Value <= range.StartAmount)
                    throw new ArgumentException("Valor final da faixa deve ser maior que o valor inicial.");

                if (range.YieldPercent <= 0)
                    throw new ArgumentException("Percentual de rendimento da faixa deve ser maior que zero.");

                bool isLast = i == orderedRanges.Count - 1;

                if (!isLast)
                {
                    AccountYieldRanges nextRange = orderedRanges[i + 1];

                    if (!range.EndAmount.HasValue)
                        throw new ArgumentException("Apenas a última faixa pode ter valor final vazio.");

                    if (range.EndAmount.Value != nextRange.StartAmount)
                        throw new ArgumentException("As faixas de rendimento devem ser contínuas.");
                }
                else if (range.EndAmount.HasValue)
                {
                    throw new ArgumentException("A última faixa deve ter valor final vazio.");
                }
            }
        }
    }
}