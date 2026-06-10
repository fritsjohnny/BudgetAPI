using BudgetAPI.Authorization;
using BudgetAPI.Models;
using BudgetAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetAPI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class AnnualSavingsController : ControllerBase
    {
        private readonly IAnnualSavingsService _annualSavingsService;

        public AnnualSavingsController(IAnnualSavingsService annualSavingsService)
        {
            _annualSavingsService = annualSavingsService;
        }

        [HttpGet("{year:int}")]
        public async Task<ActionResult<AnnualSavingsReportDTO>> GetByYear(int year, bool includeCurrentMonth = true, bool includeNextMonths = false)
        {
            return await _annualSavingsService.GetByYear(year, includeCurrentMonth, includeNextMonths);
        }

        [HttpGet("Consolidated")]
        public async Task<ActionResult<IEnumerable<AnnualSavingsConsolidatedDTO>>> GetConsolidated(bool includeCurrentYear = true, bool includeNextYears = false, bool includeCurrentMonth = true, bool includeNextMonths = false)
        {
            return await _annualSavingsService.GetConsolidated(includeCurrentYear, includeNextYears, includeCurrentMonth, includeNextMonths);
        }
    }
}
