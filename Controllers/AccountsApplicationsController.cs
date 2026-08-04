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
    public class AccountsApplicationsController : ControllerBase
    {
        private readonly IAccountApplicationService _accountsApplicationsService;

        public AccountsApplicationsController(IAccountApplicationService service)
        {
            _accountsApplicationsService = service;
        }

        // GET: api/AccountsApplications
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AccountsApplications>>> GetAll()
        {
            IQueryable<AccountsApplications> query = _accountsApplicationsService.GetApplications();
            List<AccountsApplications> list = await query.ToListAsync();
            return list;
        }

        // GET: api/AccountsApplications/ByAccount/5
        [HttpGet("ByAccount/{accountId}")]
        public async Task<ActionResult<IEnumerable<AccountsApplications>>> GetByAccount(int accountId)
        {
            IQueryable<AccountsApplications> query = _accountsApplicationsService.GetApplicationsByAccount(accountId);
            List<AccountsApplications> list = await query.ToListAsync();
            return list;
        }

        // GET: api/AccountsApplications/5
        [HttpGet("{id}")]
        public async Task<ActionResult<AccountsApplications>> Get(int id)
        {
            AccountsApplications? app = await _accountsApplicationsService.GetApplication(id).FirstOrDefaultAsync();

            if (app == null)
                return NotFound();

            return app;
        }

        // POST: api/AccountsApplications
        [HttpPost]
        public async Task<ActionResult<AccountsApplications>> Post(AccountsApplications application)
        {
            try
            {
                await _accountsApplicationsService.PostApplication(application);
            }
            catch (UnauthorizedAccessException uaex)
            {
                return Problem(uaex.Message, statusCode: 403);
            }
            catch (ArgumentException aex)
            {
                return BadRequest(aex.Message);
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }

            return CreatedAtAction(nameof(Get), new { id = application.Id }, application);
        }

        // POST: api/AccountsApplications/Bulk
        [HttpPost("Bulk")]
        public async Task<IActionResult> Bulk(List<AccountsApplications> applications)
        {
            try
            {
                int rows = await _accountsApplicationsService.BulkInsertApplications(applications);
                return Ok(rows);
            }
            catch (UnauthorizedAccessException uaex)
            {
                return Problem(uaex.Message, statusCode: 403);
            }
            catch (ArgumentException aex)
            {
                return BadRequest(aex.Message);
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }
        }

        // PUT: api/AccountsApplications/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Put(int id, AccountsApplications application)
        {
            if (id != application.Id)
                return BadRequest("Id do recurso não confere com o payload.");

            // reforço de segurança: só permite alterar se a conta pertence ao usuário
            if (!_accountsApplicationsService.ValidateAccountOwnership(application.AccountId))
                return Problem("Conta não pertence ao usuário.", statusCode: 403);

            try
            {
                await _accountsApplicationsService.PutApplication(application);
            }
            catch (DbUpdateConcurrencyException dex)
            {
                if (!_accountsApplicationsService.ApplicationExists(id))
                    return NotFound();

                return Problem(dex.Message);
            }
            catch (UnauthorizedAccessException uaex)
            {
                return Problem(uaex.Message, statusCode: 403);
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }

            return Ok();
        }

        // DELETE: api/AccountsApplications/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _accountsApplicationsService.DisableApplication(id);
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }

            return Ok();
        }
    }
}
