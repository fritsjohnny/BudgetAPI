using BudgetAPI.Authorization;
using BudgetAPI.Models;
using BudgetAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetAPI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class FinancialHealthController : ControllerBase
    {
        private readonly IFinancialHealthService _financialHealthService;

        public FinancialHealthController(IFinancialHealthService financialHealthService)
        {
            _financialHealthService = financialHealthService;
        }

        [HttpGet]
        public async Task<ActionResult<FinancialHealthReportDTO>> GetReport(
            [FromQuery] string initialReference,
            [FromQuery] string finalReference,
            [FromQuery] int reserveTargetMonths = 9,
            [FromQuery] int futureMonths = 12,
            [FromQuery] bool includeCurrentMonth = false)
        {
            try
            {
                FinancialHealthReportDTO report = await _financialHealthService.GetReport(
                    initialReference,
                    finalReference,
                    reserveTargetMonths,
                    futureMonths,
                    includeCurrentMonth);

                return Ok(report);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return Problem(
                    detail: ex.Message,
                    title: "Erro ao gerar o relatório de saúde financeira");
            }
        }
    }
}
