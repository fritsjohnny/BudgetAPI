using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface ICardService
    {
        public IQueryable<CardsDTO> GetCards();
        public IQueryable<Cards> GetCards(int id);
        Task<int> PutCard(int id, Cards card);
        Task<int> PostCard(Cards card);
        Task<int> DeleteCard(Cards card);
        bool CardExists(int id);
        bool ValidarUsuario(int id);
        IQueryable<CardsDTO> GetAvailableCards(string reference, DateTime currentDate);
    }
    public class CardService : ICardService
    {
        private readonly BudgetContext _context;

        private readonly Users _user;

        public CardService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _user    = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
        }

        public IQueryable<CardsDTO> GetCards()
        {
            return _context.Cards.Where(u => u.UserId == _user.Id)
                                 .Select(c => CardToDTO(c));
        }

        public IQueryable<Cards> GetCards(int id)
        {
            IQueryable<Cards> card = _context.Cards.Where(c => c.Id == id && c.UserId == _user.Id);

            return card;
        }

        public Task<int> PutCard(int id, Cards card)
        {
            _context.Entry(card).State = EntityState.Modified;

            return _context.SaveChangesAsync();
        }

        public Task<int> PostCard(Cards card)
        {
            card.UserId = _user.Id;

            _context.Cards.Add(card);

            return _context.SaveChangesAsync();
        }

        public Task<int> DeleteCard(Cards card)
        {
            _context.Cards.Remove(card);

            return _context.SaveChangesAsync();
        }

        public bool CardExists(int id)
        {
            return _context.Cards.Any(e => e.Id == id);
        }

        public bool ValidarUsuario(int id)
        {
            return id == _user.Id;
        }

        private static CardsDTO CardToDTO(Cards card) =>
        new()
        {
            Id             = card.Id,
            UserId         = card.UserId,
            Name           = card.Name,
            Color          = card.Color,
            Background     = card.Background,
            Disabled       = card.Disabled,
            DueDay         = card.DueDay,
            ClosingDay     = card.ClosingDay,
            AppPackageName = card.AppPackageName,
        };

        public IQueryable<CardsDTO> GetAvailableCards(string reference, DateTime currentDate)
        {
            DateTime tomorrow = currentDate.Date.AddDays(1);

            IQueryable<CardsDTO> cards = _context.Cards
                .Where(c =>
                    c.UserId == _user.Id &&
                    (
                        c.Disabled != true ||
                        _context.CardsPostings.Any(cp =>
                            cp.CardId == c.Id &&
                            cp.Reference == reference) ||
                        _context.CardsReceipts.Any(cr =>
                            cr.CardId == c.Id &&
                            cr.Reference == reference)
                    ))
                .OrderByDescending(c => _context.CardsPostings
                    .Where(cp => cp.CardId == c.Id && cp.Date < tomorrow)
                    .Max(cp => (DateTime?)cp.Date))
                .ThenBy(c => c.Name)
                .Select(c => CardToDTO(c));

            return cards;
        }
    }
}
