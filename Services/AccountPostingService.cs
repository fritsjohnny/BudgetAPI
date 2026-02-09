using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface IAccountPostingService
    {
        IQueryable<AccountsPostings> GetAccountsPostings();
        IQueryable<AccountsPostings> GetAccountsPostings(int id);
        IQueryable<AccountsPostings> GetAccountsPostings(int accountId, string reference);
        Task<int> PutAccountsPostings(AccountsPostings accountPosting);
        Task<int> PostAccountsPostings(AccountsPostings accountsPostings);
        Task<int> DeleteAccountsPostings(AccountsPostings accountsPostings);
        Task<int> SetPositions(List<AccountsPostings> accountsPostings);
        bool ValidarUsuario(int accountPostingId);
        bool AccountsPostingsExists(int id);
        bool ValidateAccountAndUser(int accountId);
        IQueryable<AccountsYieldsDTO> GetAccountsYields(string? reference, int? accountId);
        Task<int> TransferBetweenAccounts(AccountsPostings accountPosting);
    }

    public class AccountPostingService : IAccountPostingService
    {
        private readonly BudgetContext _context;
        private readonly Users _user;

        public AccountPostingService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _user    = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
        }

        public IQueryable<AccountsPostings> GetAccountsPostings()
        {
            return _context.AccountsPostings.Include(a => a.Account)
                                            .Where(a => a.Account!.UserId == _user.Id)
                                            .OrderBy(a => a.Position);
        }

        public IQueryable<AccountsPostings> GetAccountsPostings(int id)
        {
            IQueryable<AccountsPostings>? accountsPostings = _context.AccountsPostings.Include(a => a.Account)
                                                                                      .Where(a => a.Id == id && a.Account!.UserId == _user.Id);

            return accountsPostings;
        }

        public IQueryable<AccountsPostings> GetAccountsPostings(int accountId, string reference)
        {
            IOrderedQueryable<AccountsPostings>? accountsPostings = _context.AccountsPostings.Include(a => a.Account)
                                                                                             .Where(a => a.AccountId == accountId && a.Reference == reference && a.Account!.UserId == _user.Id)
                                                                                             .OrderBy(a => a.Position);

            return accountsPostings;
        }

        // ✅ CORREÇÃO 1: Método auxiliar para identificar transferências
        private bool IsTransfer(AccountsPostings posting)
        {
            return posting.Type == "T" ||
                   posting.Type == "P" ||
                   posting.Type == "R" ||
                   posting.RelatedId != null;
        }

        // ✅ CORREÇÃO 2: Método auxiliar para gerar descrições padronizadas
        private (string originDesc, string destinationDesc) GetTransferDescriptions(Accounts fromAccount, Accounts toAccount)
        {
            string descOrigin      = $"Transferido para {toAccount.Name ?? "Conta destino"}";
            string descDestination = $"Recebido de {fromAccount.Name ?? "Conta origem"}";
            return (descOrigin, descDestination);
        }

        public Task<int> PutAccountsPostings(AccountsPostings accountsPostings)
        {
            if (IsTransfer(accountsPostings))
            {
                return UpdateTransferBetweenAccounts(accountsPostings);
            }

            _context.Entry(accountsPostings).State = EntityState.Modified;
            return _context.SaveChangesAsync();
        }

        private async Task<int> UpdateTransferBetweenAccounts(AccountsPostings request)
        {
            if (request == null) throw new ArgumentException("Dados da transferência não informados.");
            if (request.AccountId <= 0 || request.ToAccountId <= 0) throw new ArgumentException("Conta de origem e conta de destino são obrigatórias.");
            if (request.AccountId == request.ToAccountId) throw new ArgumentException("A conta de origem deve ser diferente da conta de destino.");
            if (string.IsNullOrWhiteSpace(request.Reference)) throw new ArgumentException("Referência é obrigatória.");

            decimal newAmount = Math.Abs(request.Amount);

            if (newAmount <= 0) throw new ArgumentException("Valor da transferência deve ser maior que zero.");

            AccountsPostings? current = await _context.AccountsPostings.Include(a => a.Account)
                                                               .FirstOrDefaultAsync(a => a.Id == request.Id && a.Account!.UserId == _user.Id);

            if (current == null) throw new InvalidOperationException("Lançamento não encontrado.");
            if (current.RelatedId == null) throw new InvalidOperationException("Transferência inválida: lançamento relacionado não encontrado.");

            AccountsPostings? related = await _context.AccountsPostings.Include(a => a.Account)
                                                               .FirstOrDefaultAsync(a => a.Id == current.RelatedId.Value && a.Account!.UserId == _user.Id);

            if (related == null) throw new InvalidOperationException("Transferência inválida: lançamento relacionado não encontrado.");

            AccountsPostings origin      = (current.Type == "P") ? current : related;
            AccountsPostings destination = (origin.Id == current.Id) ? related : current;

            // ⚠️ CORREÇÃO CRÍTICA: Determinar qual lançamento tem qual tipo
            // Se current.Type == "R", então current é o destino e request tem os dados do destino
            // Precisamos SEMPRE trabalhar com os dados de ORIGEM (Type="P")

            // Mapear os dados do request para os lançamentos corretos
            int requestedFromAccountId;
            int requestedToAccountId;

            if (current.Type == "P")
            {
                // Request veio do lançamento de origem
                requestedFromAccountId = request.AccountId;
                requestedToAccountId = request.ToAccountId!.Value;
            }
            else
            {
                // Request veio do lançamento de destino - inverter
                requestedFromAccountId = request.ToAccountId!.Value;
                requestedToAccountId = request.AccountId;
            }

            Accounts? fromAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == requestedFromAccountId);
            Accounts? toAccount   = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == requestedToAccountId);

            if (fromAccount == null || toAccount == null) throw new ArgumentException("Conta de origem e/ou conta de destino não encontrada.");
            if (fromAccount.UserId != _user.Id || toAccount.UserId != _user.Id) throw new ArgumentException("Não é permitido transferir entre contas de usuários diferentes.");

            // ✅ CORREÇÃO 3: Validação de saldo corrigida
            decimal oldAmount = Math.Abs(origin.Amount);
            int oldFromAccountId = origin.AccountId;
            int newFromAccountId = requestedFromAccountId; // Agora usa a variável correta

            // Se mudou a conta de origem, valida ambas
            if (oldFromAccountId != newFromAccountId)
            {
                // Valida saldo da NOVA conta origem (sem considerar lançamento antigo)
                AccountsDTO? newFromTotals = null;
                try
                {
                    newFromTotals = _context.GetAccountTotals(newFromAccountId, request.Reference, _user.Id).FirstOrDefault();
                }
                catch { /**/ }

                if (newFromTotals == null) throw new InvalidOperationException("Não foi possível obter o saldo da nova conta de origem.");

                decimal projectedBalanceNewAccount = newFromTotals.TotalBalance - newAmount;

                if (projectedBalanceNewAccount < 0)
                    throw new InvalidOperationException($"Transferência não permitida: saldo insuficiente na conta '{fromAccount.Name}'. Saldo disponível: {newFromTotals.TotalBalance:C}, necessário: {newAmount:C}");
            }
            else
            {
                // Mesma conta origem: remove efeito do lançamento antigo e valida o novo
                AccountsDTO? fromTotals = null;
                try
                {
                    fromTotals = _context.GetAccountTotals(newFromAccountId, request.Reference, _user.Id).FirstOrDefault();
                }
                catch { /**/ }

                if (fromTotals == null) throw new InvalidOperationException("Não foi possível obter o saldo atual da conta de origem.");

                decimal balanceWithoutThisTransfer = fromTotals.TotalBalance + oldAmount;
                decimal projectedBalance           = balanceWithoutThisTransfer - newAmount;

                if (projectedBalance < 0)
                    throw new InvalidOperationException($"Transferência não permitida: saldo insuficiente. Saldo disponível: {balanceWithoutThisTransfer:C}, necessário: {newAmount:C}");
            }

            var (descOrigin, descDestination) = GetTransferDescriptions(fromAccount, toAccount);

            using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                // Usar as variáveis corretas
                origin.AccountId   = requestedFromAccountId;
                origin.ToAccountId = requestedToAccountId;
                origin.Date        = request.Date;
                origin.Reference   = request.Reference;
                origin.Description = descOrigin;
                origin.Amount      = newAmount * -1;
                origin.Note        = request.Note;
                origin.Type        = "P";

                destination.AccountId   = requestedToAccountId;
                destination.ToAccountId = requestedFromAccountId;
                destination.Date        = request.Date;
                destination.Reference   = request.Reference;
                destination.Description = descDestination;
                destination.Amount      = newAmount;
                destination.Note        = request.Note;
                destination.Type        = "R";

                _context.Entry(origin).State      = EntityState.Modified;
                _context.Entry(destination).State = EntityState.Modified;

                int rows = await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return rows;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<int> PostAccountsPostings(AccountsPostings accountsPostings)
        {
            if (IsTransfer(accountsPostings))
            {
                return await TransferBetweenAccounts(accountsPostings);
            }

            accountsPostings.Position = (short)((_context.AccountsPostings.Where(o => o.Reference == accountsPostings.Reference).Max(o => o.Position) ?? 0) + 1);

            _context.AccountsPostings.Add(accountsPostings);

            if (accountsPostings.ExpenseId != null && accountsPostings.Type == "P")
            {
                var expense = _context.Expenses.Find(accountsPostings.ExpenseId);

                if (expense != null)
                {
                    expense.Scheduled = false;
                }
            }

            return await _context.SaveChangesAsync();
        }

        public async Task<int> DeleteAccountsPostings(AccountsPostings accountsPostings)
        {
            // ✅ CORREÇÃO 4: Verifica se é transferência usando método auxiliar
            if (IsTransfer(accountsPostings))
            {
                return await DeleteTransferBetweenAccounts(accountsPostings.Id);
            }

            _context.AccountsPostings.Remove(accountsPostings);
            return await _context.SaveChangesAsync();
        }

        private async Task<int> DeleteTransferBetweenAccounts(int id)
        {
            AccountsPostings? current = await _context.AccountsPostings.Include(a => a.Account)
                                                                       .FirstOrDefaultAsync(a => a.Id == id && a.Account!.UserId == _user.Id);

            if (current == null) throw new InvalidOperationException("Lançamento não encontrado.");

            AccountsPostings? related = null;

            if (current.RelatedId != null)
            {
                related = await _context.AccountsPostings.Include(a => a.Account)
                                                         .FirstOrDefaultAsync(a => a.Id == current.RelatedId.Value && a.Account!.UserId == _user.Id);
            }
            else
            {
                // fallback: tenta localizar o par pelo inverso
                related = await _context.AccountsPostings.Include(a => a.Account)
                                                         .FirstOrDefaultAsync(a => a.RelatedId == current.Id && a.Account!.UserId == _user.Id);
            }

            using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                _context.AccountsPostings.Remove(current);

                if (related != null)
                {
                    _context.AccountsPostings.Remove(related);
                }

                int rows = await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return rows;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public Task<int> SetPositions(List<AccountsPostings> accountsPostings)
        {
            foreach (AccountsPostings accountPosting in accountsPostings)
            {
                _context.Entry(accountPosting).State = EntityState.Modified;
            }

            return _context.SaveChangesAsync();
        }

        public bool ValidarUsuario(int accountPostingId)
        {
            return GetAccountsPostings(accountPostingId).Any();
        }

        public bool AccountsPostingsExists(int id)
        {
            return GetAccountsPostings(id).Any();
        }

        public bool ValidateAccountAndUser(int accountId)
        {
            return _context.Accounts.Where(a => a.Id == accountId && a.UserId == _user.Id).Any();
        }

        public async Task<int> TransferBetweenAccounts(AccountsPostings accountPosting)
        {
            if (accountPosting == null) throw new ArgumentException("Dados da transferência não informados.");

            int fromAccountId = accountPosting.AccountId;
            int toAccountId   = accountPosting.ToAccountId ?? 0;

            if (fromAccountId <= 0 || toAccountId <= 0) throw new ArgumentException("Conta de origem e conta de destino são obrigatórias.");
            if (fromAccountId == toAccountId) throw new ArgumentException("A conta de origem deve ser diferente da conta de destino.");
            if (string.IsNullOrWhiteSpace(accountPosting.Reference)) throw new ArgumentException("Referência é obrigatória.");

            decimal amount = Math.Abs(accountPosting.Amount);

            if (amount <= 0) throw new ArgumentException("Valor da transferência deve ser maior que zero.");

            Accounts? fromAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == fromAccountId);
            Accounts? toAccount   = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == toAccountId);

            if (fromAccount == null || toAccount == null) throw new ArgumentException("Conta de origem e/ou conta de destino não encontrada.");
            if (fromAccount.UserId != _user.Id || toAccount.UserId != _user.Id) throw new ArgumentException("Não é permitido transferir entre contas de usuários diferentes.");

            AccountsDTO? fromTotals = null;

            try
            {
                fromTotals = _context.GetAccountTotals(fromAccountId, accountPosting.Reference, _user.Id).FirstOrDefault();
            }
            catch { /**/ }

            if (fromTotals == null) throw new InvalidOperationException("Não foi possível obter o saldo atual da conta de origem para validar a transferência.");

            decimal projectedBalance = fromTotals.TotalBalance - amount;

            if (projectedBalance < 0)
                throw new InvalidOperationException($"Transferência não permitida: saldo insuficiente. Saldo disponível: {fromTotals.TotalBalance:C}, necessário: {amount:C}");

            var (descOrigin, descDestination) = GetTransferDescriptions(fromAccount, toAccount);

            short nextPos = (short)((_context.AccountsPostings.Where(o => o.Reference == accountPosting.Reference).Max(o => o.Position) ?? 0) + 1);

            var originPosting = new AccountsPostings
            {
                AccountId   = fromAccountId,
                ToAccountId = toAccountId,
                Date        = accountPosting.Date,
                Reference   = accountPosting.Reference,
                Description = descOrigin,
                Amount      = amount * -1,
                Note        = accountPosting.Note,
                Type        = "P",
                Position    = nextPos
            };

            var destinationPosting = new AccountsPostings
            {
                AccountId   = toAccountId,
                ToAccountId = fromAccountId,
                Date        = accountPosting.Date,
                Reference   = accountPosting.Reference,
                Description = descDestination,
                Amount      = amount,
                Note        = accountPosting.Note,
                Type        = "R",
                Position    = (short)(nextPos + 1)
            };

            using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                _context.AccountsPostings.Add(originPosting);
                _context.AccountsPostings.Add(destinationPosting);

                // ✅ CORREÇÃO 5: SaveChanges único com RelatedId já definido (reduz race condition)
                // Aguarda geração dos IDs
                await _context.SaveChangesAsync();

                // Agora define os RelatedIds
                originPosting.RelatedId      = destinationPosting.Id;
                destinationPosting.RelatedId = originPosting.Id;

                _context.Entry(originPosting).State      = EntityState.Modified;
                _context.Entry(destinationPosting).State = EntityState.Modified;

                int rows = await _context.SaveChangesAsync();

                await tx.CommitAsync();

                // Preencher o mesmo objeto recebido, para a Controller retornar o id certo
                accountPosting.Id          = originPosting.Id;
                accountPosting.RelatedId   = originPosting.RelatedId;
                accountPosting.ToAccountId = originPosting.ToAccountId;

                return rows;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public IQueryable<AccountsYieldsDTO> GetAccountsYields(string? reference, int? accountId)
        {
            IQueryable<AccountsYieldsDTO> accountsYields = Enumerable.Empty<AccountsYieldsDTO>().AsQueryable();

            try
            {
                accountsYields = _context.GetAccountsYields(reference, accountId, _user.Id);
            }
            catch { /**/ }

            return accountsYields.OrderByDescending(y => y.RowNum);
        }
    }
}
