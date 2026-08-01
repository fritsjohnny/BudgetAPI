using System.Text.RegularExpressions;
using BudgetAPI.Data;
using BudgetAPI.Helpers;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BudgetAPI.Services
{
    public interface ICardPostingService
    {
        IQueryable<CardsPostings> GetCardsPostings();
        IQueryable<CardsPostings> GetCardsPostings(int id);
        IQueryable<CardsPostingsDTO> GetCardsPostingsById(int id);
        IQueryable<CardsPostingsDTO> GetCardsPostings(int cardId, string reference);
        Task<CardsPostings?> GetCardsPostingsByDescription(string description);
        Task<List<int>> GetRecentCategoryIdsByDescription(string description, int take = 3);
        IQueryable<CardsPostings> GetCardsPostingsByPeopleId(int peopleId, string reference);
        IQueryable<CardsPostingsPeople> GetCardsPostingsPeople(int cardId, string reference);
        IQueryable<CardsPostingsDTO> GetCardsPostingsByReferences(string initialReference, string finalReference, int categoryId, bool others);
        CardsPostingsPeople GetCardsPostingsByPeopleId(int? peopleId, string reference, int cardId);
        Task PutCardsPostings(CardsPostings cardPosting, bool repeatToNextMonths = false, bool preserveFutureValues = false, bool allowClosedInvoiceOperation = false);
        Task PutCardsPostingsWithParcels(CardsPostings cardsPostings, bool repeat, int qtyMonths, bool allowClosedInvoiceOperation = false);
        Task PostCardsPostings(CardsPostings cardPosting, bool allowClosedInvoiceOperation = false);
        Task PostCardsPostingsWithParcels(CardsPostings cardsPostings, bool repeat, int qtyMonths, bool allowClosedInvoiceOperation = false);
        Task PostCardsPostingsFromNotification(CardsPostings cardPosting, bool allowClosedInvoiceOperation = false);
        Task PostCardsPostingsWithParcelsFromNotification(CardsPostings cardPosting, bool repeat, int qtyMonths, bool allowClosedInvoiceOperation = false);
        Task DeleteCardsPostings(CardsPostings cardPosting, bool allowClosedInvoiceOperation = false);
        Task<Expenses> ConvertToExpenseAsync(int cardPostingId, bool allowClosedInvoiceOperation = false);
        Task ReorderPositionsByDate(int cardId, string reference);
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
        private readonly ICardsInvoiceClosingService _invoiceClosingService;

        public CardPostingService(BudgetContext context, IHttpContextAccessor httpContextAccessor, IExpenseService expenseService, ICardsInvoiceClosingService invoiceClosingService)
        {
            _context        = context;
            _user           = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
            _expenseService = expenseService;
            _invoiceClosingService = invoiceClosingService;
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
                                         .OrderBy(c => c.Position)
                                         .ThenBy(c => c.Id);
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

        public async Task<List<int>> GetRecentCategoryIdsByDescription(string description, int take = 5)
        {
            string normalizedDescription = (description ?? string.Empty).Trim().ToLower();
            var ids = await _context.CardsPostings
                .Where(cp => cp.Card!.UserId == _user.Id && cp.CategoryId != null &&
                    cp.Description != null && cp.Description.ToLower().Trim() == normalizedDescription)
                .OrderByDescending(cp => cp.Id)
                .Select(cp => cp.CategoryId!.Value)
                .ToListAsync();

            return ids.Distinct().Take(Math.Max(1, take)).ToList();
        }

        public IQueryable<CardsPostingsDTO> GetCardsPostings(int cardId, string reference)
        {
            IQueryable<CardsPostingsDTO>? cardsPostings = _context.CardsPostings.Include(c => c.Card)
                                                                                .Include(c => c.People)
                                                                                .Where(c => (cardId == 0 || c.CardId == cardId) && c.Reference == reference && c.Card!.UserId == _user.Id)
                                                                                .OrderBy(c => c.Position)
                                                                                .ThenBy(c => c.Id)
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
                                                                                .ThenBy(c => c.Id)
                                                                                .Select(c => CardPostingToDTO(c));

            return cardsPostings;
        }

        public IQueryable<CardsPostings> GetCardsPostingsByPeopleId(int peopleId, string reference)
        {
            IOrderedQueryable<CardsPostings>? cardsPostings = _context.CardsPostings.Include(c => c.Card)
                                                                                    .Where(c => c.PeopleId == peopleId && c.Reference == reference && c.Card!.UserId == _user.Id)
                                                                                    .OrderBy(c => c.Position)
                                                                                    .ThenBy(c => c.Id);

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
                                                                     .OrderBy(c => c.Date).ThenBy(c => c.Position).ThenBy(c => c.Id);

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

        public async Task PutCardsPostings(CardsPostings cardPosting, bool repeatToNextMonths = false, bool preserveFutureValues = false, bool allowClosedInvoiceOperation = false)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                CardsPostings? savedCardPosting = await _context.CardsPostings
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

                await FinancialResourceValidator.ValidateCardPostingReferencesAsync(
                    _context,
                    _user.Id,
                    cardPosting.CategoryId,
                    cardPosting.PeopleId,
                    cardPosting.ExpenseId);

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

                List<(int CardId, string Reference)> affectedGroups = new()
                {
                    (savedCardPosting.CardId, savedCardPosting.Reference!),
                    (cardPosting.CardId, cardPosting.Reference!)
                };

                string originalDescription =
                    NormalizeDescription(savedCardPosting.Description);

                if (repeatToNextMonths &&
                    savedCardPosting.Parcels.GetValueOrDefault() > 1 &&
                    (cardPosting.ParcelNumber != savedCardPosting.ParcelNumber ||
                     cardPosting.Parcels != savedCardPosting.Parcels ||
                     cardPosting.Reference != savedCardPosting.Reference))
                {
                    throw new InvalidOperationException(
                        "Não é permitido alterar a referência, o número da parcela ou o total de parcelas ao repetir a edição.");
                }

                cardPosting.RelatedId = savedCardPosting.RelatedId;

                List<CardsPostings>? futurePostings = null;

                if (repeatToNextMonths)
                {
                    (
                        int? currentRelatedId,
                        List<CardsPostings> resolvedFuturePostings
                    ) = await GetFutureCardPostingsForRepeatAsync(
                        savedCardPosting,
                        originalDescription);

                    cardPosting.RelatedId = currentRelatedId;
                    futurePostings       = resolvedFuturePostings;

                    if (!preserveFutureValues)
                    {
                        cardPosting.Amount =
                            GetFutureAmount(cardPosting, cardPosting);
                    }
                }

                if (futurePostings != null)
                {
                    affectedGroups.AddRange(
                        futurePostings.Select(cp => (cp.CardId, cp.Reference!)));

                    affectedGroups.AddRange(
                        futurePostings.Select(cp => (cardPosting.CardId, cp.Reference!)));

                    expenseIdsToAdjust.AddRange(
                        futurePostings.Select(cp => cp.ExpenseId));

                    await _invoiceClosingService.ValidateOperationAsync(
                        affectedGroups,
                        allowClosedInvoiceOperation);

                    bool isInstallment =
                        savedCardPosting.Parcels.GetValueOrDefault() > 1;

                    foreach (CardsPostings item in futurePostings)
                    {
                        await FinancialResourceValidator.ValidateCardForUpdateAsync(
                            _context,
                            _user.Id,
                            item.CardId,
                            cardPosting.CardId);

                        item.CardId = cardPosting.CardId;

                        item.Date = isInstallment
                            ? cardPosting.Date
                            : ReferenceDateHelper.GetProportionalDate(
                                cardPosting.Date,
                                savedCardPosting.Reference!,
                                item.Reference!);

                        item.DueDate = ReferenceDateHelper.GetProportionalDate(
                            cardPosting.DueDate,
                            savedCardPosting.Reference!,
                            item.Reference!);

                        item.Description = cardPosting.Description;
                        item.Fixed       = cardPosting.Fixed;
                        item.CategoryId  = cardPosting.CategoryId;
                        item.PeopleId    = cardPosting.PeopleId;
                        item.Note        = cardPosting.Note;
                        item.Others      = cardPosting.Others;
                        item.Provisioned = cardPosting.Provisioned;

                        if (!preserveFutureValues)
                        {
                            item.Amount = GetFutureAmount(
                                cardPosting,
                                item);

                            item.TotalAmount =
                                cardPosting.TotalAmount;
                        }

                        _context.Entry(item).State =
                            EntityState.Modified;

                    }
                }

                if (futurePostings == null)
                {
                    await _invoiceClosingService.ValidateOperationAsync(
                        affectedGroups,
                        allowClosedInvoiceOperation);
                }

                await AcquirePositionLocksAsync(affectedGroups);

                _context.Entry(cardPosting).State =
                    EntityState.Modified;

                await _context.SaveChangesAsync();

                await ReorderPositionGroupsByDateAsync(affectedGroups);

                await _context.SaveChangesAsync();

                await AjustarDespesasVinculadas(
                    expenseIdsToAdjust.ToArray());

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                if (ex is ClosedInvoiceOperationException)
                    throw;

                throw new Exception(
                    $"Erro no CardPostingService.PutCardsPostings: {ex.Message}",
                    ex);
            }
        }

        public async Task PutCardsPostingsWithParcels(CardsPostings cardPosting, bool repeat, int qtyMonths, bool allowClosedInvoiceOperation = false)
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

                await FinancialResourceValidator.ValidateCardPostingReferencesAsync(
                    _context,
                    _user.Id,
                    cardPosting.CategoryId,
                    cardPosting.PeopleId,
                    cardPosting.ExpenseId);

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

                List<(int CardId, string Reference)> affectedGroups =
                    GetGeneratedPositionGroups(cardPosting, repeat, qtyMonths);

                affectedGroups.Add(
                    (savedCardPosting.CardId, savedCardPosting.Reference!));

                await _invoiceClosingService.ValidateOperationAsync(
                    affectedGroups,
                    allowClosedInvoiceOperation);

                await AcquirePositionLocksAsync(affectedGroups);

                List<CardsPostings> cardsPostingsList =
                    repeat
                        ? await RepeatCardsPostingsAsync(
                            cardPosting,
                            qtyMonths)
                        : await GenerateCardsPostingsAsync(
                            cardPosting);

                _context.Entry(cardPosting).State =
                    EntityState.Modified;

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

                if (repeat)
                {
                    cardPosting.RelatedId = null;
                }

                int relatedId =
                    cardPosting.RelatedId ??
                    cardPosting.Id;

                foreach (
                    CardsPostings item
                    in cardsPostingsList.Skip(1))
                {
                    if (!repeat)
                    {
                        item.RelatedId = relatedId;
                    }

                    _context.CardsPostings.Add(item);
                }

                await _context.SaveChangesAsync();

                await ReorderPositionGroupsByDateAsync(affectedGroups);

                await _context.SaveChangesAsync();

                await AjustarDespesasVinculadas(
                    previousExpenseId,
                    cardPosting.ExpenseId);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                if (ex is ClosedInvoiceOperationException)
                    throw;

                throw new Exception(
                    $"Erro no CardPostingService.PutCardsPostingsWithParcels: {ex.Message}",
                    ex);
            }
        }

        public async Task PostCardsPostings(CardsPostings cardPosting, bool allowClosedInvoiceOperation = false)
        {
            await FinancialResourceValidator.ValidateCardPostingReferencesAsync(
                _context,
                _user.Id,
                cardPosting.CategoryId,
                cardPosting.PeopleId,
                cardPosting.ExpenseId);

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await FinancialResourceValidator.ValidateCardForCreateAsync(
                    _context,
                    _user.Id,
                    cardPosting.CardId);

                await _invoiceClosingService.ValidatePreviousInvoiceClosedAsync(cardPosting.CardId, cardPosting.Reference!);

                await _invoiceClosingService.ValidateOperationAsync(
                    new[] { (cardPosting.CardId, cardPosting.Reference!) },
                    allowClosedInvoiceOperation);

                await PostCardsPostingsCoreAsync(cardPosting);

                await ReorderPositionGroupsByDateAsync(new[] { (cardPosting.CardId, cardPosting.Reference!) });

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                if (ex is ClosedInvoiceOperationException or OpenPreviousInvoiceOperationException)
                    throw;
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

            cardPosting.Position = await GetNextPositionAsync(cardPosting.Reference!, cardPosting.CardId);

            _context.CardsPostings.Add(cardPosting);

            await _context.SaveChangesAsync();

            if (cardPosting.ExpenseId.HasValue)
                await _expenseService.AjustarValorComBaseNaCategoria(cardPosting.ExpenseId.Value);
        }

        public async Task PostCardsPostingsWithParcels(CardsPostings cardPosting, bool repeat, int qtyMonths, bool allowClosedInvoiceOperation = false)
        {
            await FinancialResourceValidator.ValidateCardPostingReferencesAsync(
                _context,
                _user.Id,
                cardPosting.CategoryId,
                cardPosting.PeopleId,
                cardPosting.ExpenseId);

            List<(int CardId, string Reference)> generatedGroups =
                GetGeneratedPositionGroups(cardPosting, repeat, qtyMonths);

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await FinancialResourceValidator.ValidateCardForCreateAsync(
                    _context,
                    _user.Id,
                    cardPosting.CardId);

                await _invoiceClosingService.ValidatePreviousInvoiceClosedAsync(cardPosting.CardId, cardPosting.Reference!);

                await _invoiceClosingService.ValidateOperationAsync(
                    generatedGroups,
                    allowClosedInvoiceOperation);

                List<CardsPostings> generatedPostings =
                    await PostCardsPostingsWithParcelsCoreAsync(cardPosting, repeat, qtyMonths);

                await ReorderPositionGroupsByDateAsync(
                    generatedPostings.Select(cp => (cp.CardId, cp.Reference!)));

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                if (ex is ClosedInvoiceOperationException or OpenPreviousInvoiceOperationException)
                    throw;
                throw new Exception($"Erro no CardPostingService.PostCardsPostingsWithParcels: {ex.Message}", ex);
            }
        }

        private async Task<List<CardsPostings>> PostCardsPostingsWithParcelsCoreAsync(CardsPostings cardPosting, bool repeat, int qtyMonths)
        {
            await FinancialResourceValidator.ValidateCardForCreateAsync(
                _context,
                _user.Id,
                cardPosting.CardId);

            List<(int CardId, string Reference)> generatedGroups =
                GetGeneratedPositionGroups(cardPosting, repeat, qtyMonths);

            await AcquirePositionLocksAsync(generatedGroups);

            List<CardsPostings>? cardsPostingsList = repeat ?
                                                     await RepeatCardsPostingsAsync(cardPosting, qtyMonths) :
                                                     await GenerateCardsPostingsAsync(cardPosting);

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
                else if (!repeat)
                {
                    cp.RelatedId = firstCardsPostings.Id;
                    await _context.SaveChangesAsync();
                }
            }

            if (cardPosting.ExpenseId.HasValue)
                await _expenseService.AjustarValorComBaseNaCategoria(cardPosting.ExpenseId.Value);

            return cardsPostingsList;
        }

        public async Task PostCardsPostingsFromNotification(CardsPostings cardPosting, bool allowClosedInvoiceOperation = false)
        {
            await FinancialResourceValidator.ValidateCardPostingReferencesAsync(
                _context,
                _user.Id,
                cardPosting.CategoryId,
                cardPosting.PeopleId,
                cardPosting.ExpenseId);

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await FinancialResourceValidator.ValidateCardForCreateAsync(
                    _context,
                    _user.Id,
                    cardPosting.CardId);

                await _invoiceClosingService.ValidatePreviousInvoiceClosedAsync(cardPosting.CardId, cardPosting.Reference!);

                cardPosting.Provisioned = false;

                CardsPostings? provisioned = await FindProvisionedPostingAsync(cardPosting);

                List<(int CardId, string Reference)> affectedGroups = new()
                {
                    (cardPosting.CardId, cardPosting.Reference!)
                };

                if (provisioned == null)
                {
                    await _invoiceClosingService.ValidateOperationAsync(
                        affectedGroups,
                        allowClosedInvoiceOperation);

                    await PostCardsPostingsCoreAsync(cardPosting);
                }
                else
                {
                    affectedGroups.Add((provisioned.CardId, provisioned.Reference!));

                    int? previousExpenseId = provisioned.ExpenseId;

                    ApplyNotificationToProvisioned(provisioned, cardPosting);

                    await FinancialResourceValidator.ValidateCardPostingReferencesAsync(
                        _context,
                        _user.Id,
                        provisioned.CategoryId,
                        provisioned.PeopleId,
                        provisioned.ExpenseId);

                    await _invoiceClosingService.ValidateOperationAsync(
                        affectedGroups,
                        allowClosedInvoiceOperation);

                    _context.Entry(provisioned).State = EntityState.Modified;

                    await _context.SaveChangesAsync();

                    await AjustarDespesasVinculadas(previousExpenseId, provisioned.ExpenseId);
                }

                await ReorderPositionGroupsByDateAsync(affectedGroups);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                if (ex is ClosedInvoiceOperationException or OpenPreviousInvoiceOperationException)
                    throw;
                throw new Exception($"Erro no CardPostingService.PostCardsPostingsFromNotification: {ex.Message}", ex);
            }
        }

        public async Task PostCardsPostingsWithParcelsFromNotification(CardsPostings cardPosting, bool repeat, int qtyMonths, bool allowClosedInvoiceOperation = false)
        {
            await FinancialResourceValidator.ValidateCardPostingReferencesAsync(
                _context,
                _user.Id,
                cardPosting.CategoryId,
                cardPosting.PeopleId,
                cardPosting.ExpenseId);

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await FinancialResourceValidator.ValidateCardForCreateAsync(_context, _user.Id, cardPosting.CardId);

                await _invoiceClosingService.ValidatePreviousInvoiceClosedAsync(cardPosting.CardId, cardPosting.Reference!);

                cardPosting.Provisioned = false;

                CardsPostings? provisioned = await FindProvisionedPostingAsync(cardPosting);

                List<(int CardId, string Reference)> affectedGroups =
                    GetGeneratedPositionGroups(cardPosting, repeat, qtyMonths);
                if (provisioned is not null)
                    affectedGroups.Add((provisioned.CardId, provisioned.Reference!));

                await _invoiceClosingService.ValidateOperationAsync(
                    affectedGroups,
                    allowClosedInvoiceOperation);

                if (provisioned == null)
                {
                    await PostCardsPostingsWithParcelsCoreAsync(cardPosting, repeat, qtyMonths);
                }
                else
                {
                    (int CardId, string Reference) provisionedGroup =
                        (provisioned.CardId, provisioned.Reference!);

                    int? previousExpenseId = provisioned.ExpenseId;
                    int rootId = provisioned.RelatedId ?? provisioned.Id;

                    bool hasSequence = provisioned.RelatedId.HasValue ||
                               await _context.CardsPostings.AnyAsync(cp => cp.Card!.UserId == _user.Id &&
                                                                          cp.Id != provisioned.Id &&
                                                                           cp.RelatedId == rootId);

                    ApplyNotificationToProvisioned(provisioned, cardPosting);

                    await FinancialResourceValidator.ValidateCardPostingReferencesAsync(
                        _context,
                        _user.Id,
                        provisioned.CategoryId,
                        provisioned.PeopleId,
                        provisioned.ExpenseId);

                    affectedGroups = GetGeneratedPositionGroups(cardPosting, repeat, qtyMonths);
                    affectedGroups.Add(provisionedGroup);

                    await AcquirePositionLocksAsync(affectedGroups);

                    List<CardsPostings> generatedPostings = repeat
                        ? await RepeatCardsPostingsAsync(cardPosting, qtyMonths)
                        : await GenerateCardsPostingsAsync(cardPosting);

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

                    if (repeat)
                    {
                        provisioned.RelatedId = null;
                    }

                    _context.Entry(provisioned).State = EntityState.Modified;

                    if (!hasSequence)
                    {
                        foreach (CardsPostings generatedPosting in generatedPostings.Skip(1))
                        {
                            generatedPosting.RelatedId = repeat ? null : rootId;
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

                await ReorderPositionGroupsByDateAsync(affectedGroups);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                if (ex is ClosedInvoiceOperationException or OpenPreviousInvoiceOperationException)
                    throw;
                throw new Exception($"Erro no CardPostingService.PostCardsPostingsWithParcelsFromNotification: {ex.Message}", ex);
            }
        }

        public async Task DeleteCardsPostings(CardsPostings cardPosting, bool allowClosedInvoiceOperation = false)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                List<CardsPostings> postingsToDelete =
                    await GetPostingsToDeleteAsync(cardPosting.Id);

                await _invoiceClosingService.ValidateOperationAsync(
                    GetAffectedGroups(postingsToDelete),
                    allowClosedInvoiceOperation);

                List<int?> expenseIdsToAdjust = GetExpenseIds(postingsToDelete);
                RemovePostings(postingsToDelete);

                await _context.SaveChangesAsync();

                await AjustarDespesasVinculadas(expenseIdsToAdjust.ToArray());

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                if (ex is ClosedInvoiceOperationException)
                    throw;
                throw new Exception($"Erro no CardPostingService.DeleteCardsPostings: {ex.Message}", ex);
            }
        }

        public async Task<Expenses> ConvertToExpenseAsync(
            int cardPostingId,
            bool allowClosedInvoiceOperation = false)
        {
            if (cardPostingId <= 0)
                throw new ArgumentException("O identificador do lançamento deve ser maior que zero.", nameof(cardPostingId));

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                List<CardsPostings> postingsToDelete =
                    await GetPostingsToDeleteAsync(cardPostingId);
                CardsPostings source = postingsToDelete[0];

                await _invoiceClosingService.ValidateOperationAsync(
                    GetAffectedGroups(postingsToDelete),
                    allowClosedInvoiceOperation);

                if (source.CategoryId.HasValue &&
                    !await _context.Categories.AnyAsync(category =>
                        category.Id == source.CategoryId.Value &&
                        category.UserId == _user.Id))
                {
                    throw new ArgumentException("A categoria do lançamento não pertence ao usuário atual.");
                }

                if (source.PeopleId.HasValue &&
                    !await _context.People.AnyAsync(person =>
                        person.Id == source.PeopleId.Value &&
                        person.UserId == _user.Id))
                {
                    throw new ArgumentException("A pessoa do lançamento não pertence ao usuário atual.");
                }

                short nextPosition = (short)((await _context.Expenses
                    .Where(expense => expense.UserId == _user.Id && expense.Reference == source.Reference)
                    .MaxAsync(expense => (short?)expense.Position) ?? 0) + 1);

                var expense = new Expenses
                {
                    UserId = _user.Id,
                    Reference = source.Reference!,
                    Position = nextPosition,
                    Description = source.Description,
                    ToPay = source.Amount,
                    TotalToPay = source.Amount,
                    Paid = 0,
                    DueDate = source.DueDate,
                    CategoryId = source.CategoryId,
                    PeopleId = source.PeopleId,
                    ParcelNumber = source.ParcelNumber,
                    Parcels = source.Parcels,
                    Note = source.Note,
                    Fixed = source.Fixed
                };

                await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                    _context,
                    _user.Id,
                    expense.CardId,
                    expense.AccountId);

                _context.Expenses.Add(expense);
                await _context.SaveChangesAsync();

                List<int?> previousExpenseIds = GetExpenseIds(postingsToDelete);
                RemovePostings(postingsToDelete);
                await _context.SaveChangesAsync();

                await AjustarDespesasVinculadas(previousExpenseIds.ToArray());
                await transaction.CommitAsync();
                return expense;
            }
            catch (ClosedInvoiceOperationException)
            {
                await transaction.RollbackAsync();
                throw;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task<List<CardsPostings>> GetPostingsToDeleteAsync(int cardPostingId)
        {
            CardsPostings? mainPosting = await _context.CardsPostings
                .Include(posting => posting.Card)
                .FirstOrDefaultAsync(posting =>
                    posting.Id == cardPostingId &&
                    posting.Card!.UserId == _user.Id);

            if (mainPosting is null)
                throw new KeyNotFoundException("Lançamento de cartão não encontrado para o usuário atual.");

            int mainParcelNumber = mainPosting.ParcelNumber.GetValueOrDefault(1);
            int totalParcels     = mainPosting.Parcels.GetValueOrDefault(1);

            List<CardsPostings> relatedPostings = new();

            if (totalParcels > 1)
            {
                relatedPostings = await _context.CardsPostings
                    .Include(posting => posting.Card)
                    .Where(posting =>
                        posting.RelatedId == mainPosting.Id &&
                        posting.Parcels == totalParcels &&
                        posting.ParcelNumber.HasValue &&
                        posting.ParcelNumber.Value > mainParcelNumber &&
                        posting.Card!.UserId == _user.Id)
                    .ToListAsync();
            }

            relatedPostings.Insert(0, mainPosting);
            return relatedPostings;
        }

        private static IEnumerable<(int CardId, string Reference)> GetAffectedGroups(
            IEnumerable<CardsPostings> postings) =>
            postings.Select(posting => (posting.CardId, posting.Reference!)).Distinct();

        private static List<int?> GetExpenseIds(IEnumerable<CardsPostings> postings) =>
            postings.Select(posting => posting.ExpenseId).Distinct().ToList();

        private void RemovePostings(IEnumerable<CardsPostings> postings) =>
            _context.CardsPostings.RemoveRange(postings);

        public async Task ReorderPositionsByDate(int cardId, string reference)
        {
            if (cardId <= 0)
            {
                throw new ArgumentException("O cartão informado é inválido.", nameof(cardId));
            }

            if (string.IsNullOrWhiteSpace(reference))
            {
                throw new ArgumentException("A referência informada é inválida.", nameof(reference));
            }

            if (!ValidateCardAndUser(cardId))
            {
                throw new InvalidOperationException(
                    "Cartão não encontrado para o usuário atual.");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await ReorderPositionGroupsByDateAsync(new[] { (cardId, reference) });

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                throw new Exception(
                    $"Erro no CardPostingService.ReorderPositionsByDate: {ex.Message}",
                    ex);
            }
        }

        public async Task<int> SetPositions(List<CardsPostings> cardsPostings)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                List<int> ids = cardsPostings.Select(cp => cp.Id)
                                             .Distinct()
                                             .ToList();

                List<CardsPostings> savedPostings = await _context.CardsPostings
                    .Where(cp => ids.Contains(cp.Id) && cp.Card!.UserId == _user.Id)
                    .ToListAsync();

                if (savedPostings.Count != ids.Count)
                {
                    throw new InvalidOperationException(
                        "Existem lançamentos inválidos para o usuário atual.");
                }

                await AcquirePositionLocksAsync(
                    savedPostings.Select(cp => (cp.CardId, cp.Reference!)));

                Dictionary<int, short?> requestedPositions = cardsPostings
                    .GroupBy(cp => cp.Id)
                    .ToDictionary(group => group.Key, group => group.First().Position);

                foreach (CardsPostings savedPosting in savedPostings)
                {
                    savedPosting.Position = requestedPositions[savedPosting.Id];
                }

                int result = await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return result;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                throw new Exception(
                    $"Erro no CardPostingService.SetPositions: {ex.Message}",
                    ex);
            }
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

        /// <summary>
        /// Adquire o sp_getapplock (Exclusive, LockOwner=Transaction) para o recurso de posições de
        /// CardsPostings de um usuário/cartão/referência específicos. Deve ser chamado dentro de uma
        /// transação já aberta, que permanecerá segurando o lock até o Commit/Rollback.
        /// </summary>
        private async Task AcquirePositionLockAsync(int cardId, string reference)
        {
            if (_context.Database.CurrentTransaction == null)
            {
                throw new InvalidOperationException(
                    "AcquirePositionLockAsync deve ser executado dentro de uma transação ativa.");
            }

            string lockResource = $"CardsPostings.Position:{_user.Id}:{cardId}:{reference}";

            System.Data.Common.DbConnection connection = _context.Database.GetDbConnection();

            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using (System.Data.Common.DbCommand lockCommand = connection.CreateCommand())
            {
                lockCommand.Transaction = _context.Database.CurrentTransaction.GetDbTransaction();
                lockCommand.CommandText = "sp_getapplock";
                lockCommand.CommandType = System.Data.CommandType.StoredProcedure;

                System.Data.Common.DbParameter resourceParam = lockCommand.CreateParameter();
                resourceParam.ParameterName = "@Resource";
                resourceParam.Value = lockResource;
                lockCommand.Parameters.Add(resourceParam);

                System.Data.Common.DbParameter lockModeParam = lockCommand.CreateParameter();
                lockModeParam.ParameterName = "@LockMode";
                lockModeParam.Value = "Exclusive";
                lockCommand.Parameters.Add(lockModeParam);

                System.Data.Common.DbParameter lockOwnerParam = lockCommand.CreateParameter();
                lockOwnerParam.ParameterName = "@LockOwner";
                lockOwnerParam.Value = "Transaction";
                lockCommand.Parameters.Add(lockOwnerParam);

                System.Data.Common.DbParameter lockTimeoutParam = lockCommand.CreateParameter();
                lockTimeoutParam.ParameterName = "@LockTimeout";
                lockTimeoutParam.Value = 10000;
                lockCommand.Parameters.Add(lockTimeoutParam);

                System.Data.Common.DbParameter returnParam = lockCommand.CreateParameter();
                returnParam.ParameterName = "@ReturnValue";
                returnParam.Direction = System.Data.ParameterDirection.ReturnValue;
                returnParam.DbType = System.Data.DbType.Int32;
                lockCommand.Parameters.Add(returnParam);

                await lockCommand.ExecuteNonQueryAsync();

                int lockResult = returnParam.Value is int value ? value : -100;

                if (lockResult < 0)
                {
                    throw new InvalidOperationException(
                        "Não foi possível reservar a posição do lançamento para o cartão " +
                        $"{cardId}, referência {reference}. O bloqueio (sp_getapplock) falhou ou expirou " +
                        $"o tempo limite. Código retornado: {lockResult}.");
                }
            }
        }

        private async Task AcquirePositionLocksAsync(
            IEnumerable<(int CardId, string Reference)> groups)
        {
            if (_context.Database.CurrentTransaction == null)
            {
                throw new InvalidOperationException(
                    "AcquirePositionLocksAsync deve ser executado dentro de uma transação ativa.");
            }

            List<(int CardId, string Reference)> orderedGroups = groups
                .Where(group =>
                    group.CardId > 0 &&
                    !string.IsNullOrWhiteSpace(group.Reference))
                .Distinct()
                .OrderBy(group => group.CardId)
                .ThenBy(group => group.Reference, StringComparer.Ordinal)
                .ToList();

            foreach ((int CardId, string Reference) group in orderedGroups)
            {
                await AcquirePositionLockAsync(
                    group.CardId,
                    group.Reference);
            }
        }

        /// <summary>
        /// Reserva de forma segura a próxima posição para um lançamento de cartão em uma referência.
        /// Utiliza sp_getapplock (via AcquirePositionLockAsync) para evitar que requisições concorrentes
        /// calculem o mesmo MAX(Position). Deve ser chamado dentro de uma transação já aberta.
        /// </summary>
        private async Task<short> GetNextPositionAsync(string reference, int cardId)
        {
            await AcquirePositionLockAsync(cardId, reference);

            short currentMaxPosition = await _context.CardsPostings
                .Where(c => c.Reference == reference && c.CardId == cardId && c.Card!.UserId == _user.Id)
                .Select(c => (short?)c.Position)
                .MaxAsync() ?? 0;

            if (currentMaxPosition == short.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Não é possível gerar uma nova posição para o cartão {cardId}, referência {reference}. " +
                    $"O limite máximo de {short.MaxValue} posições foi atingido.");
            }

            return (short)(currentMaxPosition + 1);
        }

        /// <summary>
        /// Recalcula sequencialmente as posições de todos os lançamentos de um cartão/referência do
        /// usuário atual, ordenando por Date, posição anterior (desempate) e Id. Não abre nem finaliza
        /// transação/SaveChanges: deve ser chamado dentro de uma transação já aberta pelo método público
        /// chamador, que também deve persistir e commitar.
        /// </summary>
        private async Task ReorderPositionsByDateCoreAsync(int cardId, string reference)
        {
            if (_context.Database.CurrentTransaction == null)
            {
                throw new InvalidOperationException(
                    "ReorderPositionsByDateCoreAsync deve ser executado dentro de uma transação ativa.");
            }

            await AcquirePositionLockAsync(cardId, reference);

            List<CardsPostings> postings = await _context.CardsPostings
                .Where(cp => cp.CardId == cardId && cp.Reference == reference && cp.Card!.UserId == _user.Id)
                .OrderBy(cp => cp.Date)
                .ThenBy(cp => cp.Position)
                .ThenBy(cp => cp.Id)
                .ToListAsync();

            int maxCapacity = short.MaxValue + 1;

            if (postings.Count > maxCapacity)
            {
                throw new InvalidOperationException(
                    $"Não é possível reordenar as posições para o cartão {cardId}, referência {reference}. " +
                    $"Quantidade de lançamentos encontrada: {postings.Count}. " +
                    $"Limite permitido: {maxCapacity} lançamentos (posições de 0 a {short.MaxValue}).");
            }

            for (int index = 0; index < postings.Count; index++)
            {
                short newPosition = checked((short)index);

                if (postings[index].Position != newPosition)
                {
                    postings[index].Position = newPosition;
                }
            }
        }

        /// <summary>
        /// Reordena vários grupos de CardId/Reference afetados por uma mesma operação, removendo
        /// duplicados e ignorando referências vazias, sempre processando em ordem determinística
        /// (CardId, depois Reference) para reduzir o risco de deadlock entre locks.
        /// </summary>
        private async Task ReorderPositionGroupsByDateAsync(IEnumerable<(int CardId, string Reference)> groups)
        {
            List<(int CardId, string Reference)> distinctGroups = groups
                .Where(g =>
                    g.CardId > 0 &&
                    !string.IsNullOrWhiteSpace(g.Reference))
                .Distinct()
                .OrderBy(g => g.CardId)
                .ThenBy(g => g.Reference, StringComparer.Ordinal)
                .ToList();

            foreach ((int CardId, string Reference) group in distinctGroups)
            {
                await ReorderPositionsByDateCoreAsync(group.CardId, group.Reference);
            }
        }

        private static List<(int CardId, string Reference)> GetGeneratedPositionGroups(
            CardsPostings cardPosting,
            bool repeat,
            int qtyMonths)
        {
            List<(int CardId, string Reference)> groups = new();
            string reference = cardPosting.Reference!;

            if (repeat)
            {
                for (int i = 0; i <= qtyMonths; i++)
                {
                    groups.Add((cardPosting.CardId, reference));
                    reference = GetNewReference(reference);
                }

                return groups;
            }

            int parcelNumber = cardPosting.ParcelNumber ?? 1;
            int totalParcels = cardPosting.Parcels ?? 1;

            for (int i = parcelNumber; i <= totalParcels; i++)
            {
                groups.Add((cardPosting.CardId, reference));
                reference = GetNewReference(reference);
            }

            return groups;
        }

        private async Task<List<CardsPostings>> GenerateCardsPostingsAsync(CardsPostings cardPosting)
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

                bool keepExistingPosition = cardPosting.Id > 0 && i == parcelNumber;

                CardsPostings item = new()
                {
                    CardId       = cardPosting.CardId,
                    Date         = cardPosting.Date,
                    Reference    = reference,
                    PeopleId     = cardPosting.PeopleId,
                    Position     = keepExistingPosition ? cardPosting.Position : await GetNextPositionAsync(reference, cardPosting.CardId),
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

        private async Task<List<CardsPostings>> RepeatCardsPostingsAsync(CardsPostings cardPosting, int qtyMonths)
        {
            List<CardsPostings> cardPostingsList = new();

            string reference = cardPosting.Reference!;

            for (int i = 0; i <= qtyMonths; i++)
            {
                DateTime date     = ReferenceDateHelper.GetProportionalDate(cardPosting.Date, cardPosting.Reference!, reference);
                DateTime? dueDate = ReferenceDateHelper.GetProportionalDate(cardPosting.DueDate, cardPosting.Reference!, reference);

                bool keepExistingPosition = cardPosting.Id > 0 && i == 0;

                CardsPostings item = new()
                {
                    CardId       = cardPosting.CardId,
                    Date         = date,
                    DueDate      = dueDate,
                    Reference    = reference,
                    PeopleId     = cardPosting.PeopleId,
                    Position     = keepExistingPosition ? cardPosting.Position : await GetNextPositionAsync(reference, cardPosting.CardId),
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

        private async Task<(int? CurrentRelatedId, List<CardsPostings> FuturePostings)> GetFutureCardPostingsForRepeatAsync(CardsPostings savedCardPosting, string originalDescription)
        {
            int currentParcel  = savedCardPosting.ParcelNumber.GetValueOrDefault();
            int totalParcels   = savedCardPosting.Parcels.GetValueOrDefault();
            bool isInstallment = totalParcels > 1;
            int relatedId      = savedCardPosting.RelatedId ?? savedCardPosting.Id;

            bool hasLinkedSequence =
                await _context.CardsPostings.AnyAsync(cp =>
                    cp.Card!.UserId == _user.Id &&
                    cp.Id != savedCardPosting.Id &&
                    (cp.Id == relatedId ||
                     cp.RelatedId == relatedId) &&
                    (!isInstallment ||
                     cp.Parcels == totalParcels));

            if (hasLinkedSequence)
            {
                List<CardsPostings> linkedFuturePostings =
                    await _context.CardsPostings
                        .Where(cp =>
                            cp.Card!.UserId == _user.Id &&
                            cp.Id != savedCardPosting.Id &&
                            cp.IsPaid != true &&
                            cp.RelatedId == relatedId &&
                            string.Compare(
                                cp.Reference,
                                savedCardPosting.Reference) > 0 &&
                            (!isInstallment ||
                             (cp.ParcelNumber.HasValue &&
                              cp.ParcelNumber.Value > currentParcel &&
                              cp.Parcels == totalParcels)))
                        .OrderBy(cp => cp.Reference)
                        .ThenBy(cp => cp.ParcelNumber)
                        .ToListAsync();

                return (savedCardPosting.RelatedId, linkedFuturePostings);
            }

            if (!isInstallment)
            {
                List<CardsPostings> futureCandidates =
                    await _context.CardsPostings
                        .Where(cp =>
                            cp.Card!.UserId == _user.Id &&
                            cp.Id != savedCardPosting.Id &&
                            cp.CardId == savedCardPosting.CardId &&
                            cp.IsPaid != true &&
                            cp.Description != null &&
                            string.Compare(
                                cp.Reference,
                                savedCardPosting.Reference) > 0)
                        .ToListAsync();

                List<CardsPostings> futurePostings =
                    futureCandidates
                        .Where(cp =>
                            string.Equals(
                                NormalizeDescription(cp.Description),
                                originalDescription,
                                StringComparison.OrdinalIgnoreCase))
                        .OrderBy(cp => cp.Reference)
                        .ThenBy(cp => cp.Position)
                        .ToList();

                return (
                    savedCardPosting.RelatedId,
                    futurePostings);
            }

            if (currentParcel <= 0 ||
                currentParcel > totalParcels)
            {
                throw new InvalidOperationException(
                    "O número da parcela atual é inválido para o total de parcelas informado.");
            }

            List<CardsPostings> legacyCandidates =
                await _context.CardsPostings
                    .Where(cp =>
                        cp.Card!.UserId == _user.Id &&
                        cp.Id != savedCardPosting.Id &&
                        cp.CardId == savedCardPosting.CardId &&
                        cp.Description != null &&
                        cp.ParcelNumber.HasValue &&
                        cp.ParcelNumber.Value >= 1 &&
                        cp.ParcelNumber.Value <= totalParcels &&
                        cp.Parcels == totalParcels &&
                        cp.TotalAmount == savedCardPosting.TotalAmount &&
                        cp.Date == savedCardPosting.Date)
                    .ToListAsync();

            List<CardsPostings> matchingLegacyCandidates =
                legacyCandidates
                    .Where(cp =>
                        string.Equals(
                            NormalizeDescription(cp.Description),
                            originalDescription,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            DateTime currentReferenceDate =
                DateTime.ParseExact(
                    savedCardPosting.Reference!,
                    "yyyyMM",
                    null);

            List<CardsPostings> legacySequence =
                matchingLegacyCandidates
                    .Where(cp =>
                    {
                        int monthDifference =
                            cp.ParcelNumber!.Value -
                            currentParcel;

                        string expectedReference =
                            currentReferenceDate
                                .AddMonths(monthDifference)
                                .ToString("yyyyMM");

                        return cp.Reference ==
                               expectedReference;
                    })
                    .ToList();

            List<(
                int Id,
                int ParcelNumber,
                string Reference
            )> sequenceRecords =
                legacySequence
                    .Select(cp => (
                        cp.Id,
                        cp.ParcelNumber!.Value,
                        cp.Reference!))
                    .ToList();

            sequenceRecords.Add((
                savedCardPosting.Id,
                currentParcel,
                savedCardPosting.Reference!));

            IGrouping<
                int,
                (
                    int Id,
                    int ParcelNumber,
                    string Reference
                )
            >? duplicatedParcel =
                sequenceRecords
                    .GroupBy(cp => cp.ParcelNumber)
                    .FirstOrDefault(group =>
                        group.Count() > 1);

            if (duplicatedParcel != null)
            {
                throw new InvalidOperationException(
                    "Não foi possível identificar com segurança a sequência legada. " +
                    $"Existe mais de um lançamento correspondente à parcela {duplicatedParcel.Key}/{totalParcels}.");
            }

            bool hasFutureParcel =
                legacySequence.Any(cp =>
                    cp.ParcelNumber!.Value >
                    currentParcel);

            if (currentParcel < totalParcels &&
                !hasFutureParcel)
            {
                throw new InvalidOperationException(
                    "Não foi possível localizar as parcelas futuras deste lançamento legado. " +
                    "Nenhuma alteração foi aplicada aos próximos meses.");
            }

            (
                int Id,
                int ParcelNumber,
                string Reference
            ) anchor =
                sequenceRecords
                    .OrderBy(cp => cp.ParcelNumber)
                    .ThenBy(cp => cp.Reference)
                    .ThenBy(cp => cp.Id)
                    .First();

            foreach (CardsPostings item in legacySequence)
            {
                item.RelatedId =
                    item.Id == anchor.Id
                        ? null
                        : anchor.Id;
            }

            int? currentRelatedId =
                savedCardPosting.Id == anchor.Id
                    ? null
                    : anchor.Id;

            List<CardsPostings> legacyFuturePostings =
                legacySequence
                    .Where(cp =>
                        cp.IsPaid != true &&
                        cp.ParcelNumber!.Value >
                        currentParcel)
                    .OrderBy(cp => cp.ParcelNumber)
                    .ToList();

            return (currentRelatedId, legacyFuturePostings);
        }
    }
}
