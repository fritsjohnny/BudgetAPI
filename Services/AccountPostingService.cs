using BudgetAPI.Data;
using BudgetAPI.Models;
using BudgetAPI.Helpers;
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
        Task<int> GenerateCardReceiptFromAccountPosting(int accountPostingId, int cardId, int peopleId);
        Task<decimal> GetPreviousYield(int accountId, string reference);
        Task<decimal> GetTotalPreviousYields(int accountId, string reference);
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

        // Método auxiliar para identificar transferências
        private bool IsTransfer(AccountsPostings posting)
        {
            // Uma transferência pode ser identificada por:
            // 1. Type="T" → Request do frontend para criar/editar transferência
            // 2. Type="P" ou "R" COM RelatedId → Lançamento de transferência já persistido no banco
            return posting.Type == "T" ||
                   (posting.RelatedId.HasValue && (posting.Type == "P" || posting.Type == "R"));
        }

        // Método auxiliar para gerar descrições padronizadas
        private (string originDesc, string destinationDesc) GetTransferDescriptions(Accounts fromAccount, Accounts toAccount)
        {
            string descOrigin      = $"Transferido para {toAccount.Name ?? "Conta destino"}";
            string descDestination = $"Recebido de {fromAccount.Name ?? "Conta origem"}";
            return (descOrigin, descDestination);
        }

        public async Task<int> PutAccountsPostings(AccountsPostings accountsPostings)
        {
            if (IsTransfer(accountsPostings))
            {
                return await UpdateTransferBetweenAccounts(accountsPostings);
            }

            AccountsPostings? entity = await _context.AccountsPostings.Include(a => a.Account)
                                                                          .Where(a => a.Id == accountsPostings.Id && a.Account!.UserId == _user.Id)
                                                                          .FirstOrDefaultAsync();

            if (entity == null)
                throw new Exception("Lançamento não encontrado.");

            // Se trocou a conta, exige que a nova conta pertença ao usuário e esteja ativa
            if (entity.AccountId != accountsPostings.AccountId)
            {
                var newAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountsPostings.AccountId && a.UserId == _user.Id);

                if (newAccount == null)
                    throw new ArgumentException("Conta inválida para o usuário atual.");

                if (newAccount.Disabled == true)
                    throw new InvalidOperationException($"Não é permitido alterar o lançamento para a conta desativada '{newAccount.Name}'.");
            }

            _context.Entry(entity).CurrentValues.SetValues(accountsPostings);

            return await _context.SaveChangesAsync();
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

            // Validar Type do lançamento no banco
            if (current.Type != "P" && current.Type != "R")
            {
                throw new InvalidOperationException($"Tipo de lançamento inválido: '{current.Type}'. Esperado 'P' ou 'R' para transferências.");
            }

            if (related.Type != "P" && related.Type != "R")
            {
                throw new InvalidOperationException($"Tipo do lançamento relacionado inválido: '{related.Type}'. Esperado 'P' ou 'R' para transferências.");
            }

            // Impedir mudança de Reference
            if (current.Reference != request.Reference)
            {
                throw new InvalidOperationException("Não é permitido alterar a referência (mês) de uma transferência. Delete e crie uma nova transferência.");
            }

            AccountsPostings origin      = (current.Type == "P") ? current : related;
            AccountsPostings destination = (origin.Id == current.Id) ? related : current;

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



            // Validação de saldo corrigida
            decimal oldAmount = Math.Abs(origin.Amount);
            int oldFromAccountId = origin.AccountId;
            int newFromAccountId = requestedFromAccountId;

            // Se houve troca de conta de origem ou destino, exige que a nova conta esteja ativa
            if (oldFromAccountId != newFromAccountId)
            {
                if (fromAccount.Disabled == true)
                    throw new InvalidOperationException($"Não é permitido alterar a transferência para a conta desativada '{fromAccount.Name}'.");
            }

            int oldToAccountId = destination.AccountId;
            if (oldToAccountId != requestedToAccountId)
            {
                if (toAccount.Disabled == true)
                    throw new InvalidOperationException($"Não é permitido alterar a transferência para a conta desativada '{toAccount.Name}'.");
            }

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

            // inclusão exige conta ativa
            var acc = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountsPostings.AccountId && a.UserId == _user.Id);

            if (acc == null)
                throw new ArgumentException("Conta inválida para o usuário atual.");

            if (acc.Disabled == true)
                throw new InvalidOperationException($"Não é permitido incluir registros na conta desativada '{acc.Name}'.");

            accountsPostings.Position = (short)((_context.AccountsPostings.Where(o => o.Reference == accountsPostings.Reference).Max(o => o.Position) ?? 0) + 1);

            _context.AccountsPostings.Add(accountsPostings);

            if (accountsPostings.ExpenseId != null && accountsPostings.Type == "P")
            {
                var expense = await _context.Expenses.FindAsync(accountsPostings.ExpenseId);

                if (expense != null)
                {
                    expense.Scheduled = false;
                }
            }

            return await _context.SaveChangesAsync();
        }

        public async Task<int> DeleteAccountsPostings(AccountsPostings accountsPostings)
        {
            // Verifica se é transferência usando método auxiliar
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
            // Atualizar apenas o campo Position de lançamentos já existentes e pertencentes ao usuário
            List<int> ids = accountsPostings.Select(a => a.Id).Distinct().ToList();

            List<AccountsPostings> savedPostings = _context.AccountsPostings
                                                      .Where(a => ids.Contains(a.Id) && a.Account!.UserId == _user.Id)
                                                      .ToList();

            if (savedPostings.Count != ids.Count)
            {
                throw new Exception("Erro no AccountPostingService.SetPositions: existem lançamentos inválidos para o usuário atual.");
            }

            foreach (AccountsPostings saved in savedPostings)
            {
                AccountsPostings? request = accountsPostings.FirstOrDefault(a => a.Id == saved.Id);

                if (request != null)
                {
                    saved.Position = request.Position;
                }
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

            // inclusão de transferência exige que ambas as contas estejam ativas
            if (fromAccount.Disabled == true)
                throw new InvalidOperationException($"Não é permitido incluir registros na conta desativada '{fromAccount.Name}'.");

            if (toAccount.Disabled == true)
                throw new InvalidOperationException($"Não é permitido incluir registros na conta desativada '{toAccount.Name}'.");

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

        public async Task<int> GenerateCardReceiptFromAccountPosting(int accountPostingId, int cardId, int peopleId)
        {
            if (accountPostingId == 0) throw new ArgumentNullException("Dados do lançamento não informados.");

            AccountsPostings? posting = await _context.AccountsPostings
                .Include(ap => ap.Account)
                .FirstOrDefaultAsync(ap =>
                    ap.Id == accountPostingId &&
                    ap.Account!.UserId == _user.Id);

            if (posting == null)
                throw new ArgumentException("Lançamento não encontrado no banco.");

            if (posting.CardReceiptId.HasValue)
                return posting.CardReceiptId.Value;

            if (posting.Type != "R") throw new ArgumentException("Apenas lançamentos do tipo 'R' podem gerar um comprovante de cartão.");

            await FinancialResourceValidator.ValidateCardForCreateAsync(
                _context,
                _user.Id,
                cardId);

            await FinancialResourceValidator.ValidateAccountForCreateAsync(
                _context,
                _user.Id,
                posting.AccountId);

            int? existingId = await _context.CardsReceipts.AsNoTracking()
                                                  .Where(x => x.Reference == posting.Reference
                                                           && x.CardId == cardId
                                                           && x.PeopleId == peopleId
                                                           && x.AccountId == posting.AccountId
                                                           && x.Amount == posting.Amount)
                                                  .Select(x => (int?)x.Id)
                                                  .FirstOrDefaultAsync();

            if (existingId.HasValue)
            {
                throw new InvalidOperationException($"Já existe um recebimento de cartão correspondente a este lançamento (ID do comprovante: {existingId.Value}).");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // valida cartão e conta antes de criar o recebimento
                Cards? card = await _context.Cards.FirstOrDefaultAsync(c => c.Id == cardId && c.UserId == _user.Id);
                if (card == null)
                    throw new ArgumentException("Cartão inválido para o usuário atual.");

                if (card.Disabled == true)
                    throw new InvalidOperationException($"Não é permitido incluir registros no cartão desativado '{card.Name}'.");

                Accounts? account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == posting.AccountId && a.UserId == _user.Id);
                if (account == null)
                    throw new ArgumentException("Conta inválida para o usuário atual.");

                if (account.Disabled == true)
                    throw new InvalidOperationException($"Não é permitido incluir registros na conta desativada '{account.Name}'.");

                CardsReceipts cardReceipt = new CardsReceipts
                {
                    Date      = posting.Date,
                    Reference = posting.Reference,
                    CardId    = cardId,
                    PeopleId  = peopleId,
                    AccountId = posting.AccountId,
                    Amount    = posting.Amount,
                    Note      = $"Gerado a partir do lançamento ID {posting.Id}"
                };

                _context.CardsReceipts.Add(cardReceipt);

                await _context.SaveChangesAsync(); // gera o Id

                posting.CardReceiptId = cardReceipt.Id;

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return cardReceipt.Id;
            }

            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<decimal> GetPreviousYield(int accountId, string reference)
        {
            decimal previousYield = await _context.AccountsPostings
                .Where(a => a.AccountId == accountId
                         && a.Account!.UserId == _user.Id
                         && (a.Type == "Y" || a.Type == "y")
                         && a.Reference.CompareTo(reference) <= 0)
                .OrderByDescending(a => a.Reference)
                .ThenByDescending(a => a.Date)
                .ThenByDescending(a => a.Id)
                .Select(a => a.Amount)
                .FirstOrDefaultAsync();

            return previousYield;
        }

        public async Task<decimal> GetTotalPreviousYields(int accountId, string reference)
        {
            DateTime? dateApplied = await (
                                            from aa in _context.AccountsApplications
                                            join a in _context.Accounts on aa.AccountId equals a.Id
                                            where aa.AccountId == accountId
                                               && a.UserId == _user.Id
                                            orderby aa.DateApplied descending, aa.Id descending
                                            select (DateTime?)aa.DateApplied
                                        ).FirstOrDefaultAsync();

            if (!dateApplied.HasValue)
            {
                return 0;
            }

            decimal totalPreviousYields = await (
                                                from ap in _context.AccountsPostings
                                                join a in _context.Accounts on ap.AccountId equals a.Id
                                                where ap.AccountId == accountId
                                                   && a.UserId == _user.Id
                                                   && (ap.Type == "Y" || ap.Type == "y")
                                                   && ap.Date >= dateApplied.Value
                                                   && ap.Reference.CompareTo(reference) <= 0
                                                select ap.Amount
                                                ).SumAsync();

            return totalPreviousYields;
        }
    }
}
