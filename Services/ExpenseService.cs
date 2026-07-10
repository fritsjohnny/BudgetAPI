using BudgetAPI.Data;
using BudgetAPI.Helpers;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface IExpenseService
    {
        IQueryable<Expenses> GetExpenses();
        IQueryable<Expenses> GetExpenses(int id);
        IQueryable<ExpensesDTO> GetExpensesDTO(int id);
        IQueryable<Expenses> GetExpensesByDescription(string description);
        IQueryable<ExpensesDTO> GetExpensesByReference(string reference);
        IQueryable<ExpensesDTO> GetExpensesByReferences(string initialReference, string finalReference, int categoryId, bool others);
        IQueryable<ExpensesDTO> GetMyExpensesByReference(string reference);
        IQueryable<ExpensesDTO2> GetExpensesComboList(string reference);
        IQueryable<ExpensesByCategories> GetExpensesByCategories(string reference, int cardId);
        ExpensesByCategories GetExpensesAndCardPostingsByCategoryId(int? id, string reference, int cardId);
        Task<int> PutExpenses(Expenses expenses, bool repeatToNextMonths = false);
        Task PutExpensesWithParcels(Expenses expenses, bool repeat, int qtyMonths);
        Task<int> SetPositions(List<Expenses> expenses);
        Task<int> AddValue(Expenses expense, decimal value);
        Task<int> PostExpenses(Expenses expense);
        Task PostExpensesWithParcels(Expenses expenses, bool repeat, int qtyMonths);
        Task<int> DeleteExpenses(Expenses expense);
        bool ExpensesExists(int id);
        bool ValidarUsuario(int expenseId);
        Task OrderByPreviousMonth(string reference);
        Task<List<Expenses>> GetUpcomingOrOverdueExpenses(int daysAhead = 1);
        Task<ExpensesDTO?> AjustarValorComBaseNaCategoria(int expenseId);
        Task<int> RepeatFixedExpenses(string reference);
        IQueryable<ExpensesDueDateReportDTO> GetExpensesByDueDateRange(DateTime initialDate, DateTime finalDate);
    }

    public class ExpenseService : IExpenseService
    {
        private readonly BudgetContext _context;

        private readonly Users _user;

        public ExpenseService(
            BudgetContext context,
            IHttpContextAccessor httpContextAccessor,
            FirebaseNotificationService firebase,
            ILogger<FirebaseNotificationService> logger)
        {
            _context = context;
            _user    = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
        }

        public IQueryable<Expenses> GetExpenses()
        {
            return _context.Expenses.OrderBy(e => e.Position);
        }

        public IQueryable<Expenses> GetExpenses(int id)
        {
            IQueryable<Expenses>? expenses = _context.Expenses.Where(e => e.Id == id && e.UserId == _user.Id);

            return expenses;
        }

        public IQueryable<ExpensesDTO> GetExpensesDTO(int id)
        {
            IQueryable<ExpensesDTO>? expenses = _context.Expenses.Where(e => e.Id == id && e.UserId == _user.Id)
                                                                 .Select(e => ExpensesToDTO(e));

            return expenses;
        }

        public IQueryable<Expenses> GetExpensesByDescription(string description)
        {
            IQueryable<Expenses>? expenses = _context.Expenses.Where(cp => cp.UserId == _user.Id &&
                                                                            cp.CategoryId != null &&
                                                                            cp.Description!.ToLower().Trim() == description.ToLower().Trim())
                                                              .OrderByDescending(o => o.Id);

            return expenses;
        }

        public IQueryable<ExpensesDTO> GetExpensesByReference(string reference)
        {
            IQueryable<ExpensesDTO>? expenses = _context.Expenses.Where(e => e.Reference == reference && e.UserId == _user.Id)
                                                                 .OrderBy(e => e.Position)
                                                                 .Select(e => ExpensesToDTO(e));

            return expenses;
        }

        public IQueryable<ExpensesDTO> GetExpensesByReferences(string initialReference, string finalReference, int categoryId, bool others)
        {
            IQueryable<ExpensesDTO>? expenses = _context.Expenses.Include(c => c.Category)
                                                                 .Where(e => string.Compare(e.Reference, initialReference) >= 0 &&
                                                                             string.Compare(e.Reference, finalReference) <= 0 &&
                                                                             (categoryId == 0 || e.CategoryId == categoryId) &&
                                                                             (others == false || e.PeopleId != null) &&
                                                                             e.CardId == null &&
                                                                             e.UserId == _user.Id)
                                                                 .OrderBy(e => e.Position)
                                                                 .Select(e => ExpensesToDTO(e));

            return expenses;
        }

        public IQueryable<ExpensesDueDateReportDTO> GetExpensesByDueDateRange(DateTime initialDate, DateTime finalDate)
        {
            DateTime start = initialDate.Date;
            DateTime end   = finalDate.Date.AddDays(1).AddTicks(-1); // inclui o dia final (23:59:59.9999999)

            IQueryable<ExpensesDueDateReportDTO> expenses = _context.Expenses
                                                                     .Include(e => e.Category)
                                                                     .Where(e => e.UserId == _user.Id &&
                                                                                 //e.CardId == null &&
                                                                                 e.DueDate != null &&
                                                                                 e.DueDate >= start &&
                                                                                 e.DueDate <= end &&
                                                                                 (e.ToPay - e.Paid) > 0)
                                                                     .OrderBy(e => e.DueDate)
                                                                     .ThenBy(e => e.Position)
                                                                     .Select(e => new ExpensesDueDateReportDTO
                                                                     {
                                                                         Id           = e.Id,
                                                                         DueDate      = e.DueDate,
                                                                         Reference    = e.Reference,
                                                                         Description  = e.Description,
                                                                         ToPay        = e.ToPay,
                                                                         Paid         = e.Paid,
                                                                         Remaining    = (e.ToPay - e.Paid),
                                                                         CategoryId   = e.CategoryId,
                                                                         CategoryName = e.Category != null ? e.Category.Name : null,
                                                                         PeopleId     = e.PeopleId,
                                                                         CardId       = e.CardId
                                                                     });

            return expenses;
        }

        public IQueryable<ExpensesDTO> GetMyExpensesByReference(string reference)
        {
            IQueryable<ExpensesDTO>? myExpenses = _context.GetMyExpenses(reference, _user.Id)
                                                          .OrderBy(e => e.Position);

            return myExpenses;
        }

        public IQueryable<ExpensesDTO2> GetExpensesComboList(string reference)
        {
            IQueryable<ExpensesDTO2>? expenses = _context.Expenses.Where(e => e.Reference == reference && e.UserId == _user.Id)
                                                                  .OrderBy(e => e.Position)
                                                                  .Select(e => ExpensesToComboList(e));

            return expenses;
        }

        public IQueryable<ExpensesByCategories> GetExpensesByCategories(string reference, int cardId)
        {
            IQueryable<ExpensesByCategories>? expensesByCategories = _context.GetExpensesByCategories(reference, cardId, _user.Id);

            return expensesByCategories;
        }

        public ExpensesByCategories GetExpensesAndCardPostingsByCategoryId(int? id, string reference, int cardId)
        {
            ExpensesByCategories expensesByCategory = new()
            {
                Id        = id,
                Reference = reference,
                CardId    = cardId
            };

            id = id == 0 ? null : id;

            expensesByCategory.Expenses = _context.Expenses.Where(e => e.CategoryId == id &&
                                                                       e.Reference == reference &&
                                                                       e.UserId == _user.Id &&
                                                                       e.CardId == null).OrderBy(o => o.Position);

            expensesByCategory.CardsPostings = _context.CardsPostings.Include(o => o.Card)
                                                                     .Where(cp => cp.CategoryId == id &&
                                                                                  cp.Reference == reference &&
                                                                                  cp.Card!.UserId == _user.Id &&
                                                                                  (cardId == 0 || cp.CardId == cardId) &&
                                                                                  !cp.Others)
                                                                     .OrderBy(o => o.Date).ThenBy(o => o.Position);


            return expensesByCategory;
        }

        public async Task<int> PutExpenses(Expenses expense, bool repeatToNextMonths = false)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                Expenses? savedExpense = await _context.Expenses.AsNoTracking()
                                                        .Where(e => e.Id == expense.Id && e.UserId == _user.Id)
                                                        .FirstOrDefaultAsync();

                if (savedExpense == null)
                {
                    throw new Exception("Despesa não encontrada para o usuário atual.");
                }

                await FinancialResourceValidator.ValidateResourcesForUpdateAsync(
                    _context,
                    _user.Id,
                    savedExpense.CardId,
                    expense.CardId,
                    savedExpense.AccountId,
                    expense.AccountId);

                string originalDescription = (savedExpense.Description ?? string.Empty).Trim();
                string originalReference   = savedExpense.Reference;

                expense.UserId = _user.Id;

                if (repeatToNextMonths)
                {
                    expense.ToPay = GetFutureToPay(expense, expense);
                }

                _context.Entry(expense).State = EntityState.Modified;

                if (repeatToNextMonths)
                {
                    List<Expenses> futureExpenses = await _context.Expenses.Where(e =>
                                                                      e.UserId == _user.Id &&
                                                                      e.Id != expense.Id &&
                                                                      e.Paid == 0 &&
                                                                      e.Description != null &&
                                                                      e.Description.Trim() == originalDescription &&
                                                                      string.Compare(e.Reference, originalReference) > 0)
                                                                 .ToListAsync();

                    foreach (Expenses item in futureExpenses)
                    {
                        await FinancialResourceValidator.ValidateResourcesForUpdateAsync(
                            _context,
                            _user.Id,
                            item.CardId,
                            expense.CardId,
                            item.AccountId,
                            expense.AccountId);

                        item.Description = expense.Description;
                        item.ToPay = GetFutureToPay(expense, item);
                        item.TotalToPay = expense.TotalToPay;
                        item.Note = expense.Note;
                        item.CardId = expense.CardId;
                        item.AccountId = expense.AccountId;
                        item.DueDate = GetFutureDueDate(
                            expense,
                            savedExpense.Reference,
                            item.Reference);

                        item.CategoryId = expense.CategoryId;
                        item.Scheduled = expense.Scheduled;
                        item.PeopleId = expense.PeopleId;
                        item.DueDay = expense.DueDay;
                        item.ExpectedValue = expense.ExpectedValue;
                        item.Fixed = expense.Fixed;
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

        public async Task PutExpensesWithParcels(Expenses expenses, bool repeat, int qtyMonths)
        {
            Expenses? savedExpense = _context.Expenses
                .AsNoTracking()
                .FirstOrDefault(e =>
                    e.Id == expenses.Id &&
                    e.UserId == _user.Id);

            if (savedExpense == null)
            {
                throw new InvalidOperationException(
                    "Despesa não encontrada para o usuário atual.");
            }

            int relatedId =
                savedExpense.RelatedId ??
                savedExpense.Id;

            bool hasExistingParcelSequence =
                savedExpense.Parcels.GetValueOrDefault() > 1 ||
                savedExpense.RelatedId.HasValue ||
                _context.Expenses.Any(e =>
                    e.UserId == _user.Id &&
                    e.Id != savedExpense.Id &&
                    e.RelatedId == relatedId);

            if (!repeat && hasExistingParcelSequence)
            {
                throw new InvalidOperationException(
                    "As parcelas desta despesa já foram geradas. " +
                    "Não é permitido gerar novamente as demais parcelas.");
            }

            await FinancialResourceValidator.ValidateResourcesForUpdateAsync(
                _context,
                _user.Id,
                savedExpense.CardId,
                expenses.CardId,
                savedExpense.AccountId,
                expenses.AccountId);

            _context.Entry(expenses).State =
                EntityState.Modified;

            List<Expenses> expensesList =
                repeat
                    ? RepeatExpenses(expenses, qtyMonths)
                    : GenerateExpenses(expenses);

            bool createsNewRecords =
                expensesList.Skip(1).Any();

            if (createsNewRecords)
            {
                await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                    _context,
                    _user.Id,
                    expenses.CardId,
                    expenses.AccountId);
            }

            Expenses? currentGeneratedExpense =
                expensesList.FirstOrDefault();

            if (currentGeneratedExpense != null)
            {
                expenses.ToPay =
                    currentGeneratedExpense.ToPay;
            }

            foreach (Expenses item in expensesList.Skip(1))
            {
                _context.Expenses.Add(item);
            }

            await _context.SaveChangesAsync();
        }

        public Task<int> SetPositions(List<Expenses> expenses)
        {
            // Atualizar apenas o campo Position para registros que pertencem ao usuário
            List<int> ids = expenses.Select(e => e.Id).Distinct().ToList();

            List<Expenses> savedExpenses = _context.Expenses
                                        .Where(e => ids.Contains(e.Id) && e.UserId == _user.Id)
                                        .ToList();

            if (savedExpenses.Count != ids.Count)
            {
                throw new Exception("Erro no ExpenseService.SetPositions: existem despesas inválidas para o usuário atual.");
            }

            foreach (Expenses saved in savedExpenses)
            {
                Expenses? request = expenses.FirstOrDefault(e => e.Id == saved.Id);

                if (request != null)
                {
                    saved.Position = request.Position;
                }
            }

            return _context.SaveChangesAsync();
        }

        public Task<int> AddValue(Expenses expense, decimal value)
        {
            expense.ToPay      += value;
            expense.TotalToPay += value;

            _context.Entry(expense).State = EntityState.Modified;

            return _context.SaveChangesAsync();
        }

        public async Task<int> PostExpenses(Expenses expense)
        {
            await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                _context,
                _user.Id,
                expense.CardId,
                expense.AccountId);

            expense.UserId = _user.Id;

            _context.Expenses.Add(expense);

            return await _context.SaveChangesAsync();
        }

        public async Task PostExpensesWithParcels(Expenses expenses, bool repeat, int qtyMonths)
        {
            await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                _context,
                _user.Id,
                expenses.CardId,
                expenses.AccountId);

            List<Expenses>? expensesList = repeat ?
                                           RepeatExpenses(expenses, qtyMonths) :
                                           GenerateExpenses(expenses);

            Expenses? firstExpenses = null;

            foreach (Expenses cp in expensesList)
            {
                cp.UserId = _user.Id;

                // Set RelatedId for all except the first one
                if (firstExpenses != null)
                {
                    cp.RelatedId = firstExpenses.Id;
                }

                _context.Expenses.Add(cp);
                await _context.SaveChangesAsync();

                if (firstExpenses == null)
                {
                    firstExpenses = cp;

                    // Update the input object with the details of the first Expenses
                    expenses.Id = firstExpenses.Id;
                    expenses.ToPay = firstExpenses.ToPay;
                }
            }
        }

        public async Task<int> DeleteExpenses(Expenses expense)
        {
            // Find all the Expenses with the RelatedId equal to the Id of the expense to be deleted
            var relatedExpenses = _context.Expenses.Where(e => e.RelatedId == expense.Id);

            // Remove all found Expenses
            if (relatedExpenses.Any())
            {
                _context.Expenses.RemoveRange(relatedExpenses);
            }

            // Remove the original expense
            _context.Expenses.Remove(expense);

            // Save changes and return the number of affected entries
            return await _context.SaveChangesAsync();
        }

        public bool ExpensesExists(int id)
        {
            return _context.Expenses.Any(e => e.Id == id && e.UserId == _user.Id);
        }

        public bool ValidarUsuario(int expenseId)
        {
            return GetExpenses(expenseId).Any();
        }

        private static string GetNewReference(string reference)
        {
            var year  = int.Parse(reference.Substring(0, 4));
            var month = int.Parse(reference.Substring(4, 2));

            var date = new DateTime(year, month, 1).AddMonths(1);

            var newReference = date.ToString("yyyyMM");

            return newReference;
        }

        private static string GetPreviousReference(string reference)
        {
            var year  = int.Parse(reference.Substring(0, 4));
            var month = int.Parse(reference.Substring(4, 2));

            var date = new DateTime(year, month, 1).AddMonths(-1);

            var previousReference = date.ToString("yyyyMM");

            return previousReference;
        }

        private short GetNewPosition(string reference)
        {
            var newPosition = _context.Expenses.Where(e => e.Reference == reference).Max(e => e.Position) ?? 0;

            return ++newPosition;
        }

        private List<Expenses> GenerateExpenses(Expenses expense)
        {
            List<Expenses> expensesList = new();

            string reference = expense.Reference;
            int parcelNumber = expense.ParcelNumber ?? 1;
            int totalParcels = expense.Parcels ?? 1;

            if (parcelNumber <= 0 || parcelNumber > totalParcels)
            {
                throw new InvalidOperationException(
                    "O número da parcela é inválido para o total de parcelas informado.");
            }

            for (int i = parcelNumber; i <= totalParcels; i++)
            {
                DateTime? dueDate = ReferenceDateHelper.GetProportionalDate(expense.DueDate, expense.Reference, reference, expense.DueDay);

                Expenses item = new()
                {
                    UserId        = expense.UserId,
                    Reference     = reference,
                    Position      = expense.Id > 0 && i == parcelNumber ? expense.Position : GetNewPosition(reference),
                    Description   = expense.Description,
                    ToPay         = GetParcelAmount(expense.TotalToPay, totalParcels, i),
                    Paid          = i == parcelNumber ? expense.Paid : 0,
                    Note          = expense.Note,
                    CardId        = expense.CardId,
                    AccountId     = expense.AccountId,
                    DueDate       = dueDate,
                    ParcelNumber  = i,
                    Parcels       = totalParcels,
                    TotalToPay    = expense.TotalToPay,
                    CategoryId    = expense.CategoryId,
                    Scheduled     = expense.Scheduled,
                    PeopleId      = expense.PeopleId,
                    DueDay        = expense.DueDay,
                    ExpectedValue = expense.ExpectedValue,
                    Fixed         = expense.Fixed
                };

                expensesList.Add(item);

                reference = GetNewReference(reference);
            }

            return expensesList;
        }

        private List<Expenses> RepeatExpenses(Expenses expense, int qtyMonths)
        {
            List<Expenses> expensesList = new();

            string reference = expense.Reference;

            for (int i = 0; i <= qtyMonths; i++)
            {
                DateTime? dueDate = ReferenceDateHelper.GetProportionalDate(
                    expense.DueDate,
                    expense.Reference,
                    reference,
                    expense.DueDay);

                Expenses item = new()
                {
                    UserId        = expense.UserId,
                    Reference     = reference,
                    Position      = expense.Id > 0 && i == 0 ? expense.Position : GetNewPosition(reference),
                    Description   = expense.Description,
                    ToPay         = expense.ToPay,
                    Paid          = i == 0 ? expense.Paid : 0,
                    Note          = expense.Note,
                    CardId        = expense.CardId,
                    AccountId     = expense.AccountId,
                    DueDate       = dueDate,
                    ParcelNumber  = expense.ParcelNumber,
                    Parcels       = expense.Parcels,
                    TotalToPay    = expense.TotalToPay,
                    CategoryId    = expense.CategoryId,
                    Scheduled     = expense.Scheduled,
                    PeopleId      = expense.PeopleId,
                    DueDay        = expense.DueDay,
                    ExpectedValue = expense.ExpectedValue,
                    Fixed         = expense.Fixed
                };

                expensesList.Add(item);

                reference = GetNewReference(reference);
            }

            return expensesList;
        }

        private static DateTime? GetFutureDueDate(Expenses sourceExpense, string sourceReference, string targetReference)
        {
            return ReferenceDateHelper.GetProportionalDate(
                sourceExpense.DueDate,
                sourceReference,
                targetReference,
                sourceExpense.DueDay);
        }

        private static ExpensesDTO ExpensesToDTO(Expenses expense) =>
            new()
            {
                Id            = expense.Id,
                UserId        = expense.UserId,
                Reference     = expense.Reference,
                Position      = expense.Position,
                Description   = expense.Description,
                ToPay         = expense.ToPay,
                Paid          = expense.Paid,
                Remaining     = expense.ToPay - Math.Abs(expense.Paid),
                Note          = expense.Note,
                CardId        = expense.CardId,
                AccountId     = expense.AccountId,
                DueDate       = expense.DueDate,
                ParcelNumber  = expense.ParcelNumber,
                Parcels       = expense.Parcels,
                TotalToPay    = expense.TotalToPay,
                CategoryId    = expense.CategoryId,
                Category      = expense.Category?.Name,
                Scheduled     = expense.Scheduled,
                PeopleId      = expense.PeopleId,
                RelatedId     = expense.RelatedId,
                Fixed         = expense.Fixed,
                DueDay        = expense.DueDay,
                ExpectedValue = expense.ExpectedValue
            };

        private static ExpensesDTO2 ExpensesToComboList(Expenses expense) =>
        new()
        {
            Id          = expense.Id,
            Position    = expense.Position,
            Description = expense.Description,
            CategoryId  = expense.CategoryId
        };

        public async Task OrderByPreviousMonth(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference) || reference.Length != 6)
            {
                throw new ArgumentException("Referência inválida. O formato esperado é 'yyyyMM'.");
            }

            string previousReference = DateTime.ParseExact(reference, "yyyyMM", null).AddMonths(-1).ToString("yyyyMM");

            List<Expenses> previousExpenses = await _context.Expenses.Where(e => e.UserId == _user.Id && e.Reference == previousReference)
                                                                     .OrderBy(e => e.Position)
                                                                     .ToListAsync();

            if (!previousExpenses.Any())
            {
                throw new InvalidOperationException("Nenhuma despesa encontrada para o mês anterior.");
            }

            foreach (Expenses previousExpense in previousExpenses)
            {
                Expenses? expense = await _context.Expenses.Where(e => e.UserId == _user.Id &&
                                                                       e.Reference == reference &&
                                                                       e.Description == previousExpense.Description)
                                                           .FirstOrDefaultAsync();

                if (expense != null)
                {
                    expense.Position = previousExpense.Position;

                    if (expense.DueDate == null && previousExpense.DueDate != null)
                    {
                        expense.DueDate = ReferenceDateHelper.GetProportionalDate(
                            previousExpense.DueDate,
                            previousExpense.Reference,
                            reference,
                            previousExpense.DueDay);
                    }
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task<List<Expenses>> GetUpcomingOrOverdueExpenses(int daysAhead = 1)
        {
            DateTime today   = DateTime.Today;
            DateTime maxDate = today.AddDays(daysAhead);

            List<Expenses>? expenses = await _context.Expenses.Where(e => e.UserId == _user.Id &&
                                                                      e.DueDate != null &&
                                                                      e.Paid != e.ToPay &&
                                                                      (e.DueDate <= today || e.DueDate <= maxDate))
                                                               .OrderBy(e => e.DueDate)
                                                               .ToListAsync();

            return expenses;
        }

        public async Task<ExpensesDTO?> AjustarValorComBaseNaCategoria(int expenseId)
        {
            if (expenseId == 0)
                return null;

            Expenses? expense = await _context.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId && e.UserId == _user.Id);

            if (expense == null)
                return null;

            if (expense.CategoryId.HasValue)
                throw new InvalidOperationException("A despesa já está vinculada a uma categoria.");

            // Usa o método que já consolida despesas e lançamentos de cartão
            ExpensesByCategories? summarizedCategory = await _context.GetExpensesByCategories(expense.Reference!, expense.CardId ?? 0, _user.Id)
                                                                     .FirstOrDefaultAsync(r => r.Category!.TrimEnd() == expense.Description!.TrimEnd());

            // Recebimentos relacionados à despesa, sem categoria
            decimal received = await _context.AccountsPostings.Where(ap => ap.ExpenseId == expense.Id)
                                                              .SumAsync(ap => ap.Amount);

            decimal paid       = Math.Abs(received);
            decimal expected   = expense.ExpectedValue ?? expense.ToPay;
            decimal summarized = summarizedCategory?.Amount ?? 0;
            decimal newValue   = expected - summarized - paid;

            expense.ToPay      = Math.Max(newValue, paid);
            expense.TotalToPay = expense.ToPay;
            expense.Paid       = paid;

            _context.Entry(expense).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return ExpensesToDTO(expense);
        }

        public async Task<int> RepeatFixedExpenses(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference) || reference.Length != 6)
            {
                throw new ArgumentException("Referência inválida. O formato esperado é 'yyyyMM'.");
            }

            // Obter a referência do mês anterior
            string previousReference = GetPreviousReference(reference);

            // Buscar despesas fixas do mês anterior
            List<Expenses> fixedExpenses = await _context.Expenses
                                                         .Where(e => e.UserId == _user.Id &&
                                                                     e.Reference == previousReference &&
                                                                     e.Fixed == true &&
                                                                     e.CardId == null)
                                                         .OrderBy(e => e.Position)
                                                         .ToListAsync();

            if (!fixedExpenses.Any())
            {
                return 0; // Nenhuma despesa fixa encontrada
            }

            int createdCount = 0;

            foreach (Expenses fixedExpense in fixedExpenses)
            {
                // Verifica se já existe uma despesa com a mesma descrição na referência de destino
                bool alreadyExists = await _context.Expenses.AnyAsync(e => e.UserId == _user.Id &&
                                                                           e.Reference == reference &&
                                                                           e.Description == fixedExpense.Description &&
                                                                           e.CategoryId == fixedExpense.CategoryId);

                if (!alreadyExists)
                {
                    DateTime? sourceDueDate = fixedExpense.DueDate;

                    if (!sourceDueDate.HasValue && fixedExpense.DueDay.HasValue)
                    {
                        sourceDueDate = DateTime.ParseExact(
                            fixedExpense.Reference,
                            "yyyyMM",
                            null);
                    }

                    DateTime? newDueDate = ReferenceDateHelper.GetProportionalDate(
                        sourceDueDate,
                        fixedExpense.Reference,
                        reference,
                        fixedExpense.DueDay);

                    // Criar nova despesa
                    var newExpense = new Expenses
                    {
                        UserId        = _user.Id,
                        Reference     = reference,
                        Position      = GetNewPosition(reference),
                        Description   = fixedExpense.Description,
                        ToPay         = fixedExpense.ToPay,
                        Paid          = 0, // Despesa nova começa com valor pago zerado
                        Note          = fixedExpense.Note,
                        CardId        = fixedExpense.CardId,
                        AccountId     = fixedExpense.AccountId,
                        DueDate       = newDueDate,
                        ParcelNumber  = null,
                        Parcels       = null,
                        TotalToPay    = fixedExpense.TotalToPay,
                        CategoryId    = fixedExpense.CategoryId,
                        Scheduled     = fixedExpense.Scheduled,
                        PeopleId      = fixedExpense.PeopleId,
                        DueDay        = fixedExpense.DueDay,
                        ExpectedValue = fixedExpense.ExpectedValue,
                        Fixed         = true,
                        RelatedId     = null
                    };

                    await FinancialResourceValidator.ValidateResourcesForCreateAsync(
                        _context,
                        _user.Id,
                        newExpense.CardId,
                        newExpense.AccountId);

                    _context.Expenses.Add(newExpense);
                    createdCount++;
                }
            }

            if (createdCount > 0)
            {
                await _context.SaveChangesAsync();
            }

            return createdCount;
        }

        private static decimal GetParcelAmount(decimal totalToPay, int parcels, int parcelNumber)
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

            decimal toPay = Math.Round(totalToPay / parcels, 2, MidpointRounding.AwayFromZero);

            decimal difference = totalToPay - (toPay * parcels);

            return parcelNumber == 1 ? toPay + difference : toPay;
        }

        private static decimal GetFutureToPay(Expenses sourceExpense, Expenses targetExpense)
        {
            if (sourceExpense.TotalToPay != 0 &&
                targetExpense.Parcels.HasValue &&
                targetExpense.Parcels.Value > 1 &&
                targetExpense.ParcelNumber.HasValue &&
                targetExpense.ParcelNumber.Value >= 1 &&
                targetExpense.ParcelNumber.Value <= targetExpense.Parcels.Value)
            {
                return GetParcelAmount(
                    sourceExpense.TotalToPay,
                    targetExpense.Parcels.Value,
                    targetExpense.ParcelNumber.Value);
            }

            return sourceExpense.ToPay;
        }
    }
}