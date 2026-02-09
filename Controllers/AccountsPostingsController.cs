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
    public class AccountsPostingsController : ControllerBase
    {
        private readonly IAccountPostingService _accountPostingService;

        public AccountsPostingsController(IAccountPostingService accountPostingService)
        {
            _accountPostingService = accountPostingService;
        }

        // GET: api/AccountsPostings
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AccountsPostings>>> GetAccountsPostings()
        {
            return await _accountPostingService.GetAccountsPostings().ToListAsync();
        }

        // GET: api/AccountsPostings/5
        [HttpGet("{id}")]
        public async Task<ActionResult<AccountsPostings>> GetAccountsPostings(int id)
        {
            AccountsPostings? accountsPostings = await _accountPostingService.GetAccountsPostings(id).FirstOrDefaultAsync();

            if (accountsPostings == null)
            {
                return NotFound();
            }

            return accountsPostings;
        }

        [HttpGet("{accountId}/{reference}")]
        public async Task<ActionResult<IEnumerable<AccountsPostings>>> GetAccountsPostings(int accountId, string reference)
        {
            var accountsPostings = await _accountPostingService.GetAccountsPostings(accountId, reference).ToListAsync();

            return accountsPostings;
        }

        // PUT: api/AccountsPostings/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutAccountsPostings(int id, AccountsPostings accountsPostings)
        {
            if (id != accountsPostings.Id || !_accountPostingService.ValidarUsuario(id))
            {
                return BadRequest();
            }

            // ✅ CORREÇÃO FINAL: Valida AMBAS as contas para transferências (não assume qual é origem/destino)
            bool isTransfer = accountsPostings.Type == "T" || 
                              accountsPostings.Type == "P" || 
                              accountsPostings.Type == "R" || 
                              accountsPostings.RelatedId != null;

            if (isTransfer)
            {
                // Valida AccountId (sempre presente)
                if (!_accountPostingService.ValidateAccountAndUser(accountsPostings.AccountId))
                {
                    return BadRequest("A conta informada é inválida ou não pertence ao usuário.");
                }

                // Valida ToAccountId (se informado)
                if (accountsPostings.ToAccountId.HasValue)
                {
                    if (!_accountPostingService.ValidateAccountAndUser(accountsPostings.ToAccountId.Value))
                    {
                        return BadRequest("A conta de destino é inválida ou não pertence ao usuário.");
                    }
                }
                else
                {
                    return BadRequest("Conta de destino é obrigatória para transferências.");
                }
            }

            try
            {
                await _accountPostingService.PutAccountsPostings(accountsPostings);
            }
            catch (DbUpdateConcurrencyException dex)
            {
                if (!_accountPostingService.AccountsPostingsExists(id))
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

        // POST: api/AccountsPostings
        [HttpPost]
        public async Task<ActionResult<AccountsPostings>> PostAccountsPostings(AccountsPostings accountsPostings)
        {
            if (!_accountPostingService.ValidateAccountAndUser(accountsPostings.AccountId))
            {
                return BadRequest("Conta de origem inválida ou não pertence ao usuário.");
            }

            // ✅ CORREÇÃO 7: Validação da conta de destino para transferências
            if (accountsPostings.Type == "T" && accountsPostings.ToAccountId.HasValue)
            {
                if (!_accountPostingService.ValidateAccountAndUser(accountsPostings.ToAccountId.Value))
                {
                    return BadRequest("Conta de destino inválida ou não pertence ao usuário.");
                }
            }

            try
            {
                await _accountPostingService.PostAccountsPostings(accountsPostings);
                return CreatedAtAction("GetAccountsPostings", new { id = accountsPostings.Id }, accountsPostings);
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }
        }

        // DELETE: api/AccountsPostings/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAccountsPostings(int id)
        {
            AccountsPostings? accountsPostings = await _accountPostingService.GetAccountsPostings(id).FirstOrDefaultAsync();

            if (accountsPostings == null)
            {
                return NotFound();
            }

            if (!_accountPostingService.ValidarUsuario(accountsPostings.Id))
            {
                return BadRequest();
            }

            try
            {
                await _accountPostingService.DeleteAccountsPostings(accountsPostings);
                return Ok();
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }
        }

        [HttpPut("SetPositions")]
        public async Task<ActionResult<AccountsPostings>> SetPositions(List<AccountsPostings> accountsPostings)
        {
            await _accountPostingService.SetPositions(accountsPostings);

            return Ok();
        }

        [HttpGet("yields")]
        public async Task<ActionResult<IEnumerable<AccountsYieldsDTO>>> GetYieldsAll()
        {
            var lista = await _accountPostingService.GetAccountsYields(null, null).ToListAsync();
            return Ok(lista);
        }

        [HttpGet("yields/{reference}")]
        public async Task<ActionResult<IEnumerable<AccountsYieldsDTO>>> GetYieldsByRef(string reference)
        {
            var lista = await _accountPostingService.GetAccountsYields(reference, null).ToListAsync();
            return Ok(lista);
        }

        [HttpGet("yields/{reference}/{accountId:int}")]
        public async Task<ActionResult<IEnumerable<AccountsYieldsDTO>>> GetYieldsByRefAccount(string reference, int accountId)
        {
            var lista = await _accountPostingService.GetAccountsYields(reference, accountId).ToListAsync();
            return Ok(lista);
        }
    }
}
