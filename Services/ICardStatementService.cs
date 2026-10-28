using BudgetAPI.Models;

namespace BudgetAPI.Services
{
    public interface ICardStatementService
    {
        Task<C6StatementPreview> PreviewC6Async(IFormFile file, int cardId);
        Task<List<CardsPhysicalCard>> GetPhysicalCardsAsync(int cardId);
        Task<List<CardsPhysicalCard>> SavePhysicalCardsAsync(int cardId, IReadOnlyCollection<CardsPhysicalCardInput> cards);
        Task<List<CardStatementDescriptionAlias>> GetAliasesAsync(int cardId, int physicalCardId);
        Task<CardStatementDescriptionAlias> SaveAliasAsync(CardStatementDescriptionAliasInput input);
        Task<bool> DeleteAliasAsync(int cardId, int aliasId);
        Task<List<CardStatementReconciliationMatchDTO>> GetReconciliationMatchesAsync(int cardId, string reference);
        Task<CardStatementReconciliationMatchDTO> SaveReconciliationMatchAsync(CardStatementReconciliationMatchInput input);
        Task<List<CardStatementReconciliationMatchDTO>> SaveReconciliationMatchesAsync(List<CardStatementReconciliationMatchInput> inputs);
    }

    public class CardStatementImportException : Exception
    {
        public CardStatementImportException(string message) : base(message)
        {
        }
    }
}
