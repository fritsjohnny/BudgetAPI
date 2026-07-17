using BudgetAPI.Data;
using BudgetAPI.Helpers;
using BudgetAPI.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface ICardsInvoiceClosingService
    {
        Task<CardsInvoiceClosingDTO> EnsureAsync(int cardId, string reference);
        Task<CardsInvoiceClosingDTO> UpdateAsync(int id, DateTime closingDate);
        Task<CardsInvoiceClosing?> GetEntityAsync(int cardId, string reference);
        Task ValidateOperationAsync(
            IEnumerable<(int CardId, string Reference)> groups,
            bool allowClosedInvoiceOperation);
    }

    public sealed class ClosedInvoiceOperationException : InvalidOperationException
    {
        public ClosedInvoiceOperationException(string message) : base(message) { }
    }

    public sealed class CardsInvoiceClosingConflictException : Exception
    {
        public CardsInvoiceClosingConflictException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public class CardsInvoiceClosingService : ICardsInvoiceClosingService
    {
        private readonly BudgetContext _context;
        private readonly Users _user;

        public CardsInvoiceClosingService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _user = httpContextAccessor.HttpContext?.Items["User"] as Users ?? new Users();
        }

        public async Task<CardsInvoiceClosingDTO> EnsureAsync(int cardId, string reference)
        {
            ValidateCardId(cardId);
            DateTime referenceMonth = ReferenceHelper.GetReferenceMonth(reference);

            Cards? card = await _context.Cards
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == cardId && item.UserId == _user.Id);

            if (card is null)
                throw new KeyNotFoundException("Cartão não encontrado para o usuário atual.");

            CardsInvoiceClosing? existing = await FindForCurrentUserAsync(cardId, reference);
            if (existing is not null)
                return ToDTO(existing);

            if (!card.ClosingDay.HasValue)
                throw new ArgumentException("Configure o dia de fechamento no cadastro do cartão antes de gerar o fechamento da fatura.");
            if (card.ClosingDay < 1 || card.ClosingDay > 31)
                throw new ArgumentException("O dia de fechamento do cartão deve estar entre 1 e 31.");

            int day = Math.Min(card.ClosingDay.Value, DateTime.DaysInMonth(referenceMonth.Year, referenceMonth.Month));
            DateTime utcNow = DateTime.UtcNow;
            var closing = new CardsInvoiceClosing
            {
                CardId = card.Id,
                Reference = reference,
                ClosingDate = new DateTime(referenceMonth.Year, referenceMonth.Month, day),
                IsEstimated = true,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            };

            _context.CardsInvoiceClosings.Add(closing);
            try
            {
                await _context.SaveChangesAsync();
                closing.Card = card;
                return ToDTO(closing);
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                _context.Entry(closing).State = EntityState.Detached;
                CardsInvoiceClosing? concurrentlyCreated = await FindForCurrentUserAsync(cardId, reference);
                if (concurrentlyCreated is not null)
                    return ToDTO(concurrentlyCreated);

                throw new CardsInvoiceClosingConflictException(
                    "Não foi possível confirmar o fechamento criado simultaneamente. Tente novamente.",
                    exception);
            }
        }

        public async Task<CardsInvoiceClosing?> GetEntityAsync(int cardId, string reference)
        {
            ValidateCardId(cardId);
            ReferenceHelper.GetReferenceMonth(reference);

            bool cardBelongsToUser = await _context.Cards
                .AnyAsync(card => card.Id == cardId && card.UserId == _user.Id);
            if (!cardBelongsToUser)
                throw new KeyNotFoundException("Cartão não encontrado para o usuário atual.");

            return await FindForCurrentUserAsync(cardId, reference);
        }

        public async Task<CardsInvoiceClosingDTO> UpdateAsync(int id, DateTime closingDate)
        {
            if (id <= 0)
                throw new ArgumentException("O identificador do fechamento deve ser maior que zero.", nameof(id));

            CardsInvoiceClosing? closing = await _context.CardsInvoiceClosings
                .Include(item => item.Card)
                .FirstOrDefaultAsync(item => item.Id == id && item.Card!.UserId == _user.Id);
            if (closing is null)
                throw new KeyNotFoundException("Fechamento de fatura não encontrado para o usuário atual.");

            DateTime referenceMonth = ReferenceHelper.GetReferenceMonth(closing.Reference);
            DateTime normalizedDate = closingDate.Date;
            if (normalizedDate.Year != referenceMonth.Year || normalizedDate.Month != referenceMonth.Month)
                throw new ArgumentException($"A data de fechamento deve pertencer à referência {ReferenceHelper.FormatReference(closing.Reference)}.");

            closing.ClosingDate = normalizedDate;
            closing.IsEstimated = false;
            closing.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return ToDTO(closing);
        }

        public async Task ValidateOperationAsync(
            IEnumerable<(int CardId, string Reference)> groups,
            bool allowClosedInvoiceOperation)
        {
            if (groups is null)
                throw new ArgumentNullException(nameof(groups));

            List<(int CardId, string Reference)> distinctGroups = groups
                .Distinct()
                .ToList();

            var closedInvoices = new List<CardsInvoiceClosingDTO>();
            foreach ((int cardId, string reference) in distinctGroups)
            {
                ValidateCardId(cardId);
                ReferenceHelper.GetReferenceMonth(reference);

                CardsInvoiceClosingDTO closing = await EnsureAsync(cardId, reference);
                if (closing.IsClosed)
                    closedInvoices.Add(closing);
            }

            if (allowClosedInvoiceOperation || closedInvoices.Count == 0)
                return;

            CardsInvoiceClosingDTO first = closedInvoices[0];
            string message =
                $"A fatura {ReferenceHelper.FormatReference(first.Reference)} do cartão {first.CardName ?? first.CardId.ToString()} " +
                $"foi fechada em {first.ClosingDate:dd/MM/yyyy}. " +
                "Marque a opção para permitir uma operação em fatura fechada.";

            if (closedInvoices.Count > 1)
                message += $" Há {closedInvoices.Count} faturas fechadas afetadas pela operação.";

            throw new ClosedInvoiceOperationException(message);
        }

        private Task<CardsInvoiceClosing?> FindForCurrentUserAsync(int cardId, string reference) =>
            _context.CardsInvoiceClosings
                .AsNoTracking()
                .Include(item => item.Card)
                .FirstOrDefaultAsync(item => item.CardId == cardId && item.Reference == reference && item.Card!.UserId == _user.Id);

        private static void ValidateCardId(int cardId)
        {
            if (cardId <= 0)
                throw new ArgumentException("O identificador do cartão deve ser maior que zero.", nameof(cardId));
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException exception)
        {
            Exception? current = exception;
            while (current is not null)
            {
                if (current is SqlException sqlException && sqlException.Number is 2601 or 2627)
                    return true;
                current = current.InnerException;
            }
            return false;
        }

        private static CardsInvoiceClosingDTO ToDTO(CardsInvoiceClosing entity) => new()
        {
            Id = entity.Id,
            CardId = entity.CardId,
            CardName = entity.Card?.Name,
            Reference = entity.Reference,
            ClosingDate = entity.ClosingDate,
            IsEstimated = entity.IsEstimated,
            IsClosed = BrazilDateTimeHelper.GetCurrentDate() > entity.ClosingDate.Date
        };
    }
}
