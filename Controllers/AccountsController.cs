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
    public class AccountsController : ControllerBase
    {
        private readonly IAccountService _accountService;
        private readonly IInvestmentStrategyService _investmentStrategyService;

        public AccountsController(IAccountService accountService, IInvestmentStrategyService investmentStrategyService)
        {
            _accountService = accountService;
            _investmentStrategyService = investmentStrategyService;
        }

        [HttpPost("InvestmentStrategyReport")]
        public async Task<ActionResult<InvestmentStrategyReportDTO>> GetInvestmentStrategyReport([FromBody] InvestmentStrategyRequestDTO request)
        {
            try { return Ok(await _investmentStrategyService.GetReport(request)); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return NotFound(ex.Message); }
        }

        [HttpGet("Totals")]
        public async Task<ActionResult<AccountsDTO>> GetAccountTotals(int account, string reference)
        {
            return await _accountService.GetAccountTotals(account, reference);
        }

        [HttpGet("AccountsSummary")]
        public async Task<ActionResult<IEnumerable<AccountsSummary>>> GetAccountsSummary(string reference)
        {
            return await _accountService.GetAccountsSummary(reference).ToListAsync();
        }

        [HttpGet("SummaryTotals")]
        public async Task<ActionResult<AccountsSummaryTotals>> GetAccountsSummaryTotals(string reference)
        {
            return await _accountService.GetAccountsSummaryTotals(reference).FirstOrDefaultAsync() ?? new AccountsSummaryTotals();
        }

        [HttpGet("ForecastBalanceReport")]
        public async Task<ActionResult<AccountForecastBalanceReportDTO>> GetForecastBalanceReport(
            [FromQuery] int accountId,
            [FromQuery] DateTime initialDate,
            [FromQuery] DateTime finalDate)
        {
            try
            {
                AccountForecastBalanceReportDTO report = await _accountService.GetForecastBalanceReport(
                    accountId,
                    initialDate,
                    finalDate);

                return Ok(report);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                return Problem(
                    detail: ex.Message,
                    title: "Erro ao gerar o relatório de saldo previsto em conta");
            }
        }

        // GET: api/Accounts
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Accounts>>> GetAccount()
        {
            return await _accountService.GetAccount().ToListAsync();
        }

        // GET: api/Accounts/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Accounts>> GetAccount(int id)
        {
            Accounts? accounts = await _accountService.GetAccount(id).FirstOrDefaultAsync();

            if (accounts == null)
            {
                return NotFound();
            }

            return accounts;
        }

        // PUT: api/Accounts/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutAccount(int id, Accounts account)
        {
            if (id != account.Id || !_accountService.ValidarUsuario(account.UserId))
            {
                return BadRequest();
            }

            try
            {
                await _accountService.PutAccount(account);
            }
            catch (DbUpdateConcurrencyException dex)
            {
                if (!_accountService.AccountExists(id))
                {
                    return NotFound();
                }

                return Problem(dex.Message);
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }

            return Ok();
        }

        // POST: api/Accounts
        [HttpPost]
        public async Task<ActionResult<Accounts>> PostAccount(Accounts account)
        {
            await _accountService.PostAccount(account);

            return CreatedAtAction("GetAccount", new { id = account.Id }, account);
        }

        // DELETE: api/Accounts/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAccount(int id)
        {
            Accounts? account = await _accountService.GetAccount(id).FirstOrDefaultAsync();

            if (account == null)
            {
                return NotFound();
            }

            if (!_accountService.ValidarUsuario(account.UserId))
            {
                return BadRequest();
            }

            await _accountService.DeleteAccount(account);

            return Ok();
        }

        [HttpPut("SetPositions")]
        public async Task<ActionResult<Accounts>> SetPositions(List<Accounts> accounts)
        {
            await _accountService.SetPositions(accounts);

            return Ok();
        }

        [HttpGet("Available")]
        public async Task<ActionResult<IEnumerable<Accounts>>> GetAvailableAccounts(string reference)
        {
            return await _accountService.GetAvailableAccounts(reference).ToListAsync();
        }
    }
}
