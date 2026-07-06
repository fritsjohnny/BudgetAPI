using BudgetAPI.Data;
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
                List<int?> expenseIdsToAdjust = new List<int?>();

                int? previousExpenseId = await _context.CardsPostings.AsNoTracking()
                                                                     .Where(cp => cp.Id == cardPosting.Id && cp.Card!.UserId == _user.Id)
                                                                     .Select(cp => cp.ExpenseId)
                                                                     .FirstOrDefaultAsync();

                expenseIdsToAdjust.Add(previousExpenseId);
                expenseIdsToAdjust.Add(cardPosting.ExpenseId);

                if (!ValidateCardAndUser(cardPosting.CardId))
                {
                    throw new Exception("Erro no CardPostingService: cartão inválido para o usuário atual.");
                }

                _context.Entry(cardPosting).State = EntityState.Modified;

                if (repeatToNextMonths)
                {
                    List<CardsPostings> futurePostings = await _context.CardsPostings.Where(cp =>
                                                                                            cp.RelatedId != null &&
                                                                                            (cp.RelatedId == cardPosting.Id || cp.RelatedId == cardPosting.RelatedId) &&
                                                                                            string.Compare(cp.Reference, cardPosting.Reference) > 0 &&
                                                                                            cp.Card!.UserId == _user.Id)
                                                                                     .ToListAsync();

                    expenseIdsToAdjust.AddRange(futurePostings.Select(cp => cp.ExpenseId));

                    foreach (CardsPostings item in futurePostings)
                    {
                        item.CardId      = cardPosting.CardId;
                        item.Date        = cardPosting.Date;
                        item.Description = cardPosting.Description;
                        item.TotalAmount = cardPosting.TotalAmount;
                        item.Amount      = cardPosting.Amount;
                        item.Fixed       = cardPosting.Fixed;
                        item.CategoryId  = cardPosting.CategoryId;
                        item.PeopleId    = cardPosting.PeopleId;
                        item.Note        = cardPosting.Note;
                        item.IsPaid      = cardPosting.IsPaid;
                        item.ExpenseId   = cardPosting.ExpenseId;
                        item.Others      = cardPosting.Others;

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
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                int? previousExpenseId = await _context.CardsPostings.AsNoTracking()
                                                                     .Where(cp => cp.Id == cardPosting.Id && cp.Card!.UserId == _user.Id)
                                                                     .Select(cp => cp.ExpenseId)
                                                                     .FirstOrDefaultAsync();

                if (!ValidateCardAndUser(cardPosting.CardId))
                {
                    throw new Exception("Erro no CardPostingService.PutCardsPostingsWithParcels: cartão inválido para o usuário atual.");
                }

                _context.Entry(cardPosting).State = EntityState.Modified;

                List<CardsPostings> cardsPostingsList = repeat ?
                                                RepeatCardsPostings(cardPosting, qtyMonths) :
                                                GenerateCardsPostings(cardPosting);

                int relatedId = cardPosting.RelatedId ?? cardPosting.Id;

                foreach (CardsPostings cp in cardsPostingsList.Skip(1))
                {
                    cp.RelatedId = relatedId;
                    _context.CardsPostings.Add(cp);
                }

                await _context.SaveChangesAsync();

                await AjustarDespesasVinculadas(previousExpenseId, cardPosting.ExpenseId);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Erro no CardPostingService.PutCardsPostingsWithParcels: {ex.Message}", ex);
            }
        }

        public async Task PostCardsPostings(CardsPostings cardPosting)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (!ValidateCardAndUser(cardPosting.CardId))
                {
                    throw new Exception("Erro no CardPostingService.PostCardsPostings: cartão inválido para o usuário atual.");
                }

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

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Erro no CardPostingService.PostCardsPostings: {ex.Message}", ex);
            }
        }

        public async Task PostCardsPostingsWithParcels(CardsPostings cardPosting, bool repeat, int qtyMonths)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (!ValidateCardAndUser(cardPosting.CardId))
                {
                    throw new Exception("Erro no CardPostingService.PostCardsPostingsWithParcels: cartão inválido para o usuário atual.");
                }

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

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Erro no CardPostingService.PostCardsPostingsWithParcels: {ex.Message}", ex);
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
            var cardsPostingsList = new List<CardsPostings>();

            string? reference    = cardPosting.Reference;
            decimal totalAmount  = cardPosting.TotalAmount ?? 0;
            int parcels          = cardPosting.Parcels ?? 1;
            decimal amountParcel = Math.Round(totalAmount / parcels, 2, MidpointRounding.AwayFromZero);

            for (int? i = 1; i <= cardPosting.Parcels; i++)
            {
                // Calculate the difference between total amount and the sum of parcels
                decimal difference = totalAmount - (amountParcel * parcels);

                DateTime? dueDate = cardPosting.DueDate.HasValue ? cardPosting.DueDate.Value.AddMonths((i ?? 1) - 1) : null;

                var cp = new CardsPostings
                {
                    CardId       = cardPosting.CardId,
                    Date         = cardPosting.Date,
                    Reference    = reference,
                    PeopleId     = cardPosting.PeopleId,
                    Position     = cardPosting.Id > 0 && i == 1 ? cardPosting.Position : GetNewPosition(reference, cardPosting.CardId),
                    Description  = cardPosting.Description,
                    ParcelNumber = i,
                    Parcels      = cardPosting.Parcels,
                    Amount       = amountParcel,
                    TotalAmount  = cardPosting.TotalAmount,
                    Others       = cardPosting.Others,
                    Note         = cardPosting.Note,
                    CategoryId   = cardPosting.CategoryId,
                    Fixed        = cardPosting.Fixed,
                    IsPaid       = false,
                    DueDate      = dueDate
                };

                // Add the difference to the first parcel
                if (i == cardPosting.ParcelNumber && difference > 0)
                {
                    cp.Amount += difference;
                }

                cardsPostingsList.Add(cp);

                reference = GetNewReference(reference);

                // Substract the current amount from the total
                totalAmount -= cp.Amount;

                parcels -= parcels > 1 ? 1 : 0;

                // Recalculate the amount of each parcel
                amountParcel = parcels > 1 ? Math.Round(totalAmount / parcels, 2, MidpointRounding.AwayFromZero) : totalAmount;
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
            var cardPostingsList = new List<CardsPostings>();

            string? reference = cardPosting.Reference;

            for (int i = 1; i <= (qtyMonths + 1); i++)
            {
                if (i >= cardPosting.ParcelNumber)
                {
                    DateTime? dueDate = cardPosting.DueDate.HasValue ? cardPosting.DueDate.Value.AddMonths(i - 1) : null;

                    var e = new CardsPostings
                    {
                        CardId       = cardPosting.CardId,
                        Date         = i == 1 ? cardPosting.Date : cardPosting.Date.AddMonths(i - 1),
                        Reference    = reference,
                        PeopleId     = cardPosting.PeopleId,
                        Position     = cardPosting.Id > 0 && i == 1 ? cardPosting.Position : GetNewPosition(reference, cardPosting.CardId),
                        Description  = cardPosting.Description,
                        ParcelNumber = 1,
                        Parcels      = cardPosting.Parcels,
                        Amount       = cardPosting.Amount,
                        TotalAmount  = cardPosting.TotalAmount,
                        Others       = cardPosting.Others,
                        Note         = cardPosting.Note,
                        CategoryId   = cardPosting.CategoryId,
                        Fixed        = cardPosting.Fixed,
                        IsPaid       = false,
                        DueDate      = dueDate
                    };

                    cardPostingsList.Add(e);

                    reference = GetNewReference(reference);
                }
            }

            return cardPostingsList;
        }

        public int? GetCategory(string description)
        {
            CardsPostings? cardPosting = _context.CardsPostings.Where(cp => cp.Card!.UserId == _user.Id &&
                                                                            cp.CategoryId != null &&
                                                                            cp.Description.ToLower() == description.ToLower())
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
    }
}
