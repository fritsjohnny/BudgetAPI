using BudgetAPI.Data;
using BudgetAPI.Helpers;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface IIncomeService
    {
        IQueryable<Incomes> GetIncomes();
        IQueryable<Incomes> GetIncomes(int id);
        IQueryable<IncomesDTO> GetIncomesDTO(int id);
        IQueryable<IncomesDTO> GetIncomes(string reference);
        IQueryable<IncomesDTO> GetMyIncomes(string reference);
        IQueryable<IncomesDTO2> GetIncomesComboList(string reference);
        Task<int> PutIncomes(Incomes incomes, bool repeatToNextMonths = false);
        Task PutIncomesAllParcels(Incomes incomes);
        Task PutIncomesWithParcels(Incomes incomes, int qtyMonths);
        Task<int> SetPositions(List<Incomes> incomes);
        Task<int> AddValue(Incomes income, decimal value);
        Task<int> PostIncomes(Incomes income);
        Task PostIncomesAllParcels(Incomes incomes);
        Task PostIncomesWithParcels(Incomes incomes, int qtyMonths);
        Task<int> DeleteIncomes(Incomes income);
        bool IncomesExists(int id);
        bool ValidarUsuario(int incomeId);
        Task OrderByPreviousMonth(string reference);
        Task RepeatPreviousMonth(string reference);
    }

    public class IncomeService : IIncomeService
    {
        private readonly BudgetContext _context;

        private readonly Users _user;

        public IncomeService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _user    = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
        }

        public IQueryable<Incomes> GetIncomes()
        {
            return _context.Incomes.OrderBy(e => e.Position);
        }

        public IQueryable<Incomes> GetIncomes(int id)
        {
            IQueryable<Incomes>? incomes = _context.Incomes.Where(e => e.Id == id && e.UserId == _user.Id);

            return incomes;
        }

        public IQueryable<IncomesDTO> GetIncomesDTO(int id)
        {
            IQueryable<IncomesDTO>? incomes = _context.Incomes.Where(e => e.Id == id && e.UserId == _user.Id)
                                                              .Select(e => IncomesToDTO(e));

            return incomes;
        }

        public IQueryable<IncomesDTO> GetIncomes(string reference)
        {
            IQueryable<IncomesDTO>? incomes = _context.Incomes.Where(e => e.Reference == reference && e.UserId == _user.Id)
                                                              .OrderBy(e => e.Position)
                                                              .Select(e => IncomesToDTO(e));

            return incomes;
        }

        public IQueryable<IncomesDTO> GetMyIncomes(string reference)
        {
            IQueryable<IncomesDTO>? incomes = _context.Incomes.Where(e => e.Reference == reference &&
                                                                                e.UserId == _user.Id &&
                                                                                e.PeopleId == null &&
                                                                                e.CardId == null)
                                                              .OrderBy(e => e.Position)
                                                              .Select(e => IncomesToDTO(e));

            return incomes;
        }

        public IQueryable<IncomesDTO2> GetIncomesComboList(string reference)
        {
            IQueryable<IncomesDTO2>? incomes = _context.Incomes.Where(e => e.Reference == reference && e.UserId == _user.Id)
                                                                  .OrderBy(e => e.Position)
                                                                  .Select(e => IncomesToComboList(e));

            return incomes;
        }

        public async  Task<int> PutIncomes(Incomes income, bool repeatToNextMonths = false)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                Incomes? savedIncome = await _context.Incomes.AsNoTracking()
                                                     .Where(i => i.Id == income.Id && i.UserId == _user.Id)
                                                     .FirstOrDefaultAsync();

                if (savedIncome == null)
                {
                    throw new Exception("Receita não encontrada para o usuário atual.");
                }

                await FinancialResourceValidator.ValidateResourcesForUpdateAsync(
                    _context,
                    _user.Id,
                    savedIncome.CardId,
                    income.CardId,
                    savedIncome.AccountId,
                    income.AccountId);

                string originalDescription = (savedIncome.Description ?? string.Empty).Trim();
                string originalReference   = savedIncome.Reference;

                income.UserId = _user.Id;

                if (income.TotalToReceive == 0)
                {
                    income.TotalToReceive = income.ToReceive;
                }

                bool preserveFutureIncomeValues = IsYieldIncome(income);

                if (repeatToNextMonths && !preserveFutureIncomeValues)
                {
                    income.ToReceive = GetFutureToReceive(income, income);
                }

                _context.Entry(income).State = EntityState.Modified;

                if (repeatToNextMonths)
                {
                    List<Incomes> futureIncomes = await _context.Incomes.Where(i =>
                                                                       i.UserId == _user.Id &&
                                                                       i.Id != income.Id &&
                                                                       i.Received == 0 &&
                                                                       i.Description != null &&
                                                                       i.Description.Trim() == originalDescription &&
                                                                       string.Compare(i.Reference, originalReference) > 0)
                                                                .ToListAsync();

                    foreach (Incomes item in futureIncomes)
                    {
                        await FinancialResourceValidator.ValidateResourcesForUpdateAsync(
                            _context,
                            _user.Id,
                            item.CardId,
                            income.CardId,
                            item.AccountId,
                            income.AccountId);

                        item.Description = income.Description;
                        item.Note        = income.Note;
                        item.CardId      = income.CardId;
                        item.AccountId   = income.AccountId;
                        item.Type        = income.Type;
                        item.PeopleId    = income.PeopleId;

                        if (!preserveFutureIncomeValues)
                        {
                            item.ToReceive      = GetFutureToReceive(income, item);
                            item.TotalToReceive = income.TotalToReceive;
                        }
                    }
                }

                int result = await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task PutIncomesAllParcels(
            Incomes income)
        {
            using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                Incomes? savedIncome = await _context.Incomes
                    .FirstOrDefaultAsync(i =>
                        i.Id == income.Id &&
                        i.UserId == _user.Id);

                if (savedIncome == null)
                {
                    throw new InvalidOperationException(
                        "Receita não encontrada para o usuário atual.");
                }

                await FinancialResourceValidator.ValidateResourcesForUpdateAsync(
                    _context,
                    _user.Id,
                    savedIncome.CardId,
                    income.CardId,
                    savedIncome.AccountId,
                    income.AccountId);

                income.UserId = _user.Id;
                income.RelatedId = savedIncome.RelatedId;

                if (!income.ParcelNumber.HasValue || income.ParcelNumber.Value <= 0)
                {
                    income.ParcelNumber = 1;
                }

                if (!income.Parcels.HasValue || income.Parcels.Value <= 0)
                {
                    income.Parcels = 1;
                }

                if (income.TotalToReceive == 0)
                {
                    income.TotalToReceive =
                        income.ToReceive;
                }

                List<Incomes> incomesList =
                    GenerateIncomes(income);

                Incomes? currentGeneratedIncome =
                    incomesList.FirstOrDefault();

                if (currentGeneratedIncome != null)
                {
                    income.ToReceive =
                        currentGeneratedIncome.ToReceive;
                }

                List<Incomes> generatedFutureIncomes =
                    incomesList
                        .Where(i =>
                            i.ParcelNumber.HasValue &&
                            income.ParcelNumber.HasValue &&
                            i.ParcelNumber.Value >
                            income.ParcelNumber.Value)
                        .ToList();

                if (generatedFutureIncomes.Any())
                {
                    await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                        _context,
                        _user.Id,
                        income.CardId,
                        income.AccountId);
                }

                int relatedId =
                    savedIncome.RelatedId ??
                    savedIncome.Id;

                List<Incomes> futureIncomesToRemove =
                    await _context.Incomes
                        .Where(i =>
                            i.UserId == _user.Id &&
                            i.Id != savedIncome.Id &&
                            (
                                i.RelatedId == relatedId ||
                                i.Id == relatedId
                            ) &&
                            i.ParcelNumber.HasValue &&
                            income.ParcelNumber.HasValue &&
                            i.ParcelNumber.Value >
                            income.ParcelNumber.Value &&
                            i.Received == 0)
                        .ToListAsync();

                if (futureIncomesToRemove.Any())
                {
                    _context.Incomes.RemoveRange(
                        futureIncomesToRemove);

                    await _context.SaveChangesAsync();
                }

                _context.Entry(savedIncome)
                    .CurrentValues
                    .SetValues(income);

                foreach (Incomes item in generatedFutureIncomes)
                {
                    bool alreadyExists =
                        await _context.Incomes.AnyAsync(i =>
                            i.UserId == _user.Id &&
                            i.Id != savedIncome.Id &&
                            (
                                i.RelatedId == relatedId ||
                                i.Id == relatedId
                            ) &&
                            i.ParcelNumber == item.ParcelNumber &&
                            i.Parcels == item.Parcels);

                    if (alreadyExists)
                    {
                        continue;
                    }

                    item.UserId = _user.Id;
                    item.RelatedId = relatedId;

                    _context.Incomes.Add(item);
                }

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task PutIncomesWithParcels(Incomes incomes, int qtyMonths)
        {
            Incomes? savedIncome = await _context.Incomes
                .AsNoTracking()
                .FirstOrDefaultAsync(i =>
                    i.Id == incomes.Id &&
                    i.UserId == _user.Id);

            if (savedIncome == null)
            {
                throw new InvalidOperationException(
                    "Receita não encontrada para o usuário atual.");
            }

            await FinancialResourceValidator.ValidateResourcesForUpdateAsync(
                _context,
                _user.Id,
                savedIncome.CardId,
                incomes.CardId,
                savedIncome.AccountId,
                incomes.AccountId);

            var incomesList = RepeatIncomes(incomes, qtyMonths);

            bool createsNewRecords = incomesList.Skip(1).Any();

            if (createsNewRecords)
            {
                await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                    _context,
                    _user.Id,
                    incomes.CardId,
                    incomes.AccountId);
            }

            incomes.UserId = _user.Id;

            _context.Entry(incomes).State = EntityState.Modified;

            foreach (Incomes cp in incomesList.Skip(1))
            {
                cp.UserId = _user.Id;

                _context.Incomes.Add(cp);
            }

            await _context.SaveChangesAsync();
        }

        public Task<int> SetPositions(List<Incomes> incomes)
        {
            // Atualizar apenas o campo Position de registros carregados do banco
            List<int> ids = incomes.Select(i => i.Id).Distinct().ToList();

            List<Incomes> saved = _context.Incomes
                                .Where(i => ids.Contains(i.Id) && i.UserId == _user.Id)
                                .ToList();

            if (saved.Count != ids.Count)
                throw new Exception("Erro no IncomeService.SetPositions: existem receitas inválidas para o usuário atual.");

            foreach (Incomes s in saved)
            {
                Incomes? req = incomes.FirstOrDefault(i => i.Id == s.Id);
                if (req != null)
                    s.Position = req.Position;
            }

            return _context.SaveChangesAsync();
        }

        public Task<int> AddValue(Incomes income, decimal value)
        {
            income.ToReceive += value;

            if (income.TotalToReceive == 0)
            {
                income.TotalToReceive = income.ToReceive;
            }
            else
            {
                income.TotalToReceive += value;
            }

            _context.Entry(income).State = EntityState.Modified;

            return _context.SaveChangesAsync();
        }

        public async Task<int> PostIncomes(Incomes income)
        {
            await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                _context,
                _user.Id,
                income.CardId,
                income.AccountId);

            income.UserId = _user.Id;

            if (income.TotalToReceive == 0)
            {
                income.TotalToReceive = income.ToReceive;
            }

            _context.Incomes.Add(income);

            return await _context.SaveChangesAsync();
        }

        public async Task PostIncomesAllParcels(
            Incomes income)
        {
            using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                    _context,
                    _user.Id,
                    income.CardId,
                    income.AccountId);

                income.UserId = _user.Id;

                if (!income.ParcelNumber.HasValue ||
                    income.ParcelNumber.Value <= 0)
                {
                    income.ParcelNumber = 1;
                }

                if (!income.Parcels.HasValue ||
                    income.Parcels.Value <= 0)
                {
                    income.Parcels = 1;
                }

                if (income.TotalToReceive == 0)
                {
                    income.TotalToReceive =
                        income.ToReceive;
                }

                List<Incomes> incomesList =
                    GenerateIncomes(income);

                Incomes? firstIncome = null;

                foreach (Incomes item in incomesList)
                {
                    item.UserId = _user.Id;

                    if (firstIncome != null)
                    {
                        item.RelatedId =
                            firstIncome.Id;
                    }

                    _context.Incomes.Add(item);

                    await _context.SaveChangesAsync();

                    if (firstIncome == null)
                    {
                        firstIncome = item;

                        income.Id =
                            firstIncome.Id;

                        income.ToReceive =
                            firstIncome.ToReceive;

                        income.TotalToReceive =
                            firstIncome.TotalToReceive;
                    }
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task PostIncomesWithParcels(Incomes incomes, int qtyMonths)
        {
            await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                _context,
                _user.Id,
                incomes.CardId,
                incomes.AccountId);

            var incomesList = RepeatIncomes(incomes, qtyMonths);

            Incomes? firstIncomes = null;

            foreach (Incomes cp in incomesList)
            {
                cp.UserId = _user.Id;

                // Set RelatedId for all except the first one
                if (firstIncomes != null)
                {
                    cp.RelatedId = firstIncomes.Id;
                }

                _context.Incomes.Add(cp);
                await _context.SaveChangesAsync();

                if (firstIncomes == null)
                {
                    firstIncomes = cp;

                    // Update the input object with the details of the first Incomes
                    incomes.Id = firstIncomes.Id;
                }
            }
        }

        public Task<int> DeleteIncomes(Incomes income)
        {
            int relatedId = income.RelatedId ?? income.Id;

            List<Incomes> incomesToRemove = _context.Incomes.Where(i =>
                                                      i.UserId == _user.Id &&
                                                      (
                                                          i.Id == income.Id ||
                                                          (
                                                              (i.RelatedId == relatedId || i.Id == relatedId) &&
                                                              string.Compare(i.Reference, income.Reference) > 0 &&
                                                              i.Received == 0
                                                          )
                                                      ))
                                                 .ToList();

            if (incomesToRemove.Any())
            {
                _context.Incomes.RemoveRange(incomesToRemove);
            }

            return _context.SaveChangesAsync();
        }

        public bool IncomesExists(int id)
        {
            return _context.Incomes.Any(e => e.Id == id && e.UserId == _user.Id);
        }

        public bool ValidarUsuario(int incomeId)
        {
            return GetIncomes(incomeId).Any();
        }

        private static bool IsYieldIncome(Incomes income)
        {
            return string.Equals(income.Type, "Y", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasNextParcel(Incomes income)
        {
            return income.ParcelNumber.HasValue &&
                   income.Parcels.HasValue &&
                   income.Parcels.Value > 1 &&
                   income.ParcelNumber.Value < income.Parcels.Value;
        }

        private static bool IsParceledIncome(Incomes income)
        {
            return income.ParcelNumber.HasValue &&
                   income.Parcels.HasValue &&
                   income.Parcels.Value > 1;
        }

        private static decimal GetParcelAmount(decimal totalToReceive, int parcels, int parcelNumber)
        {
            decimal toReceive  = Math.Round(totalToReceive / parcels, 2, MidpointRounding.AwayFromZero);
            decimal difference = totalToReceive - (toReceive * parcels);

            return parcelNumber == 1 ? toReceive + difference : toReceive;
        }

        private static bool IsSameIncomeFromPreviousMonth(Incomes currentIncome, Incomes previousIncome)
        {
            if (!string.Equals(currentIncome.Description?.Trim(), previousIncome.Description?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsParceledIncome(previousIncome))
            {
                if (!HasNextParcel(previousIncome))
                {
                    return false;
                }

                return currentIncome.Parcels == previousIncome.Parcels &&
                       currentIncome.ParcelNumber == previousIncome.ParcelNumber + 1;
            }

            return !IsParceledIncome(currentIncome);
        }

        private Incomes CreateNextParcelFromPreviousMonth(Incomes previousIncome, string reference, short? position)
        {
            int nextParcelNumber = previousIncome.ParcelNumber!.Value + 1;
            int parcels          = previousIncome.Parcels!.Value;

            decimal totalToReceive = previousIncome.TotalToReceive == 0 ?
                             previousIncome.ToReceive * parcels :
                             previousIncome.TotalToReceive;

            return new Incomes
            {
                UserId         = _user.Id,
                Reference      = reference,
                Position       = position,
                Description    = previousIncome.Description,
                ToReceive      = GetParcelAmount(totalToReceive, parcels, nextParcelNumber),
                Received       = 0,
                ParcelNumber   = nextParcelNumber,
                Parcels        = parcels,
                TotalToReceive = totalToReceive,
                Note           = previousIncome.Note,
                CardId         = null,
                AccountId      = previousIncome.AccountId,
                Type           = previousIncome.Type,
                PeopleId       = previousIncome.PeopleId,
                RelatedId      = previousIncome.RelatedId ?? previousIncome.Id
            };
        }

        private static string GetNewReference(string reference)
        {
            var year  = int.Parse(reference.Substring(0, 4));
            var month = int.Parse(reference.Substring(4, 2));

            var date = new DateTime(year, month, 1).AddMonths(1);

            var newReference = date.ToString("yyyyMM");

            return newReference;
        }

        private short GetNewPosition(string reference)
        {
            var newPosition = _context.Incomes.Where(e => e.Reference == reference && e.UserId == _user.Id).Max(e => e.Position) ?? 0;

            return ++newPosition;
        }

        private List<Incomes> RepeatIncomes(Incomes income, int qtyMonths)
        {
            List<Incomes> incomesList = new();

            string reference = income.Reference;

            for (int i = 1; i <= (qtyMonths + 1); i++)
            {
                Incomes item = new()
                {
                    UserId         = income.UserId,
                    Reference      = reference,
                    Position       = income.Id > 0 && i == 1 ? income.Position : GetNewPosition(reference),
                    Description    = income.Description,
                    ToReceive      = income.ToReceive,
                    Received       = i == 1 ? income.Received : 0,
                    ParcelNumber   = null,
                    Parcels        = null,
                    TotalToReceive = income.TotalToReceive == 0 ? income.ToReceive : income.TotalToReceive,
                    Note           = income.Note,
                    CardId         = income.CardId,
                    AccountId      = income.AccountId,
                    Type           = income.Type,
                    PeopleId       = income.PeopleId
                };

                incomesList.Add(item);

                reference = GetNewReference(reference);
            }

            return incomesList;
        }

        private List<Incomes> GenerateIncomes(Incomes income)
        {
            List<Incomes> incomesList = new();

            string reference    = income.Reference;
            int parcelNumber    = income.ParcelNumber ?? 1;
            int parcels         = income.Parcels ?? 1;
            decimal totalAmount = income.TotalToReceive == 0 ? income.ToReceive : income.TotalToReceive;
            decimal toReceive   = Math.Round(totalAmount / parcels, 2, MidpointRounding.AwayFromZero);
            decimal difference  = totalAmount - (toReceive * parcels);

            for (int i = parcelNumber; i <= parcels; i++)
            {
                decimal currentToReceive = toReceive;

                if (i == 1 && difference != 0)
                {
                    currentToReceive += difference;
                }

                Incomes item = new()
                {
                    UserId         = income.UserId,
                    Reference      = reference,
                    Position       = income.Id > 0 && i == parcelNumber ? income.Position : GetNewPosition(reference),
                    Description    = income.Description,
                    ToReceive      = currentToReceive,
                    Received       = i == parcelNumber ? income.Received : 0,
                    ParcelNumber   = i,
                    Parcels        = parcels,
                    TotalToReceive = totalAmount,
                    Note           = income.Note,
                    CardId         = income.CardId,
                    AccountId      = income.AccountId,
                    Type           = income.Type,
                    PeopleId       = income.PeopleId
                };

                incomesList.Add(item);

                reference = GetNewReference(reference);
            }

            return incomesList;
        }

        private static IncomesDTO IncomesToDTO(Incomes income) =>
        new IncomesDTO
        {
            Id             = income.Id,
            UserId         = income.UserId,
            Reference      = income.Reference,
            Position       = income.Position,
            Description    = income.Description,
            ToReceive      = income.ToReceive,
            Received       = income.Received,
            Remaining      = income.ToReceive - income.Received,
            ParcelNumber   = income.ParcelNumber,
            Parcels        = income.Parcels,
            TotalToReceive = income.TotalToReceive,
            Note           = income.Note,
            CardId         = income.CardId,
            AccountId      = income.AccountId,
            Type           = income.Type,
            PeopleId       = income.PeopleId,
            RelatedId      = income.RelatedId
        };

        private static IncomesDTO2 IncomesToComboList(Incomes income) =>
        new()
        {
            Id          = income.Id,
            Position    = income.Position,
            Description = income.Description
        };

        public async Task OrderByPreviousMonth(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference) || reference.Length != 6)
            {
                throw new ArgumentException("Referência inválida. O formato esperado é 'yyyyMM'.");
            }

            string previousReference = DateTime.ParseExact(reference, "yyyyMM", null).AddMonths(-1).ToString("yyyyMM");

            List<Incomes> previousIncomes = await _context.Incomes.Where(e => e.UserId == _user.Id && e.Reference == previousReference)
                                                          .OrderBy(e => e.Position)
                                                          .ToListAsync();

            if (!previousIncomes.Any())
            {
                throw new InvalidOperationException("Nenhuma receita encontrada para o mês anterior.");
            }

            List<Incomes> currentIncomes = await _context.Incomes.Where(e => e.UserId == _user.Id && e.Reference == reference)
                                                         .ToListAsync();

            foreach (Incomes previousIncome in previousIncomes.Where(i => i.CardId == null && i.Description != "Tarifa" && HasNextParcel(i)))
            {
                int nextParcelNumber = previousIncome.ParcelNumber!.Value + 1;

                bool alreadyExists = currentIncomes.Any(i =>
                                           i.UserId == _user.Id &&
                                           i.Reference == reference &&
                                           i.Description == previousIncome.Description &&
                                           i.ParcelNumber == nextParcelNumber &&
                                           i.Parcels == previousIncome.Parcels);

                if (!alreadyExists)
                {
                    Incomes nextParcel =
                        CreateNextParcelFromPreviousMonth(
                            previousIncome,
                            reference,
                            previousIncome.Position);

                    await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                        _context,
                        _user.Id,
                        nextParcel.CardId,
                        nextParcel.AccountId);

                    _context.Incomes.Add(nextParcel);
                    currentIncomes.Add(nextParcel);
                }
            }

            foreach (Incomes previousIncome in previousIncomes)
            {
                Incomes? income = currentIncomes.FirstOrDefault(e => IsSameIncomeFromPreviousMonth(e, previousIncome));

                if (income != null)
                {
                    income.Position = previousIncome.Position;
                }
            }

            List<Incomes> ordered = currentIncomes.OrderBy(e => e.Position)
                                          .ThenBy(e => e.Description)
                                          .ThenBy(e => e.ParcelNumber)
                                          .ToList();

            short pos = 1;

            foreach (Incomes income in ordered)
            {
                income.Position = pos++;
            }

            await _context.SaveChangesAsync();
        }

        public async Task RepeatPreviousMonth(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference) || reference.Length != 6)
                throw new ArgumentException("Referência inválida. Formato esperado: yyyyMM.");

            string previousReference = DateTime.ParseExact(reference, "yyyyMM", null).AddMonths(-1).ToString("yyyyMM");

            using (var tx = await _context.Database.BeginTransactionAsync())
            {
                // 1) Apaga tudo do mês atual (inclusive cartão)
                List<Incomes> currentIncomes = await _context.Incomes.Where(e => e.UserId == _user.Id && e.Reference == reference).ToListAsync();
                if (currentIncomes.Any())
                    _context.Incomes.RemoveRange(currentIncomes);

                // 2) Busca mês anterior e copia SOMENTE não-cartão (exceto Tarifa)
                List<Incomes> previousIncomes = await _context.Incomes.Where(e => e.UserId == _user.Id && e.Reference == previousReference)
                                                                      .OrderBy(e => e.Position)
                                                                      .ToListAsync();

                if (!previousIncomes.Any())
                    throw new InvalidOperationException("Nenhuma receita encontrada no mês anterior.");

                foreach (
                    Incomes previousIncome
                    in previousIncomes.Where(i =>
                        i.CardId == null &&
                        i.Description != "Tarifa"))
                {
                    bool isYield =
                        string.Equals(
                            previousIncome.Type,
                            "Y",
                            StringComparison.OrdinalIgnoreCase);

                    if (HasNextParcel(previousIncome))
                    {
                        Incomes nextParcel =
                            CreateNextParcelFromPreviousMonth(
                                previousIncome,
                                reference,
                                previousIncome.Position);

                        await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                            _context,
                            _user.Id,
                            nextParcel.CardId,
                            nextParcel.AccountId);

                        _context.Incomes.Add(nextParcel);

                        continue;
                    }

                    if (IsParceledIncome(previousIncome))
                    {
                        continue;
                    }

                    Incomes newIncome = new()
                    {
                        UserId = _user.Id,
                        Reference = reference,
                        Position = previousIncome.Position,
                        Description = previousIncome.Description,
                        ToReceive = isYield
                            ? 0
                            : previousIncome.ToReceive,
                        Received = 0,
                        ParcelNumber = null,
                        Parcels = null,
                        TotalToReceive = isYield
                            ? 0
                            : previousIncome.TotalToReceive,
                        Note = previousIncome.Note,
                        CardId = null,
                        AccountId = previousIncome.AccountId,
                        Type = previousIncome.Type,
                        PeopleId = previousIncome.PeopleId,
                        RelatedId = null
                    };

                    await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                        _context,
                        _user.Id,
                        newIncome.CardId,
                        newIncome.AccountId);

                    _context.Incomes.Add(newIncome);
                }

                await _context.SaveChangesAsync();

                // 3) Dispara trigger para recriar/atualizar receitas de cartão (Rec. Cartão)
                List<int> cardPostingIdsToTouch = await (from cp in _context.CardsPostings
                                                         join c in _context.Cards on cp.CardId equals c.Id
                                                         where c.UserId == _user.Id &&
                                                               cp.Reference == reference &&
                                                               cp.Others == true
                                                         group cp by cp.CardId into g
                                                         select g.Min(x => x.Id))
                                                         .ToListAsync();

                foreach (int id in cardPostingIdsToTouch)
                {
                    await _context.Database.ExecuteSqlRawAsync("UPDATE dbo.CardsPostings SET Reference = Reference WHERE Id = {0}", id);
                }

                // 4) Calcula Tarifa (R$ 3 por pessoa distinta que comprou no cartão no mês)
                int peopleCount = await (from cp in _context.CardsPostings
                                         join c in _context.Cards on cp.CardId equals c.Id
                                         where c.UserId == _user.Id &&
                                               cp.Reference == reference &&
                                               cp.Others == true &&
                                               cp.PeopleId != null
                                         select cp.PeopleId!.Value)
                                         .Distinct()
                                         .CountAsync();

                decimal tarifa = peopleCount * 3m;

                Incomes? tarifaIncome = await _context.Incomes.Where(i =>
                                                                    i.UserId == _user.Id &&
                                                                    i.Reference == reference &&
                                                                    i.CardId == null &&
                                                                    i.Description == "Tarifa")
                                                              .FirstOrDefaultAsync();

                if (tarifaIncome == null)
                {
                    short maxPos = await _context.Incomes.Where(i => i.UserId == _user.Id && i.Reference == reference)
                                                         .Select(i => i.Position)
                                                         .MaxAsync() ?? 0;

                    _context.Incomes.Add(new Incomes
                    {
                        UserId         = _user.Id,
                        Reference      = reference,
                        Position       = (short)(maxPos + 1),
                        Description    = "Tarifa",
                        ToReceive      = tarifa,
                        Received       = 0,
                        ParcelNumber   = null,
                        Parcels        = null,
                        TotalToReceive = tarifa,
                        Note           = null,
                        CardId         = null,
                        AccountId      = null,
                        Type           = "R",
                        PeopleId       = null,
                        RelatedId      = null
                    });
                }
                else
                {
                    tarifaIncome.ToReceive      = tarifa;
                    tarifaIncome.TotalToReceive = tarifa;
                    tarifaIncome.Received       = 0;
                }

                await _context.SaveChangesAsync();

                await OrderByPreviousMonth(reference);

                await tx.CommitAsync();
            }
        }

        private static decimal GetFutureToReceive(Incomes sourceIncome, Incomes targetIncome)
        {
            if (sourceIncome.TotalToReceive != 0 &&
                targetIncome.Parcels.HasValue &&
                targetIncome.Parcels.Value > 1 &&
                targetIncome.ParcelNumber.HasValue &&
                targetIncome.ParcelNumber.Value >= 1 &&
                targetIncome.ParcelNumber.Value <= targetIncome.Parcels.Value)
            {
                return GetParcelAmount(
                    sourceIncome.TotalToReceive,
                    targetIncome.Parcels.Value,
                    targetIncome.ParcelNumber.Value);
            }

            return sourceIncome.ToReceive;
        }
    }
}
