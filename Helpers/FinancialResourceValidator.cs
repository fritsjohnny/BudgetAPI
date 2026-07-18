using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Helpers
{
    public static class FinancialResourceValidator
    {
        public static async Task ValidateCardForCreateAsync(
            BudgetContext context,
            int userId,
            int? cardId)
        {
            if (!cardId.HasValue)
            {
                return;
            }

            Cards? card = await context.Cards
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Id == cardId.Value &&
                    c.UserId == userId);

            if (card == null)
            {
                throw new ArgumentException(
                    "Cartão inválido para o usuário atual.");
            }

            if (card.Disabled == true)
            {
                throw new InvalidOperationException(
                    $"Não é permitido incluir registros no cartão desativado '{card.Name}'.");
            }
        }

        public static async Task ValidateAccountForCreateAsync(
            BudgetContext context,
            int userId,
            int? accountId)
        {
            if (!accountId.HasValue)
            {
                return;
            }

            Accounts? account = await context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.Id == accountId.Value &&
                    a.UserId == userId);

            if (account == null)
            {
                throw new ArgumentException(
                    "Conta inválida para o usuário atual.");
            }

            if (account.Disabled == true)
            {
                throw new InvalidOperationException(
                    $"Não é permitido incluir registros na conta desativada '{account.Name}'.");
            }
        }

        public static async Task ValidateCardForUpdateAsync(
            BudgetContext context,
            int userId,
            int? originalCardId,
            int? newCardId)
        {
            if (originalCardId == newCardId || !newCardId.HasValue)
            {
                return;
            }

            Cards? card = await context.Cards
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Id == newCardId.Value &&
                    c.UserId == userId);

            if (card == null)
            {
                throw new ArgumentException(
                    "Cartão inválido para o usuário atual.");
            }

            if (card.Disabled == true)
            {
                throw new InvalidOperationException(
                    $"Não é permitido alterar o lançamento para o cartão desativado '{card.Name}'.");
            }
        }

        public static async Task ValidateAccountForUpdateAsync(
            BudgetContext context,
            int userId,
            int? originalAccountId,
            int? newAccountId)
        {
            if (originalAccountId == newAccountId || !newAccountId.HasValue)
            {
                return;
            }

            Accounts? account = await context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.Id == newAccountId.Value &&
                    a.UserId == userId);

            if (account == null)
            {
                throw new ArgumentException(
                    "Conta inválida para o usuário atual.");
            }

            if (account.Disabled == true)
            {
                throw new InvalidOperationException(
                    $"Não é permitido alterar o lançamento para a conta desativada '{account.Name}'.");
            }
        }

        public static async Task ValidateResourcesForCreateAsync(
            BudgetContext context,
            int userId,
            int? cardId,
            int? accountId)
        {
            await ValidateCardForCreateAsync(
                context,
                userId,
                cardId);

            await ValidateAccountForCreateAsync(
                context,
                userId,
                accountId);
        }

        public static async Task ValidateResourcesForUpdateAsync(
            BudgetContext context,
            int userId,
            int? originalCardId,
            int? newCardId,
            int? originalAccountId,
            int? newAccountId)
        {
            await ValidateCardForUpdateAsync(
                context,
                userId,
                originalCardId,
                newCardId);

            await ValidateAccountForUpdateAsync(
                context,
                userId,
                originalAccountId,
                newAccountId);
        }

        public static async Task ValidateCardPostingReferencesAsync(
            BudgetContext context,
            int userId,
            int? categoryId,
            int? peopleId,
            int? expenseId)
        {
            if (categoryId.HasValue && !await context.Categories
                .AsNoTracking()
                .AnyAsync(category =>
                    category.Id == categoryId.Value &&
                    category.UserId == userId))
            {
                throw new ArgumentException(
                    "A categoria informada é inválida para o usuário atual.");
            }

            if (peopleId.HasValue && !await context.People
                .AsNoTracking()
                .AnyAsync(person =>
                    person.Id == peopleId.Value &&
                    person.UserId == userId))
            {
                throw new ArgumentException(
                    "A pessoa informada é inválida para o usuário atual.");
            }

            if (expenseId.HasValue && !await context.Expenses
                .AsNoTracking()
                .AnyAsync(expense =>
                    expense.Id == expenseId.Value &&
                    expense.UserId == userId))
            {
                throw new ArgumentException(
                    "A despesa informada é inválida para o usuário atual.");
            }
        }
    }
}
