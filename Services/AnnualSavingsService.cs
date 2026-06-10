using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Services
{
    public interface IAnnualSavingsService
    {
        Task<AnnualSavingsReportDTO> GetByYear(int year, bool includeCurrentMonth = true, bool includeNextMonths = false);
        Task<List<AnnualSavingsConsolidatedDTO>> GetConsolidated(bool includeCurrentYear = true, bool includeNextYears = false, bool includeCurrentMonth = true, bool includeNextMonths = false);
    }

    public class AnnualSavingsService : IAnnualSavingsService
    {
        private readonly BudgetContext _context;
        private readonly Users _user;

        public AnnualSavingsService(BudgetContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _user    = httpContextAccessor.HttpContext!.Items["User"] as Users ?? new Users();
        }

        public async Task<AnnualSavingsReportDTO> GetByYear(int year, bool includeCurrentMonth = true, bool includeNextMonths = false)
        {
            List<AnnualSavingsMonthProjectionDTO> rows = await _context
                .GetAnnualSavings(year, _user.Id, includeCurrentMonth, includeNextMonths)
                .AsNoTracking()
                .ToListAsync();

            List<AnnualSavingsMonthDTO> monthRows = rows
                .Select(row => new AnnualSavingsMonthDTO
                {
                    Reference          = row.Reference,
                    Month              = row.MonthNumber,
                    MonthName          = row.MonthName,
                    Total              = row.Total,
                    GeneralBalance     = row.GeneralBalance,
                    RealGeneralBalance = row.RealGeneralBalance,
                    HasData            = row.HasData
                })
                .ToList();

            ApplyQuarterAverages(monthRows);

            List<decimal> values = monthRows.Where(m => m.Total.HasValue).Select(m => m.Total!.Value).ToList();
            decimal total = values.Sum();
            int months = values.Count;
            decimal average = months > 0 ? Math.Round(total / months, 2) : 0;

            AnnualSavingsMonthDTO? lastVisibleMonth = monthRows.LastOrDefault(m => m.GeneralBalance.HasValue || m.RealGeneralBalance.HasValue);

            return new AnnualSavingsReportDTO
            {
                Year               = year,
                Total              = total,
                Months             = months,
                Average            = average,
                GeneralBalance     = lastVisibleMonth?.GeneralBalance ?? 0,
                RealGeneralBalance = lastVisibleMonth?.RealGeneralBalance ?? 0,
                MonthRows          = monthRows
            };
        }

        public async Task<List<AnnualSavingsConsolidatedDTO>> GetConsolidated(bool includeCurrentYear = true, bool includeNextYears = false, bool includeCurrentMonth = true, bool includeNextMonths = false)
        {
            return await _context
                .GetAnnualSavingsConsolidated(_user.Id, includeCurrentYear, includeNextYears, includeCurrentMonth, includeNextMonths)
                .AsNoTracking()
                .OrderBy(c => c.IsTotal)
                .ThenBy(c => c.Year)
                .ToListAsync();
        }

        private void ApplyQuarterAverages(List<AnnualSavingsMonthDTO> monthRows)
        {
            for (int quarter = 1; quarter <= 4; quarter++)
            {
                List<AnnualSavingsMonthDTO> quarterRows = monthRows
                    .Where(m => ((m.Month - 1) / 3) + 1 == quarter)
                    .ToList();

                List<decimal> values = quarterRows
                    .Where(m => m.Total.HasValue)
                    .Select(m => m.Total!.Value)
                    .ToList();

                decimal? average = values.Any() ? Math.Round(values.Sum() / values.Count, 2) : null;
                AnnualSavingsMonthDTO firstRow = quarterRows.First();

                firstRow.ShowQuarterAverage = true;
                firstRow.QuarterAverage = average;
                firstRow.QuarterRowSpan = quarterRows.Count;
            }
        }
    }
}
