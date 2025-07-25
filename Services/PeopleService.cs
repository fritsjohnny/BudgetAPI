using BudgetAPI.Data;
using BudgetAPI.Models;

namespace BudgetAPI.Services
{
    public interface IPeopleService
    {
        IQueryable<People> GetPeople();
        IQueryable<People> GetPeople(int id);
        Task<int> PutPeople(People people);
        Task<int> PostPeople(People people);
        Task<int> DeletePeople(People people);
        bool PeopleExists(int id);
        bool ValidarUsuario(int id);
    }
    public class PeopleService : IPeopleService
    {
        private readonly BudgetContext _context;

        private readonly Users _user;

        public PeopleService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _user    = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
        }

        public IQueryable<People> GetPeople()
        {
            IQueryable<People> query = _context.People.Where(a => a.UserId == _user.Id);

            return query;
        }

        public IQueryable<People> GetPeople(int id)
        {
            var people = _context.People.Where(a => a.UserId == _user.Id && a.Id == id);

            return people;
        }

        public async Task<int> PutPeople(People people)
        {
            People? existing = await _context.People.FindAsync(people.Id);

            if (existing == null)
                return 0;

            existing.Name         = people.Name;
            existing.PhoneNumber  = people.PhoneNumber;

            return await _context.SaveChangesAsync();
        }

        public Task<int> PostPeople(People people)
        {
            people.UserId = _user.Id;

            _context.People.Add(people);

            return _context.SaveChangesAsync();
        }

        public Task<int> DeletePeople(People people)
        {
            _context.People.Remove(people);

            return _context.SaveChangesAsync();
        }

        public bool PeopleExists(int id)
        {
            return _context.People.Any(e => e.Id == id && e.UserId == _user.Id);
        }

        public bool ValidarUsuario(int id)
        {
            return id == _user.Id;
        }
    }
}
