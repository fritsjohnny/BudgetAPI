using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;
using BudgetAPI.Helpers;

namespace BudgetAPI.Services
{
	public interface ICardReceiptService
	{
		public IQueryable<CardsReceipts> GetCardReceipts();
		public IQueryable<CardsReceipts> GetCardReceipts(int id);
		Task<List<CardReceiptMovementDTO>> GetCardReceiptsByIncomeId(int incomeId);
		Task<int> PutCardReceipt(CardsReceipts card);
		Task<int> PostCardReceipt(CardsReceipts card);
		Task<int> DeleteCardReceipt(CardsReceipts card);
		bool CardReceiptExists(int id);
		bool ValidarUsuario(int cardReceiptId);
		bool ValidateAccountAndUser(int cardId);
	}
	public class CardReceiptService : ICardReceiptService
	{
		private readonly BudgetContext _context;

		private readonly Users _user;

		public CardReceiptService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
		{
			_context = context;
			_user    = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
		}

		public IQueryable<CardsReceipts> GetCardReceipts()
		{
			return _context.CardsReceipts.Include(c => c.Card)
										 .Where(c => c.Card!.UserId == _user.Id);
		}

		public IQueryable<CardsReceipts> GetCardReceipts(int id)
		{
			IQueryable<CardsReceipts> card = _context.CardsReceipts.Include(c => c.Card)
																   .Where(c => c.Id == id && c.Card!.UserId == _user.Id);

			return card;
		}

		public async Task<List<CardReceiptMovementDTO>> GetCardReceiptsByIncomeId(int incomeId)
		{
			var incomeCard = await _context.Incomes
				.AsNoTracking()
				.Where(i => i.Id == incomeId && i.UserId == _user.Id && i.Type == "R" && i.CardId.HasValue)
				.Select(i => new { i.CardId, i.Reference })
				.FirstOrDefaultAsync();

			if (incomeCard == null)
				return new List<CardReceiptMovementDTO>();

			return await _context.CardsReceipts
				.AsNoTracking()
				.Where(receipt => receipt.CardId == incomeCard.CardId &&
				                  receipt.Reference == incomeCard.Reference &&
				                  receipt.Card!.UserId == _user.Id)
				.OrderByDescending(receipt => receipt.Date)
				.ThenByDescending(receipt => receipt.Id)
				.Select(receipt => new CardReceiptMovementDTO
				{
					Id = receipt.Id,
					Date = receipt.Date,
					Reference = receipt.Reference,
					CardId = receipt.CardId,
					CardName = receipt.Card!.Name,
					CardBackground = receipt.Card.Background,
					CardColor = receipt.Card.Color,
					PeopleId = receipt.PeopleId,
					PeopleName = _context.People
						.Where(person => person.Id == receipt.PeopleId && person.UserId == _user.Id)
						.Select(person => person.Name)
						.FirstOrDefault(),
					AccountId = receipt.AccountId,
					AccountName = _context.Accounts
						.Where(account => account.Id == receipt.AccountId && account.UserId == _user.Id)
						.Select(account => account.Name)
						.FirstOrDefault(),
					Amount = receipt.Amount,
					Note = receipt.Note,
				})
				.ToListAsync();
		}

		public async Task<int> PutCardReceipt(CardsReceipts cardReceipt)
		{
			CardsReceipts? saved = await _context.CardsReceipts
				.Include(c => c.Card)
				.Where(c =>
					c.Id == cardReceipt.Id &&
					c.Card!.UserId == _user.Id)
				.FirstOrDefaultAsync();

			if (saved == null)
			{
				throw new InvalidOperationException(
					"Comprovante de cartão não encontrado para o usuário atual.");
			}

			await FinancialResourceValidator.ValidateResourcesForUpdateAsync(
				_context,
				_user.Id,
				saved.CardId,
				cardReceipt.CardId,
				saved.AccountId,
				cardReceipt.AccountId);

			_context.Entry(saved)
				.CurrentValues
				.SetValues(cardReceipt);

			return await _context.SaveChangesAsync();
		}

		public async Task<int> PostCardReceipt(CardsReceipts cardReceipt)
		{
			await FinancialResourceValidator.ValidateResourcesForCreateAsync(
				_context,
				_user.Id,
				cardReceipt.CardId,
				cardReceipt.AccountId);

			_context.CardsReceipts.Add(cardReceipt);

			return await _context.SaveChangesAsync();
		}

		public Task<int> DeleteCardReceipt(CardsReceipts cardReceipt)
		{
			_context.CardsReceipts.Remove(cardReceipt);

			return _context.SaveChangesAsync();
		}

		public bool CardReceiptExists(int id)
		{
			return _context.CardsReceipts.Any(e => e.Id == id);
		}

		public bool ValidarUsuario(int cardReceiptId)
		{
			return GetCardReceipts(cardReceiptId).Any();
		}

		public bool ValidateAccountAndUser(int cardId)
		{
			return _context.Cards.Where(c => c.Id == cardId && c.UserId == _user.Id).Any();
		}
	}
}
