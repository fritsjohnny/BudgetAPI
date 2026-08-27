using System.Data;
using System.Text.Json;
using BudgetAPI.Data;
using BudgetAPI.Helpers;
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
        Task ReorderPositionsByDate(int accountId, string reference);
        bool ValidarUsuario(int accountPostingId);
        bool AccountsPostingsExists(int id);
        bool ValidateAccountAndUser(int accountId);
        IQueryable<AccountsYieldsDTO> GetAccountsYields(string? reference, int? accountId);
        Task<int> TransferBetweenAccounts(AccountsPostings accountPosting);
        Task<int> GenerateCardReceiptFromAccountPosting(int accountPostingId, int cardId, int peopleId);
        Task<decimal> GetPreviousYield(int accountId, string reference);
        Task<decimal> GetTotalPreviousYields(int accountId, string reference);
        Task<AccountHistoricalBalanceDTO> GetHistoricalBalance(int accountId, DateTime date, int? excludePostingId);
        Task<AccountHistoricalBalanceDTO> GetHistoricalApplicationBalance(int accountApplicationId, DateTime date, int? excludePostingId);
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
            return _context.AccountsPostings.Include(a => a.Account).Include(a => a.ApplicationDetails)
                                            .Where(a => a.Account!.UserId == _user.Id)
                                            .OrderBy(a => a.Position);
        }

        public IQueryable<AccountsPostings> GetAccountsPostings(int id)
        {
            IQueryable<AccountsPostings>? accountsPostings = _context.AccountsPostings.Include(a => a.Account).Include(a => a.ApplicationDetails)
                                                                                      .Where(a => a.Id == id && a.Account!.UserId == _user.Id);

            return accountsPostings;
        }

        public IQueryable<AccountsPostings> GetAccountsPostings(int accountId, string reference)
        {
            IOrderedQueryable<AccountsPostings>? accountsPostings = _context.AccountsPostings.Include(a => a.Account).Include(a => a.ApplicationDetails)
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

        private static void NormalizeYieldFields(AccountsPostings posting)
        {
            if (string.Equals(posting.Type, "Y", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            posting.GrossAmount       = null;
            posting.TotalGrossBalance = null;
            posting.TotalBalance      = null;
            posting.TotalIOF          = null;
            posting.TotalIR           = null;
            posting.IOFElapsedDays    = null;
        }

        private async Task<List<AccountsPostingApplicationDetails>> PrepareYieldApplicationDetailsAsync(AccountsPostings posting)
        {
            if (!string.Equals(posting.Type, "Y", StringComparison.OrdinalIgnoreCase))
            {
                return new List<AccountsPostingApplicationDetails>();
            }

            List<AccountsPostingApplicationDetails> requestDetails = (posting.ApplicationDetails ?? new List<AccountsPostingApplicationDetails>())
                .Select(detail => new AccountsPostingApplicationDetails
                {
                    AccountApplicationId = detail.AccountApplicationId,
                    Amount               = Math.Round(detail.Amount, 2),
                    GrossAmount          = detail.GrossAmount.HasValue ? Math.Round(detail.GrossAmount.Value, 2) : null,
                    TotalGrossBalance    = detail.TotalGrossBalance.HasValue ? Math.Round(detail.TotalGrossBalance.Value, 2) : null,
                    TotalBalance         = detail.TotalBalance.HasValue ? Math.Round(detail.TotalBalance.Value, 2) : null,
                    TotalIOF             = detail.TotalIOF.HasValue ? Math.Round(detail.TotalIOF.Value, 2) : null,
                    TotalIR              = detail.TotalIR.HasValue ? Math.Round(detail.TotalIR.Value, 2) : null,
                    IOFElapsedDays       = detail.IOFElapsedDays
                })
                .ToList();

            if (requestDetails.Count == 0)
            {
                if (!posting.TotalBalance.HasValue && posting.TotalGrossBalance.HasValue)
                {
                    posting.TotalBalance = Math.Round(posting.TotalGrossBalance.Value - (posting.TotalIOF ?? 0) - (posting.TotalIR ?? 0), 2);
                }

                return requestDetails;
            }

            if (requestDetails.Any(detail => detail.AccountApplicationId <= 0) ||
                requestDetails.Select(detail => detail.AccountApplicationId).Distinct().Count() != requestDetails.Count)
            {
                throw new ArgumentException("As aplicações do rendimento são inválidas ou estão duplicadas.");
            }

            List<int> applicationIds = requestDetails.Select(detail => detail.AccountApplicationId).ToList();
            List<AccountsApplications> applications = await _context.AccountsApplications
                .Where(application => application.AccountId == posting.AccountId && applicationIds.Contains(application.Id))
                .ToListAsync();

            if (applications.Count != applicationIds.Count)
            {
                throw new ArgumentException("Uma aplicação informada não pertence à conta do lançamento.");
            }

            HashSet<int> existingApplicationIds = posting.Id > 0
                ? (await _context.AccountsPostingApplicationDetails
                    .Where(detail => detail.AccountPostingId == posting.Id)
                    .Select(detail => detail.AccountApplicationId)
                    .ToListAsync())
                    .ToHashSet()
                : new HashSet<int>();

            if (applications.Any(application => application.Disabled && !existingApplicationIds.Contains(application.Id)))
            {
                throw new InvalidOperationException("Não é permitido adicionar uma aplicação desativada ao rendimento.");
            }

            posting.Amount = requestDetails.Sum(detail => detail.Amount);

            if (requestDetails.All(detail => detail.GrossAmount.HasValue))
            {
                posting.GrossAmount = requestDetails.Sum(detail => detail.GrossAmount!.Value);
            }

            if (requestDetails.All(detail => detail.TotalGrossBalance.HasValue))
            {
                posting.TotalGrossBalance = requestDetails.Sum(detail => detail.TotalGrossBalance!.Value);
            }

            if (requestDetails.All(detail => detail.TotalIOF.HasValue))
            {
                posting.TotalIOF = requestDetails.Sum(detail => detail.TotalIOF!.Value);
            }

            if (requestDetails.All(detail => detail.TotalIR.HasValue))
            {
                posting.TotalIR = requestDetails.Sum(detail => detail.TotalIR!.Value);
            }

            if (requestDetails.All(detail => detail.TotalBalance.HasValue))
            {
                posting.TotalBalance = requestDetails.Sum(detail => detail.TotalBalance!.Value);
            }
            else if (!posting.TotalBalance.HasValue && posting.TotalGrossBalance.HasValue)
            {
                posting.TotalBalance = Math.Round(posting.TotalGrossBalance.Value - (posting.TotalIOF ?? 0) - (posting.TotalIR ?? 0), 2);
            }

            List<int> elapsedDays = requestDetails
                .Where(detail => detail.IOFElapsedDays.HasValue)
                .Select(detail => detail.IOFElapsedDays!.Value)
                .ToList();

            if (elapsedDays.Count > 0)
            {
                posting.IOFElapsedDays = elapsedDays.Max();
            }

            return requestDetails;
        }

        private static string SerializeYieldApplicationDetails(IEnumerable<AccountsPostingApplicationDetails> details)
        {
            return JsonSerializer.Serialize(details.Select(detail => new
            {
                a = detail.AccountApplicationId,
                m = detail.Amount,
                g = detail.GrossAmount,
                gb = detail.TotalGrossBalance,
                b = detail.TotalBalance,
                iof = detail.TotalIOF,
                ir = detail.TotalIR,
                d = detail.IOFElapsedDays
            }));
        }

        private async Task<int> SaveChangesWithYieldTriggerAsync(AccountsPostings posting, IReadOnlyCollection<AccountsPostingApplicationDetails> applicationDetails)
        {
            if (!string.Equals(posting.Type, "Y", StringComparison.OrdinalIgnoreCase) || applicationDetails.Count == 0)
            {
                return await _context.SaveChangesAsync();
            }

            string detailsJson = SerializeYieldApplicationDetails(applicationDetails);

            if (detailsJson.Length * sizeof(char) > 8000)
            {
                throw new InvalidOperationException("A quantidade de aplicações do rendimento excede o limite suportado para persistência em uma única operação.");
            }

            System.Data.Common.DbConnection connection = _context.Database.GetDbConnection();
            bool closeConnection = connection.State != ConnectionState.Open;

            if (closeConnection)
            {
                await _context.Database.OpenConnectionAsync();
            }

            try
            {
                await _context.Database.ExecuteSqlRawAsync("EXEC sys.sp_set_session_context @key=N'BudgetYieldApplicationDetails', @value={0};", detailsJson);
                return await _context.SaveChangesAsync();
            }
            finally
            {
                try
                {
                    await _context.Database.ExecuteSqlRawAsync("EXEC sys.sp_set_session_context @key=N'BudgetYieldApplicationDetails', @value=NULL;");
                }
                finally
                {
                    if (closeConnection)
                    {
                        await connection.CloseAsync();
                    }
                }
            }
        }

        // Método auxiliar para gerar descrições padronizadas
        private (string originDesc, string destinationDesc) GetTransferDescriptions(Accounts fromAccount, Accounts toAccount)
        {
            string descOrigin = $"Transferido para {toAccount.Name ?? "Conta destino"}";
            string descDestination = $"Recebido de {fromAccount.Name ?? "Conta origem"}";

            return (descOrigin, descDestination);
        }

        public async Task<int> PutAccountsPostings(AccountsPostings accountsPostings)
        {
            NormalizeYieldFields(accountsPostings);

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

            List<AccountsPostingApplicationDetails> applicationDetails = await PrepareYieldApplicationDetailsAsync(accountsPostings);

            _context.Entry(entity).CurrentValues.SetValues(accountsPostings);

            if (applicationDetails.Count > 0 && string.Equals(entity.Type, "Y", StringComparison.OrdinalIgnoreCase))
            {
                _context.Entry(entity).Property(posting => posting.TotalBalance).IsModified = true;
            }

            return await SaveChangesWithYieldTriggerAsync(entity, applicationDetails);
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

            AccountsPostings origin = (current.Type == "P") ? current : related;
            AccountsPostings destination = (origin.Id == current.Id) ? related : current;

            // Mapear os dados do request para os lançamentos corretos
            int requestedFromAccountId;
            int requestedToAccountId;

            if (current.Type == "P")
            {
                // Request veio do lançamento de origem
                requestedFromAccountId = request.AccountId;
                requestedToAccountId   = request.ToAccountId!.Value;
            }
            else
            {
                // Request veio do lançamento de destino - inverter
                requestedFromAccountId = request.ToAccountId!.Value;
                requestedToAccountId   = request.AccountId;
            }

            Accounts? fromAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == requestedFromAccountId);
            Accounts? toAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == requestedToAccountId);

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
                decimal projectedBalance = balanceWithoutThisTransfer - newAmount;

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

                NormalizeYieldFields(origin);
                NormalizeYieldFields(destination);

                _context.Entry(origin).State = EntityState.Modified;
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
            NormalizeYieldFields(accountsPostings);

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

            List<AccountsPostingApplicationDetails> applicationDetails = await PrepareYieldApplicationDetailsAsync(accountsPostings);

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                accountsPostings.ApplicationDetails = new List<AccountsPostingApplicationDetails>();
                _context.AccountsPostings.Add(accountsPostings);

                if (accountsPostings.ExpenseId != null && accountsPostings.Type == "P")
                {
                    Expenses? expense = await _context.Expenses.FindAsync(accountsPostings.ExpenseId);

                    if (expense != null)
                    {
                        expense.Scheduled = false;
                    }
                }

                await SaveChangesWithYieldTriggerAsync(accountsPostings, applicationDetails);
                await ReorderPositionsByDateCore(accountsPostings.AccountId, accountsPostings.Reference!);
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

        public async Task ReorderPositionsByDate(int accountId, string reference)
        {
            if (!ValidateAccountAndUser(accountId))
            {
                throw new InvalidOperationException("Conta não encontrada para o usuário atual.");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await ReorderPositionsByDateCore(accountId, reference);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task ReorderPositionsByDateCore(int accountId, string reference)
        {
            List<AccountsPostings> postings = await _context.AccountsPostings
                .Where(ap => ap.AccountId == accountId
                          && ap.Reference == reference
                          && ap.Account!.UserId == _user.Id)
                .OrderBy(ap => ap.Date)
                .ThenBy(ap => ap.Type == "Y" || ap.Type == "y" ? 0 : 1)
                .ThenBy(ap => ap.Position)
                .ThenBy(ap => ap.Id)
                .ToListAsync();

            if (postings.Count > short.MaxValue + 1)
            {
                throw new InvalidOperationException("A quantidade de lançamentos excede o limite de posições permitido.");
            }

            for (int index = 0; index < postings.Count; index++)
            {
                postings[index].Position = checked((short)index);
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
            int toAccountId = accountPosting.ToAccountId ?? 0;

            if (fromAccountId <= 0 || toAccountId <= 0) throw new ArgumentException("Conta de origem e conta de destino são obrigatórias.");
            if (fromAccountId == toAccountId) throw new ArgumentException("A conta de origem deve ser diferente da conta de destino.");
            if (string.IsNullOrWhiteSpace(accountPosting.Reference)) throw new ArgumentException("Referência é obrigatória.");

            decimal amount = Math.Abs(accountPosting.Amount);

            if (amount <= 0) throw new ArgumentException("Valor da transferência deve ser maior que zero.");

            Accounts? fromAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == fromAccountId);
            Accounts? toAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == toAccountId);

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

            NormalizeYieldFields(originPosting);
            NormalizeYieldFields(destinationPosting);

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

                _context.Entry(originPosting).State = EntityState.Modified;
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

        public async Task<AccountHistoricalBalanceDTO> GetHistoricalApplicationBalance(int accountApplicationId, DateTime date, int? excludePostingId)
        {
            DateTime limitDate = date.Date;
            DateTime nextDate = limitDate.AddDays(1);

            AccountsApplications? application = await _context.AccountsApplications
                .FirstOrDefaultAsync(item => item.Id == accountApplicationId);

            if (application == null ||
                !await _context.Accounts.AnyAsync(account =>
                    account.Id == application.AccountId &&
                    account.UserId == _user.Id))
            {
                throw new ArgumentException("Aplicação inválida para o usuário atual.");
            }

            IQueryable<AccountsPostingApplicationDetails> query = _context.AccountsPostingApplicationDetails
                .AsNoTracking()
                .Include(detail => detail.AccountPosting)
                .Where(detail => detail.AccountApplicationId == accountApplicationId
                              && detail.AccountPosting!.Account!.UserId == _user.Id
                              && detail.AccountPosting.Type == "Y"
                              && detail.AccountPosting.Date < nextDate);

            if (excludePostingId.HasValue)
            {
                query = query.Where(detail => detail.AccountPostingId != excludePostingId.Value);
            }

            List<AccountsPostingApplicationDetails> details = await query
                .OrderBy(detail => detail.AccountPosting!.Date)
                .ThenBy(detail => detail.AccountPosting!.Position)
                .ThenBy(detail => detail.AccountPostingId)
                .ToListAsync();

            if (details.Count == 0)
            {
                return new AccountHistoricalBalanceDTO
                {
                    Balance = application.AmountApplied,
                    GrossBalance = application.AmountApplied
                };
            }

            AccountsPostingApplicationDetails firstDetail = details[0];
            decimal reconstructedGrossBalance = firstDetail.TotalGrossBalance
                ?? Math.Round(application.AmountApplied + (firstDetail.GrossAmount ?? 0), 2);

            for (int index = 1; index < details.Count; index++)
            {
                AccountsPostingApplicationDetails currentDetail = details[index];

                if (currentDetail.GrossAmount.HasValue)
                {
                    reconstructedGrossBalance = Math.Round(reconstructedGrossBalance + currentDetail.GrossAmount.Value, 2);
                }
                else if (currentDetail.TotalGrossBalance.HasValue)
                {
                    reconstructedGrossBalance = currentDetail.TotalGrossBalance.Value;
                }
            }

            AccountsPostingApplicationDetails detail = details[^1];

            decimal reconstructedBalance;
            if (detail.TotalBalance.HasValue && detail.TotalGrossBalance.HasValue)
            {
                reconstructedBalance = Math.Round(
                    detail.TotalBalance.Value + reconstructedGrossBalance - detail.TotalGrossBalance.Value,
                    2);
            }
            else if (detail.TotalIOF.HasValue && detail.TotalIR.HasValue)
            {
                reconstructedBalance = Math.Round(
                    reconstructedGrossBalance - detail.TotalIOF.Value - detail.TotalIR.Value,
                    2);
            }
            else
            {
                reconstructedBalance = detail.TotalBalance ?? reconstructedGrossBalance;
            }

            return new AccountHistoricalBalanceDTO
            {
                Balance = reconstructedBalance,
                GrossBalance = reconstructedGrossBalance,
                TotalIOF = detail.TotalIOF,
                TotalIR = detail.TotalIR,
                IOFElapsedDays = detail.IOFElapsedDays,
                PostingDate = detail.AccountPosting?.Date
            };
        }

        public async Task<AccountHistoricalBalanceDTO> GetHistoricalBalance(int accountId, DateTime date, int? excludePostingId)
        {
            DateTime limitDate = date.Date;

            IQueryable<AccountsPostings> postingsBeforeDate = _context.AccountsPostings
                .AsNoTracking()
                .Where(ap => ap.AccountId == accountId
                          && ap.Account!.UserId == _user.Id
                          && ap.Date < limitDate);

            if (excludePostingId.HasValue)
            {
                postingsBeforeDate = postingsBeforeDate.Where(ap => ap.Id != excludePostingId.Value);
            }

            AccountsPostings? lastYield = await postingsBeforeDate
                .Where(ap => ap.Type == "Y" || ap.Type == "y")
                .OrderByDescending(ap => ap.Date)
                .ThenByDescending(ap => ap.Position)
                .ThenByDescending(ap => ap.Id)
                .FirstOrDefaultAsync();

            if (lastYield == null)
            {
                return new AccountHistoricalBalanceDTO
                {
                    Balance = await postingsBeforeDate.SumAsync(ap => ap.Amount),
                    GrossBalance = await postingsBeforeDate.SumAsync(ap =>
                        ap.Type == "Y" || ap.Type == "y"
                            ? ap.GrossAmount ?? ap.Amount
                            : ap.Amount)
                };
            }

            decimal? detailGrossBalance = await _context.AccountsPostingApplicationDetails
                .Where(detail => detail.AccountPostingId == lastYield.Id)
                .Select(detail => (decimal?)detail.TotalGrossBalance)
                .SumAsync();

            decimal? detailBalance = await _context.AccountsPostingApplicationDetails
                .Where(detail => detail.AccountPostingId == lastYield.Id)
                .Select(detail => (decimal?)detail.TotalBalance)
                .SumAsync();

            bool hasDetailGrossBalance = detailGrossBalance.HasValue;

            if (!hasDetailGrossBalance
                && !lastYield.TotalGrossBalance.HasValue
                && !lastYield.TotalBalance.HasValue)
            {
                return new AccountHistoricalBalanceDTO
                {
                    Balance = await postingsBeforeDate.SumAsync(ap => ap.Amount),
                    GrossBalance = await postingsBeforeDate.SumAsync(ap =>
                        ap.Type == "Y" || ap.Type == "y"
                            ? ap.GrossAmount ?? ap.Amount
                            : ap.Amount)
                };
            }

            IQueryable<AccountsPostings> postingsAfterLastYield = postingsBeforeDate
                .Where(ap => ap.Date > lastYield.Date
                          || (ap.Date == lastYield.Date
                              && (ap.Position > lastYield.Position
                                  || (ap.Position == lastYield.Position && ap.Id > lastYield.Id))));

            decimal balanceAfterLastYield = await postingsAfterLastYield.SumAsync(ap => ap.Amount);
            decimal grossBalanceAfterLastYield = await postingsAfterLastYield.SumAsync(ap =>
                ap.Type == "Y" || ap.Type == "y"
                    ? ap.GrossAmount ?? ap.Amount
                    : ap.Amount);

            decimal confirmedGrossBalance = hasDetailGrossBalance
                ? detailGrossBalance!.Value
                : lastYield.TotalGrossBalance ?? lastYield.TotalBalance ?? lastYield.Amount;

            decimal confirmedBalance = detailBalance.HasValue
                ? detailBalance.Value
                : lastYield.TotalBalance
                    ?? (confirmedGrossBalance - (lastYield.TotalIOF ?? 0) - (lastYield.TotalIR ?? 0));

            return new AccountHistoricalBalanceDTO
            {
                Balance = confirmedBalance + balanceAfterLastYield,
                GrossBalance = confirmedGrossBalance + grossBalanceAfterLastYield
            };
        }

        public async Task<decimal> GetTotalPreviousYields(int accountId, string reference)
        {
            DateTime? dateApplied = await (
                                            from aa in _context.AccountsApplications
                                            join a in _context.Accounts on aa.AccountId equals a.Id
                                            where aa.AccountId == accountId
                                               && !aa.Disabled
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
