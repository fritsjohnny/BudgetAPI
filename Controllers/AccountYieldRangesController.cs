using BudgetAPI.Authorization;
using BudgetAPI.Models;
using BudgetAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetAPI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class AccountYieldRangesController : ControllerBase
    {
        private readonly IAccountYieldRangeService _accountYieldRangeService;

        public AccountYieldRangesController(IAccountYieldRangeService accountYieldRangeService)
        {
            _accountYieldRangeService = accountYieldRangeService;
        }

        [HttpGet("ByAccount/{accountId:int}")]
        public async Task<ActionResult<IEnumerable<AccountYieldRanges>>> GetByAccount(int accountId)
        {
            List<AccountYieldRanges> ranges = await _accountYieldRangeService.GetByAccount(accountId).ToListAsync();

            return Ok(ranges);
        }

        [HttpPut("ByAccount/{accountId:int}/Replace")]
        public async Task<IActionResult> ReplaceByAccount(int accountId, List<AccountYieldRanges> ranges)
        {
            try
            {
                await _accountYieldRangeService.ReplaceByAccount(accountId, ranges);

                return Ok();
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }
        }
    }
}