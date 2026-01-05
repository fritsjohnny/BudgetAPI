using BudgetAPI.Data;
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
        Task<int> PutIncomes(Incomes incomes);
        void PutIncomesWithParcels(Incomes incomes, int qtyMonths);
        Task<int> SetPositions(List<Incomes> incomes);
        Task<int> AddValue(Incomes income, decimal value);
        Task<int> PostIncomes(Incomes income);
        void PostIncomesWithParcels(Incomes incomes, int qtyMonths);
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

        public Task<int> PutIncomes(Incomes income)
        {
            _context.Entry(income).State = EntityState.Modified;

            return _context.SaveChangesAsync();
        }

        public void PutIncomesWithParcels(Incomes incomes, int qtyMonths)
        {
            _context.Entry(incomes).State = EntityState.Modified;

            var incomesList = RepeatIncomes(incomes, qtyMonths);

            foreach (Incomes cp in incomesList.Skip(1))
            {
                _context.Incomes.Add(cp);

                _context.SaveChanges();
            }
        }

        public Task<int> SetPositions(List<Incomes> incomes)
        {
            foreach (Incomes income in incomes)
            {
                _context.Entry(income).State = EntityState.Modified;
            }

            return _context.SaveChangesAsync();
        }

        public Task<int> AddValue(Incomes income, decimal value)
        {
            income.ToReceive += value;

            _context.Entry(income).State = EntityState.Modified;

            return _context.SaveChangesAsync();
        }

        public Task<int> PostIncomes(Incomes income)
        {
            income.UserId = _user.Id;

            _context.Incomes.Add(income);

            return _context.SaveChangesAsync();
        }

        public void PostIncomesWithParcels(Incomes incomes, int qtyMonths)
        {
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
                _context.SaveChanges();

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
            _context.Incomes.Remove(income);

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
            var newPosition = _context.Incomes.Where(e => e.Reference == reference).Max(e => e.Position) ?? 0;

            return ++newPosition;
        }

        private List<Incomes> RepeatIncomes(Incomes income, int qtyMonths)
        {
            var incomesList = new List<Incomes>();

            var reference = income.Reference;

            for (int i = 1; i <= (qtyMonths + 1); i++)
            {
                var e = new Incomes
                {
                    UserId       = income.UserId,
                    Reference    = reference,
                    Position     = income.Id > 0 && i == 1 ? income.Position : GetNewPosition(reference),
                    Description  = income.Description,
                    ToReceive    = income.ToReceive,
                    Received     = income.Received,
                    Note         = income.Note,
                    CardId       = income.CardId,
                    AccountId    = income.AccountId,
                    Type         = income.Type,
                    PeopleId     = income.PeopleId
                };

                incomesList.Add(e);

                reference = GetNewReference(reference);
            }

            return incomesList;
        }

        private static IncomesDTO IncomesToDTO(Incomes income) =>
            new IncomesDTO
            {
                Id          = income.Id,
                UserId      = income.UserId,
                Reference   = income.Reference,
                Position    = income.Position,
                Description = income.Description,
                ToReceive   = income.ToReceive,
                Received    = income.Received,
                Remaining   = income.ToReceive - income.Received,
                Note        = income.Note,
                CardId      = income.CardId,
                AccountId   = income.AccountId,
                Type        = income.Type,
                PeopleId    = income.PeopleId,
                RelatedId   = income.RelatedId
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
                throw new ArgumentException("Referência inválida. O formato esperado é 'yyyyMM'.");

            string previousReference = DateTime.ParseExact(reference, "yyyyMM", null).AddMonths(-1).ToString("yyyyMM");

            List<Incomes> previousIncomes = await _context.Incomes.Where(e => e.UserId == _user.Id && e.Reference == previousReference)
                                                          .OrderBy(e => e.Position)
                                                          .ToListAsync();

            if (!previousIncomes.Any())
                throw new InvalidOperationException("Nenhuma receita encontrada para o mês anterior.");

            List<Incomes> currentIncomes = await _context.Incomes.Where(e => e.UserId == _user.Id && e.Reference == reference)
                                                         .ToListAsync();

            // aplica posições do mês anterior pelo Description
            foreach (Incomes prev in previousIncomes)
            {
                Incomes? income = currentIncomes.FirstOrDefault(e => e.Description == prev.Description);
                
                if (income != null)
                    income.Position = prev.Position;
            }

            // normaliza: garante Position única e sequencial
            List<Incomes> ordered = currentIncomes.OrderBy(e => e.Position)
                                                  .ThenBy(e => e.Description)
                                                  .ToList();

            short pos = 1;

            foreach (Incomes income in ordered)
                income.Position = pos++;

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

                foreach (Incomes prev in previousIncomes.Where(x => x.CardId == null && x.Description != "Tarifa"))
                {
                    bool isYield = string.Equals(prev.Type, "Y", StringComparison.OrdinalIgnoreCase);

                    _context.Incomes.Add(new Incomes
                    {
                        UserId      = _user.Id,
                        Reference   = reference,
                        Position    = prev.Position,
                        Description = prev.Description,
                        ToReceive   = isYield ? 0 : prev.ToReceive,
                        Received    = 0,
                        Note        = prev.Note,
                        CardId      = null,
                        AccountId   = prev.AccountId,
                        Type        = prev.Type,
                        PeopleId    = prev.PeopleId,
                        RelatedId   = null
                    });
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
                        UserId      = _user.Id,
                        Reference   = reference,
                        Position    = (short)(maxPos + 1),
                        Description = "Tarifa",
                        ToReceive   = tarifa,
                        Received    = 0,
                        Note        = null,
                        CardId      = null,
                        AccountId   = null,
                        Type        = "R",
                        PeopleId    = null,
                        RelatedId   = null
                    });
                }
                else
                {
                    tarifaIncome.ToReceive = tarifa;
                    tarifaIncome.Received  = 0;
                }

                await _context.SaveChangesAsync();

                await OrderByPreviousMonth(reference);

                await tx.CommitAsync();
            }
        }
    }
}
