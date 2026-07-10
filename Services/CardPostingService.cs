using System.Text.RegularExpressions;
using BudgetAPI.Data;
using BudgetAPI.Helpers;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface ICardPostingService
    {
        IQueryable<CardsPostings> GetCardsPostings();
        IQueryable<CardsPostings> GetCardsPostings(int id);
        IQueryable<CardsPostingsDTO> GetCardsPostingsById(int id);
        IQueryable<CardsPostingsDTO> GetCardsPostings(int cardId, string reference);
        Task<CardsPostings?> GetCardsPostingsByDescription(string description);
        IQueryable<CardsPostings> GetCardsPostingsByPeopleId(int peopleId, string reference);
        IQueryable<CardsPostingsPeople> GetCardsPostingsPeople(int cardId, string reference);
        IQueryable<CardsPostingsDTO> GetCardsPostingsByReferences(string initialReference, string finalReference, int categoryId, bool others);
        CardsPostingsPeople GetCardsPostingsByPeopleId(int? peopleId, string reference, int cardId);
        Task PutCardsPostings(CardsPostings cardPosting, bool repeatToNextMonths);
        Task PutCardsPostingsWithParcels(CardsPostings cardsPostings, bool repeat, int qtyMonths);
        Task PostCardsPostings(CardsPostings cardPosting);
        Task PostCardsPostingsWithParcels(CardsPostings cardsPostings, bool repeat, int qtyMonths);
        Task PostCardsPostingsFromNotification(CardsPostings cardPosting);
        Task PostCardsPostingsWithParcelsFromNotification(CardsPostings cardPosting, bool repeat, int qtyMonths);
        Task DeleteCardsPostings(CardsPostings cardPosting);
        Task<int> SetPositions(List<CardsPostings> cardsPostings);
        bool ValidarUsuario(int cardPostingId);
        bool CardsPostingsExists(int id);
        bool ValidateCardAndUser(int cardId);
        int? GetCategory(string description);
    }
    public class CardPostingService : ICardPostingService
    {
        private readonly BudgetContext _context;

        private readonly Users _user;

        private readonly IExpenseService _expenseService;

        public CardPostingService(BudgetContext context, IHttpContextAccessor httpContextAccessor, IExpenseService expenseService)
        {
            _context        = context;
            _user           = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
            _expenseService = expenseService;
        }

        // Normaliza descrições: trim, reduzir múltiplos espaços para um
        private static string NormalizeDescription(string? description)
        {
            string normalizedDescription = Regex.Replace((description ?? string.Empty).Trim(), @"\s+", " ");

            return normalizedDescription;
        }

        private static bool HasSameInstallmentIdentity(CardsPostings candidate, CardsPostings cardPosting)
        {
            int candidateParcels        = candidate.Parcels.GetValueOrDefault(1);
            int cardPostingParcels      = cardPosting.Parcels.GetValueOrDefault(1);
            int candidateParcelNumber   = candidate.ParcelNumber.GetValueOrDefault(1);
            int cardPostingParcelNumber = cardPosting.ParcelNumber.GetValueOrDefault(1);

            return candidateParcels > 1 &&
                   cardPostingParcels > 1 &&
                   candidate.TotalAmount.HasValue &&
                   cardPosting.TotalAmount.HasValue &&
                   candidate.TotalAmount.Value == cardPosting.TotalAmount.Value &&
                   candidateParcels == cardPostingParcels &&
                   candidateParcelNumber == cardPostingParcelNumber;
        }

        private static bool IsProvisionedValueMatch(CardsPostings candidate, CardsPostings cardPosting)
        {
            return candidate.Amount == cardPosting.Amount ||
                   HasSameInstallmentIdentity(candidate, cardPosting);
        }

        private static int GetProvisionedValueMatchPriority(CardsPostings candidate, CardsPostings cardPosting)
        {
            if (HasSameInstallmentIdentity(candidate, cardPosting))
                return 0;

            if (candidate.Amount == cardPosting.Amount)
                return 1;

            return int.MaxValue;
        }

        private async Task<CardsPostings?> FindProvisionedPostingAsync(CardsPostings cardPosting)
        {
            List<CardsPostings> candidates = await _context.CardsPostings
                                                   .Where(cp => cp.Card!.UserId == _user.Id &&
                                                                cp.CardId == cardPosting.CardId &&
                                                                cp.Reference == cardPosting.Reference &&
                                                                cp.Provisioned)
                                                   .ToListAsync();

            string normalizedDescription = NormalizeDescription(cardPosting.Description);

            CardsPostings? provisionedPosting = candidates.Where(candidate => string.Equals(NormalizeDescription(candidate.Description),
                                                                                     normalizedDescription,
                                                                                     StringComparison.OrdinalIgnoreCase) &&
                                                                      IsProvisionedValueMatch(candidate, cardPosting))
                                                   .OrderBy(candidate => GetProvisionedValueMatchPriority(candidate, cardPosting))
                                                   .ThenBy(candidate => Math.Abs((candidate.Date.Date - cardPosting.Date.Date).Days))
                                                   .ThenByDescending(candidate => candidate.Id)
                                                   .FirstOrDefault();

            return provisionedPosting;
        }

        private static void ApplyNotificationToProvisioned(CardsPostings provisioned, CardsPostings cardPosting)
        {
            bool preserveInstallmentStructure = provisioned.Parcels.GetValueOrDefault(1) > 1 &&
                                        cardPosting.Parcels.GetValueOrDefault(1) <= 1 &&
                                        cardPosting.ParcelNumber.GetValueOrDefault(1) <= 1;

            int? peopleId     = cardPosting.PeopleId ?? provisioned.PeopleId;
            int? categoryId   = cardPosting.CategoryId ?? provisioned.CategoryId;
            int? expenseId    = cardPosting.ExpenseId ?? provisioned.ExpenseId;
            bool? fixedValue  = cardPosting.Fixed ?? provisioned.Fixed;
            DateTime? dueDate = cardPosting.DueDate ?? provisioned.DueDate;
            bool? isPaid      = cardPosting.IsPaid ?? provisioned.IsPaid;
            string? note      = !string.IsNullOrWhiteSpace(cardPosting.Note) ? cardPosting.Note : provisioned.Note;

            decimal amount      = preserveInstallmentStructure ? provisioned.Amount : cardPosting.Amount;
            decimal totalAmount = preserveInstallmentStructure
                ? provisioned.TotalAmount ?? cardPosting.TotalAmount ?? cardPosting.Amount
                : cardPosting.TotalAmount ?? cardPosting.Amount;

            int? parcelNumber = preserveInstallmentStructure ? provisioned.ParcelNumber : cardPosting.ParcelNumber;
            int? parcels      = preserveInstallmentStructure ? provisioned.Parcels : cardPosting.Parcels;

            provisioned.CardId       = cardPosting.CardId;
            provisioned.Date         = cardPosting.Date;
            provisioned.Reference    = cardPosting.Reference;
            provisioned.Description  = cardPosting.Description;
            provisioned.Amount       = amount;
            provisioned.TotalAmount  = totalAmount;
            provisioned.ParcelNumber = parcelNumber;
            provisioned.Parcels      = parcels;
            provisioned.PeopleId     = peopleId;
            provisioned.CategoryId   = categoryId;
            provisioned.ExpenseId    = expenseId;
            provisioned.Fixed        = fixedValue;
            provisioned.DueDate      = dueDate;
            provisioned.IsPaid       = isPaid;
            provisioned.Note         = note;
            provisioned.Others       = peopleId.HasValue;
            provisioned.Provisioned  = false;

            cardPosting.Id           = provisioned.Id;
            cardPosting.CardId       = provisioned.CardId;
            cardPosting.Date         = provisioned.Date;
            cardPosting.Reference    = provisioned.Reference;
            cardPosting.Position     = provisioned.Position;
            cardPosting.Description  = provisioned.Description;
            cardPosting.Amount       = provisioned.Amount;
            cardPosting.TotalAmount  = provisioned.TotalAmount;
            cardPosting.ParcelNumber = provisioned.ParcelNumber;
            cardPosting.Parcels      = provisioned.Parcels;
            cardPosting.PeopleId     = provisioned.PeopleId;
            cardPosting.CategoryId   = provisioned.CategoryId;
            cardPosting.ExpenseId    = provisioned.ExpenseId;
            cardPosting.Fixed        = provisioned.Fixed;
            cardPosting.DueDate      = provisioned.DueDate;
            cardPosting.IsPaid       = provisioned.IsPaid;
            cardPosting.Note         = provisioned.Note;
            cardPosting.Others       = provisioned.Others;
            cardPosting.RelatedId    = provisioned.RelatedId;
            cardPosting.Provisioned  = false;
            cardPosting.People       = null;
            cardPosting.Card         = null;
            cardPosting.Category     = null;
        }

        public IQueryable<CardsPostings> GetCardsPostings()
        {
            return _context.CardsPostings.Include(c => c.Card)
                                         .Where(c => c.Card!.UserId == _user.Id)
                                         .OrderBy(c => c.Position);
        }

        public IQueryable<CardsPostings> GetCardsPostings(int id)
        {
            IQueryable<CardsPostings>? cardsPostings = _context.CardsPostings.Include(c => c.Card)
                                                                             .Include(c => c.People)
                                                                             .Where(c => c.Id == id && c.Card!.UserId == _user.Id);

            return cardsPostings;
        }

        public IQueryable<CardsPostingsDTO> GetCardsPostingsById(int id)
        {
            IQueryable<CardsPostingsDTO>? cardsPostings = _context.CardsPostings.Include(c => c.Card)
                                                                                .Include(c => c.People)
                                                                                .Where(c => c.Id == id && c.Card!.UserId == _user.Id)
                                                                                .Select(c => CardPostingToDTO(c));

            return cardsPostings;
        }

        public async Task<CardsPostings?> GetCardsPostingsByDescription(string description)
        {
            string normalizedDescription = (description ?? string.Empty).Trim().ToLower();

            IQueryable<CardsPostings> cardsPostings = _context.CardsPostings
                .Where(cp => cp.Card!.UserId == _user.Id &&
                             cp.Description != null &&
                             cp.Description.ToLower().Trim() == normalizedDescription);

            int? categoryId = await cardsPostings
                .Where(cp => cp.CategoryId != null)
                .OrderByDescending(cp => cp.Id)
                .Select(cp => cp.CategoryId)
                .FirstOrDefaultAsync();

            int? peopleId = await cardsPostings
                .Where(cp => cp.PeopleId != null)
                .OrderByDescending(cp => cp.Id)
                .Select(cp => cp.PeopleId)
                .FirstOrDefaultAsync();

            if (!categoryId.HasValue && !peopleId.HasValue)
                return null;

            return new CardsPostings
            {
                CategoryId = categoryId,
                PeopleId = peopleId
            };
        }

        public IQueryable<CardsPostingsDTO> GetCardsPostings(int cardId, string reference)
        {
            IQueryable<CardsPostingsDTO>? cardsPostings = _context.CardsPostings.Include(c => c.Card)
                                                                                .Include(c => c.People)
                                                                                .Where(c => (cardId == 0 || c.CardId == cardId) && c.Reference == reference && c.Card!.UserId == _user.Id)
                                                                                .OrderBy(c => c.Position)
                                                                                .Select(c => CardPostingToDTO(c));

            return cardsPostings;
        }

        public IQueryable<CardsPostingsDTO> GetCardsPostingsByReferences(string initialReference, string finalReference, int categoryId, bool others)
        {
            IQueryable<CardsPostingsDTO>? cardsPostings = _context.CardsPostings.Include(c => c.Card)
                                                                                .Include(c => c.Category)
                                                                                .Include(c => c.People)
                                                                                .Where(c => string.Compare(c.Reference, initialReference) >= 0 &&
                                                                                            string.Compare(c.Reference, finalReference) <= 0 &&
                                                                                            (categoryId == 0 || c.CategoryId == categoryId) &&
                                                                                            (others == false || c.Others == others) &&
                                                                                            c.Card!.UserId == _user.Id)
                                                                                .OrderBy(c => c.Position)
                                                                                .Select(c => CardPostingToDTO(c));

            return cardsPostings;
        }

        public IQueryable<CardsPostings> GetCardsPostingsByPeopleId(int peopleId, string reference)
        {
            IOrderedQueryable<CardsPostings>? cardsPostings = _context.CardsPostings.Include(c => c.Card)
                                                                                    .Where(c => c.PeopleId == peopleId && c.Reference == reference && c.Card!.UserId == _user.Id)
                                                                                    .OrderBy(c => c.Position); ;

            return cardsPostings;
        }

        public IQueryable<CardsPostingsPeople> GetCardsPostingsPeople(int cardId, string reference)
        {
            IQueryable<CardsPostingsPeople>? cardsPostingsPeople = _context.GetCardsPostingsPeople(cardId, reference, _user.Id);

            return cardsPostingsPeople;
        }

        public CardsPostingsPeople GetCardsPostingsByPeopleId(int? peopleId, string reference, int cardId)
        {
            var cardsPostingPeople = new CardsPostingsPeople
            {
                Reference = reference,
                CardId    = cardId,
                PeopleId  = peopleId
            };

            if (peopleId.HasValue)
            {
                string? person = _context.People.Where(p => p.Id == peopleId.Value && p.UserId == _user.Id)
                                                .Select(p => p.Name)
                                                .FirstOrDefault();

                cardsPostingPeople.Person = person ?? string.Empty;
            }


            cardsPostingPeople.CardsPostings = _context.CardsPostings.Include(c => c.Card)
                                                                     .Where(c => (peopleId == null || c.PeopleId == peopleId) &&
                                                                                     c.Reference == reference &&
                                                                                     c.Card!.UserId == _user.Id &&
                                                                                     (cardId == 0 || c.CardId == cardId))
                                                                     .OrderBy(c => c.Date).ThenBy(c => c.Position);

            cardsPostingPeople.Incomes = _context.Incomes.Where(i => i.PeopleId == peopleId &&
                                                                     i.Reference == reference &&
                                                                     i.UserId == _user.Id);

            cardsPostingPeople.AccountsPostings = _context.AccountsPostings.Include(ap => ap.Account)
                                                                           .Include(ap => ap.Income)
                                                                           .Include(ap => ap.CardReceipt)
                                                                           .ThenInclude(cr => cr!.Card)
                                                                           .Where(ap => ap.Account!.UserId == _user.Id &&
                                                                                        (ap.Income!.PeopleId == peopleId ||
                                                                                         ap.CardReceipt!.PeopleId == peopleId) &&
                                                                                        ap.Reference == reference
                                                                                 );

            return cardsPostingPeople;
        }

        public async Task PutCardsPostings(CardsPostings cardPosting, bool repeatToNextMonths)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Validação de cartão em edição: permite manter o mesmo cartão mesmo desativado;
                // se o CardId foi alterado, exige que o novo cartão exista, pertença ao usuário e esteja ativo.
                CardsPostings? savedCardPosting = await _context.CardsPostings
                    .AsNoTracking()
                    .Where(cp => cp.Id == cardPosting.Id && cp.Card!.UserId == _user.Id)
                    .FirstOrDefaultAsync();

                if (savedCardPosting == null)
                {
                    throw new Exception("Lançamento de cartão não encontrado para o usuário atual.");
                }

                await FinancialResourceValidator.ValidateCardForUpdateAsync(
                    _context,
                    _user.Id,
                    savedCardPosting.CardId,
                    cardPosting.CardId);

                List<int?> expenseIdsToAdjust = new()
                {
                    savedCardPosting.ExpenseId,
                    cardPosting.ExpenseId
                };

                if (repeatToNextMonths)
                {
                    cardPosting.Amount = GetFutureAmount(cardPosting, cardPosting);
                }

                _context.Entry(cardPosting).State = EntityState.Modified;

                if (repeatToNextMonths)
                {
                    string originalDescription = (savedCardPosting.Description ?? string.Empty).Trim();
                    int relatedId = savedCardPosting.RelatedId ?? savedCardPosting.Id;

                    List<CardsPostings> futurePostings = await _context.CardsPostings
                        .Where(cp =>
                            cp.Card!.UserId == _user.Id &&
                            cp.Id != savedCardPosting.Id &&
                            string.Compare(cp.Reference, savedCardPosting.Reference) > 0 &&
                            cp.IsPaid != true &&
                            (
                                cp.RelatedId == relatedId ||
                                (
                                    cp.CardId == savedCardPosting.CardId &&
                                    cp.Description != null &&
                                    cp.Description.Trim() == originalDescription &&
                                    cp.Amount == savedCardPosting.Amount &&
                                    cp.TotalAmount == savedCardPosting.TotalAmount &&
                                    cp.PeopleId == savedCardPosting.PeopleId &&
                                    cp.CategoryId == savedCardPosting.CategoryId &&
                                    cp.Others == savedCardPosting.Others &&
                                    cp.Parcels == savedCardPosting.Parcels &&
                                    cp.Note == savedCardPosting.Note &&
                                    cp.Fixed == savedCardPosting.Fixed
                                )
                            ))
                        .ToListAsync();

                    expenseIdsToAdjust.AddRange(futurePostings.Select(cp => cp.ExpenseId));

                    bool isInstallment = savedCardPosting.Parcels.GetValueOrDefault() > 1;

                    foreach (CardsPostings item in futurePostings)
                    {
                        await FinancialResourceValidator.ValidateCardForUpdateAsync(
                            _context,
                            _user.Id,
                            item.CardId,
                            cardPosting.CardId);

                        item.CardId      = cardPosting.CardId;
                        item.Date        = isInstallment ? cardPosting.Date : ReferenceDateHelper.GetProportionalDate(cardPosting.Date, savedCardPosting.Reference!, item.Reference!);
                        item.DueDate     = ReferenceDateHelper.GetProportionalDate(cardPosting.DueDate, savedCardPosting.Reference!, item.Reference!);
                        item.Description = cardPosting.Description;
                        item.TotalAmount = cardPosting.TotalAmount;
                        item.Amount      = GetFutureAmount(cardPosting, item);
                        item.Fixed       = cardPosting.Fixed;
                        item.CategoryId  = cardPosting.CategoryId;
                        item.PeopleId    = cardPosting.PeopleId;
                        item.Note        = cardPosting.Note;
                        item.Others      = cardPosting.Others;
                        item.Provisioned = cardPosting.Provisioned;

                        _context.Entry(item).State = EntityState.Modified;
                    }
                }

                await _context.SaveChangesAsync();

                await AjustarDespesasVinculadas(expenseIdsToAdjust.ToArray());

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Erro no CardPostingService.PutCardsPostings: {ex.Message}", ex);
            }
        }

        public async Task PutCardsPostingsWithParcels(CardsPostings cardPosting, bool repeat, int qtyMonths)
        {
            using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                CardsPostings? savedCardPosting =
                    await _context.CardsPostings
                        .AsNoTracking()
                        .Where(cp =>
                            cp.Id == cardPosting.Id &&
                            cp.Card!.UserId == _user.Id)
                        .FirstOrDefaultAsync();

                if (savedCardPosting == null)
                {
                    throw new InvalidOperationException(
                        "Lançamento de cartão não encontrado para o usuário atual.");
                }

                int? previousExpenseId =
                    savedCardPosting.ExpenseId;

                int savedRelatedId =
                    savedCardPosting.RelatedId ??
                    savedCardPosting.Id;

                bool hasExistingParcelSequence =
                    savedCardPosting.Parcels.GetValueOrDefault() > 1 ||
                    savedCardPosting.RelatedId.HasValue ||
                    await _context.CardsPostings.AnyAsync(cp =>
                        cp.Card!.UserId == _user.Id &&
                        cp.Id != savedCardPosting.Id &&
                        cp.RelatedId == savedRelatedId);

                if (!repeat && hasExistingParcelSequence)
                {
                    throw new InvalidOperationException(
                        "As parcelas deste lançamento já foram geradas. " +
                        "Não é permitido gerar novamente as demais parcelas.");
                }

                await FinancialResourceValidator.ValidateCardForUpdateAsync(
                    _context,
                    _user.Id,
                    savedCardPosting.CardId,
                    cardPosting.CardId);

                _context.Entry(cardPosting).State =
                    EntityState.Modified;

                List<CardsPostings> cardsPostingsList =
                    repeat
                        ? RepeatCardsPostings(
                            cardPosting,
                            qtyMonths)
                        : GenerateCardsPostings(
                            cardPosting);

                bool createsNewRecords =
                    cardsPostingsList.Skip(1).Any();

                if (createsNewRecords)
                {
                    await FinancialResourceValidator.ValidateCardForCreateAsync(
                        _context,
                        _user.Id,
                        cardPosting.CardId);
                }

                CardsPostings? currentGeneratedPosting =
                    cardsPostingsList.FirstOrDefault();

                if (currentGeneratedPosting != null)
                {
                    cardPosting.Amount =
                        currentGeneratedPosting.Amount;
                }

                int relatedId =
                    cardPosting.RelatedId ??
                    cardPosting.Id;

                foreach (
                    CardsPostings item
                    in cardsPostingsList.Skip(1))
                {
                    item.RelatedId = relatedId;

                    _context.CardsPostings.Add(item);
                }

                await _context.SaveChangesAsync();

                await AjustarDespesasVinculadas(
                    previousExpenseId,
                    cardPosting.ExpenseId);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                throw new Exception(
                    $"Erro no CardPostingService.PutCardsPostingsWithParcels: {ex.Message}",
                    ex);
            }
        }

        public async Task PostCardsPostings(CardsPostings cardPosting)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await PostCardsPostingsCoreAsync(cardPosting);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Erro no CardPostingService.PostCardsPostings: {ex.Message}", ex);
            }
        }

        private async Task PostCardsPostingsCoreAsync(CardsPostings cardPosting)
        {
            await FinancialResourceValidator.ValidateCardForCreateAsync(
                _context,
                _user.Id,
                cardPosting.CardId);

            // Se a pessoa já existe...
            if (_context.People.FirstOrDefault(p => p.Id == cardPosting.PeopleId && p.UserId == _user.Id) != null)
            {
                cardPosting.People = null;
            }

            cardPosting.Position = (short)((_context.CardsPostings.Where(c => c.Reference == cardPosting.Reference && c.CardId == cardPosting.CardId && c.Card!.UserId == _user.Id)
                                                                  .Max(c => c.Position) ?? 0) + 1);

            _context.CardsPostings.Add(cardPosting);

            await _context.SaveChangesAsync();

            if (cardPosting.ExpenseId.HasValue)
                await _expenseService.AjustarValorComBaseNaCategoria(cardPosting.ExpenseId.Value);
        }

        public async Task PostCardsPostingsWithParcels(CardsPostings cardPosting, bool repeat, int qtyMonths)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await PostCardsPostingsWithParcelsCoreAsync(cardPosting, repeat, qtyMonths);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Erro no CardPostingService.PostCardsPostingsWithParcels: {ex.Message}", ex);
            }
        }

        private async Task PostCardsPostingsWithParcelsCoreAsync(CardsPostings cardPosting, bool repeat, int qtyMonths)
        {
            await FinancialResourceValidator.ValidateCardForCreateAsync(
                _context,
                _user.Id,
                cardPosting.CardId);

            List<CardsPostings>? cardsPostingsList = repeat ?
                                                     RepeatCardsPostings(cardPosting, qtyMonths) :
                                                     GenerateCardsPostings(cardPosting);

            CardsPostings? firstCardsPostings = null;

            foreach (CardsPostings cp in cardsPostingsList)
            {
                if (firstCardsPostings == null)
                    cp.ExpenseId = cardPosting.ExpenseId;

                _context.CardsPostings.Add(cp);

                await _context.SaveChangesAsync();

                if (firstCardsPostings == null)
                {
                    firstCardsPostings = cp;

                    cardPosting.Id     = cp.Id;
                    cardPosting.Amount = cp.Amount;
                }
                else
                {
                    cp.RelatedId = firstCardsPostings.Id;
                    await _context.SaveChangesAsync();
                }
            }

            if (cardPosting.ExpenseId.HasValue)
                await _expenseService.AjustarValorComBaseNaCategoria(cardPosting.ExpenseId.Value);
        }

        public async Task PostCardsPostingsFromNotification(CardsPostings cardPosting)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await FinancialResourceValidator.ValidateCardForCreateAsync(
                    _context,
                    _user.Id,
                    cardPosting.CardId);

                cardPosting.Provisioned = false;

                CardsPostings? provisioned = await FindProvisionedPostingAsync(cardPosting);

                if (provisioned == null)
                {
                    await PostCardsPostingsCoreAsync(cardPosting);
                }
                else
                {
                    int? previousExpenseId = provisioned.ExpenseId;

                    ApplyNotificationToProvisioned(provisioned, cardPosting);

                    _context.Entry(provisioned).State = EntityState.Modified;

                    await _context.SaveChangesAsync();

                    await AjustarDespesasVinculadas(previousExpenseId, provisioned.ExpenseId);
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Erro no CardPostingService.PostCardsPostingsFromNotification: {ex.Message}", ex);
            }
        }

        public async Task PostCardsPostingsWithParcelsFromNotification(CardsPostings cardPosting, bool repeat, int qtyMonths)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await FinancialResourceValidator.ValidateCardForCreateAsync(_context, _user.Id, cardPosting.CardId);

                cardPosting.Provisioned = false;

                CardsPostings? provisioned = await FindProvisionedPostingAsync(cardPosting);

                if (provisioned == null)
                {
                    await PostCardsPostingsWithParcelsCoreAsync(cardPosting, repeat, qtyMonths);
                }
                else
                {
                    int? previousExpenseId = provisioned.ExpenseId;
                    int rootId = provisioned.RelatedId ?? provisioned.Id;

                    bool hasSequence = provisioned.RelatedId.HasValue ||
                               await _context.CardsPostings.AnyAsync(cp => cp.Card!.UserId == _user.Id &&
                                                                          cp.Id != provisioned.Id &&
                                                                          cp.RelatedId == rootId);

                    ApplyNotificationToProvisioned(provisioned, cardPosting);

                    List<CardsPostings> generatedPostings = repeat
                        ? RepeatCardsPostings(cardPosting, qtyMonths)
                        : GenerateCardsPostings(cardPosting);

                    CardsPostings? currentGeneratedPosting = generatedPostings.FirstOrDefault();

                    if (currentGeneratedPosting == null)
                        throw new InvalidOperationException("Não foi possível gerar o lançamento atual da notificação.");

                    provisioned.Date         = currentGeneratedPosting.Date;
                    provisioned.Reference    = currentGeneratedPosting.Reference;
                    provisioned.Amount       = currentGeneratedPosting.Amount;
                    provisioned.TotalAmount  = currentGeneratedPosting.TotalAmount ?? cardPosting.TotalAmount ?? provisioned.Amount;
                    provisioned.ParcelNumber = currentGeneratedPosting.ParcelNumber;
                    provisioned.Parcels      = currentGeneratedPosting.Parcels;
                    provisioned.IsPaid       = currentGeneratedPosting.IsPaid ?? provisioned.IsPaid;
                    provisioned.DueDate      = currentGeneratedPosting.DueDate ?? provisioned.DueDate;
                    provisioned.Provisioned  = false;

                    _context.Entry(provisioned).State = EntityState.Modified;

                    if (!hasSequence)
                    {
                        foreach (CardsPostings generatedPosting in generatedPostings.Skip(1))
                        {
                            generatedPosting.RelatedId = rootId;
                            generatedPosting.ExpenseId = null;
                            generatedPosting.Provisioned = false;

                            _context.CardsPostings.Add(generatedPosting);
                        }
                    }

                    await _context.SaveChangesAsync();

                    await AjustarDespesasVinculadas(previousExpenseId, provisioned.ExpenseId);

                    cardPosting.Id           = provisioned.Id;
                    cardPosting.Date         = provisioned.Date;
                    cardPosting.Reference    = provisioned.Reference;
                    cardPosting.Position     = provisioned.Position;
                    cardPosting.Description  = provisioned.Description;
                    cardPosting.Amount       = provisioned.Amount;
                    cardPosting.TotalAmount  = provisioned.TotalAmount;
                    cardPosting.ParcelNumber = provisioned.ParcelNumber;
                    cardPosting.Parcels      = provisioned.Parcels;
                    cardPosting.PeopleId     = provisioned.PeopleId;
                    cardPosting.CategoryId   = provisioned.CategoryId;
                    cardPosting.ExpenseId    = provisioned.ExpenseId;
                    cardPosting.Fixed        = provisioned.Fixed;
                    cardPosting.DueDate      = provisioned.DueDate;
                    cardPosting.IsPaid       = provisioned.IsPaid;
                    cardPosting.Note         = provisioned.Note;
                    cardPosting.Others       = provisioned.Others;
                    cardPosting.RelatedId    = provisioned.RelatedId;
                    cardPosting.Provisioned  = false;
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Erro no CardPostingService.PostCardsPostingsWithParcelsFromNotification: {ex.Message}", ex);
            }
        }

        public async Task DeleteCardsPostings(CardsPostings cardPosting)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                List<CardsPostings> relatedCardsPostings = await _context.CardsPostings.Where(cp => cp.RelatedId == cardPosting.Id && cp.Card!.UserId == _user.Id)
                                                                                       .ToListAsync();

                List<int?> expenseIdsToAdjust = relatedCardsPostings.Select(cp => cp.ExpenseId).ToList();

                expenseIdsToAdjust.Add(cardPosting.ExpenseId);

                _context.CardsPostings.RemoveRange(relatedCardsPostings);
                _context.CardsPostings.Remove(cardPosting);

                await _context.SaveChangesAsync();

                await AjustarDespesasVinculadas(expenseIdsToAdjust.ToArray());

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Erro no CardPostingService.DeleteCardsPostings: {ex.Message}", ex);
            }
        }

        public async Task<int> SetPositions(List<CardsPostings> cardsPostings)
        {
            List<int> ids = cardsPostings.Select(cp => cp.Id)
                                 .Distinct()
                                 .ToList();

            List<CardsPostings> savedPostings = await _context.CardsPostings
                                                      .Where(cp => ids.Contains(cp.Id) && cp.Card!.UserId == _user.Id)
                                                      .ToListAsync();

            if (savedPostings.Count != ids.Count)
            {
                throw new Exception("Erro no CardPostingService.SetPositions: existem lançamentos inválidos para o usuário atual.");
            }

            foreach (CardsPostings savedPosting in savedPostings)
            {
                CardsPostings? requestPosting = cardsPostings.FirstOrDefault(cp => cp.Id == savedPosting.Id);

                if (requestPosting != null)
                {
                    savedPosting.Position = requestPosting.Position;
                }
            }

            return await _context.SaveChangesAsync();
        }

        public bool CardsPostingsExists(int id)
        {
            return GetCardsPostings(id).Any();
        }

        private static string GetNewReference(string reference)
        {
            var year  = int.Parse(reference.Substring(0, 4));
            var month = int.Parse(reference.Substring(4, 2));

            var date = new DateTime(year, month, 1).AddMonths(1);

            var newReference = date.ToString("yyyyMM");

            return newReference;
        }

        private short GetNewPosition(string reference, int cardId)
        {
            var newPosition = _context.CardsPostings.Where(e => e.Reference == reference && e.CardId == cardId && e.Card!.UserId == _user.Id).Max(e => e.Position) ?? 0;

            return ++newPosition;
        }

        private List<CardsPostings> GenerateCardsPostings(CardsPostings cardPosting)
        {
            List<CardsPostings> cardsPostingsList = new();

            string reference    = cardPosting.Reference!;
            decimal totalAmount = cardPosting.TotalAmount ?? cardPosting.Amount;
            int parcelNumber    = cardPosting.ParcelNumber ?? 1;
            int totalParcels    = cardPosting.Parcels ?? 1;

            if (parcelNumber <= 0 || parcelNumber > totalParcels)
            {
                throw new InvalidOperationException(
                    "O número da parcela é inválido para o total de parcelas informado.");
            }

            for (int i = parcelNumber; i <= totalParcels; i++)
            {
                DateTime? dueDate = ReferenceDateHelper.GetProportionalDate(cardPosting.DueDate, cardPosting.Reference!, reference);

                CardsPostings item = new()
                {
                    CardId       = cardPosting.CardId,
                    Date         = cardPosting.Date,
                    Reference    = reference,
                    PeopleId     = cardPosting.PeopleId,
                    Position     = cardPosting.Id > 0 && i == parcelNumber ? cardPosting.Position : GetNewPosition(reference, cardPosting.CardId),
                    Description  = cardPosting.Description,
                    ParcelNumber = i,
                    Parcels      = totalParcels,
                    Amount       = GetParcelAmount(totalAmount, totalParcels, i),
                    TotalAmount  = cardPosting.TotalAmount,
                    Others       = cardPosting.Others,
                    Provisioned  = cardPosting.Provisioned,
                    Note         = cardPosting.Note,
                    CategoryId   = cardPosting.CategoryId,
                    Fixed        = cardPosting.Fixed,
                    IsPaid       = i == parcelNumber ? cardPosting.IsPaid : false,
                    DueDate      = dueDate
                };

                cardsPostingsList.Add(item);

                reference = GetNewReference(reference);
            }

            return cardsPostingsList;
        }

        public bool ValidarUsuario(int cardPostingId)
        {
            return GetCardsPostings(cardPostingId).Any();
        }

        public bool ValidateCardAndUser(int cardId)
        {
            return _context.Cards.Where(c => c.Id == cardId && c.UserId == _user.Id).Any();
        }

        private static CardsPostingsDTO CardPostingToDTO(CardsPostings cardPosting)
        {
            CardsPostingsDTO cardPostingDTO = new()
            {
                Id           = cardPosting.Id,
                CardId       = cardPosting.CardId,
                Date         = cardPosting.Date,
                Reference    = cardPosting.Reference,
                PeopleId     = cardPosting.PeopleId,
                Position     = cardPosting.Position,
                Description  = cardPosting.Description,
                ParcelNumber = cardPosting.ParcelNumber,
                Parcels      = cardPosting.Parcels,
                Amount       = cardPosting.Amount,
                TotalAmount  = cardPosting.TotalAmount,
                Others       = cardPosting.Others,
                Provisioned  = cardPosting.Provisioned,
                Note         = cardPosting.Note,
                CategoryId   = cardPosting.CategoryId,
                Category     = cardPosting.Category?.Name,
                People       = cardPosting.People,
                Card         = cardPosting.Card,
                RelatedId    = cardPosting.RelatedId,
                Fixed        = cardPosting.Fixed,
                DueDate      = cardPosting.DueDate,
                IsPaid       = cardPosting.IsPaid,
                ExpenseId    = cardPosting.ExpenseId
            };

            return cardPostingDTO;
        }

        private List<CardsPostings> RepeatCardsPostings(CardsPostings cardPosting, int qtyMonths)
        {
            List<CardsPostings> cardPostingsList = new();

            string reference = cardPosting.Reference!;

            for (int i = 0; i <= qtyMonths; i++)
            {
                DateTime date     = ReferenceDateHelper.GetProportionalDate(cardPosting.Date, cardPosting.Reference!, reference);
                DateTime? dueDate = ReferenceDateHelper.GetProportionalDate(cardPosting.DueDate, cardPosting.Reference!, reference);

                CardsPostings item = new()
                {
                    CardId       = cardPosting.CardId,
                    Date         = date,
                    DueDate      = dueDate,
                    Reference    = reference,
                    PeopleId     = cardPosting.PeopleId,
                    Position     = cardPosting.Id > 0 && i == 0 ? cardPosting.Position : GetNewPosition(reference, cardPosting.CardId),
                    Description  = cardPosting.Description,
                    ParcelNumber = 1,
                    Parcels      = cardPosting.Parcels,
                    Amount       = cardPosting.Amount,
                    TotalAmount  = cardPosting.TotalAmount,
                    Others       = cardPosting.Others,
                    Provisioned  = cardPosting.Provisioned,
                    Note         = cardPosting.Note,
                    CategoryId   = cardPosting.CategoryId,
                    Fixed        = cardPosting.Fixed,
                    IsPaid       = i == 0 ? cardPosting.IsPaid : false
                };

                cardPostingsList.Add(item);

                reference = GetNewReference(reference);
            }

            return cardPostingsList;
        }

        public int? GetCategory(string description)
        {
            CardsPostings? cardPosting = _context.CardsPostings.Where(cp => cp.Card!.UserId == _user.Id &&
                                                                            cp.CategoryId != null &&
                                                                            cp.Description!.ToLower() == description.ToLower())
                                                               .FirstOrDefault();


            return cardPosting != null ? cardPosting.CategoryId : null;
        }

        private async Task AjustarDespesasVinculadas(params int?[] expenseIds)
        {
            List<int> ids = expenseIds.Where(e => e.HasValue)
                              .Select(e => e.GetValueOrDefault())
                              .Distinct()
                              .ToList();

            foreach (int expenseId in ids)
            {
                await _expenseService.AjustarValorComBaseNaCategoria(expenseId);
            }
        }

        private static decimal GetParcelAmount(decimal totalAmount, int parcels, int parcelNumber)
        {
            if (parcels <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parcels),
                    "A quantidade de parcelas deve ser maior que zero.");
            }

            if (parcelNumber <= 0 || parcelNumber > parcels)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parcelNumber),
                    "O número da parcela deve estar entre 1 e o total de parcelas.");
            }

            decimal amount = Math.Round(totalAmount / parcels, 2, MidpointRounding.AwayFromZero);

            decimal difference = totalAmount - (amount * parcels);

            return parcelNumber == 1 ? amount + difference : amount;
        }

        private static decimal GetFutureAmount(CardsPostings sourcePosting, CardsPostings targetPosting)
        {
            if (sourcePosting.TotalAmount.HasValue &&
                targetPosting.Parcels.HasValue &&
                targetPosting.Parcels.Value > 1 &&
                targetPosting.ParcelNumber.HasValue &&
                targetPosting.ParcelNumber.Value >= 1 &&
                targetPosting.ParcelNumber.Value <= targetPosting.Parcels.Value)
            {
                return GetParcelAmount(
                    sourcePosting.TotalAmount.Value,
                    targetPosting.Parcels.Value,
                    targetPosting.ParcelNumber.Value);
            }

            return sourcePosting.Amount;
        }
    }
}
