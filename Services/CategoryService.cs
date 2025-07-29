using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface ICategoryService
    {
        IQueryable<Categories> GetCategories();
        IQueryable<Categories> GetCategories(int id);
        Task<int> PutCategories(Categories category);
        Task<int> PostCategories(Categories category);
        Task<int> DeleteCategories(Categories category);
        bool CategoriesExists(int id);
        bool ValidarUsuario(int id);
        Task<List<CategoriesDTO>> GetCategoriesWithExpenseStatus(string reference);
    }

    public class CategoryService : ICategoryService
    {
        private readonly BudgetContext _context;

        private readonly Users _user;

        public CategoryService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _user    = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
        }

        public IQueryable<Categories> GetCategories()
        {
            IQueryable<Categories> query = _context.Categories.Where(a => a.UserId == _user.Id);

            return query;
        }

        public IQueryable<Categories> GetCategories(int id)
        {
            var categories = _context.Categories.Where(a => a.UserId == _user.Id && a.Id == id);

            return categories;
        }

        public Task<int> PutCategories(Categories category)
        {
            _context.Entry(category).State = EntityState.Modified;

            return _context.SaveChangesAsync();
        }

        public Task<int> PostCategories(Categories category)
        {
            category.UserId = _user.Id;

            _context.Categories.Add(category);

            return _context.SaveChangesAsync();
        }

        public Task<int> DeleteCategories(Categories category)
        {
            _context.Categories.Remove(category);

            return _context.SaveChangesAsync();
        }

        public bool CategoriesExists(int id)
        {
            return _context.Categories.Any(e => e.Id == id);
        }

        public bool ValidarUsuario(int id)
        {
            return id == _user.Id;
        }

        public async Task<List<CategoriesDTO>> GetCategoriesWithExpenseStatus(string reference)
        {
            // Busca todas as categorias do usuário
            List<Categories> categories = await _context.Categories.Where(c => c.UserId == _user.Id)
                                                                   .ToListAsync();

            // Busca todas as despesas da referência que não têm categoria definida, mas têm Name
            List<Expenses> expenses = await _context.Expenses.Where(e => e.UserId == _user.Id &&
                                                                        e.Reference == reference &&
                                                                        e.CategoryId == null &&
                                                                        e.CardId == null)
                                                             .ToListAsync();

            List<CategoriesDTO> result = categories.Select(cat => new CategoriesDTO
                                                    {
                                                        Id         = cat.Id,
                                                        Name       = cat.Name,
                                                        HasExpense = expenses.Any(e => e.Description!.Trim() == cat.Name.Trim())
                                                    }).ToList();

            return result;
        }
    }
}
