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

            // Buscar do banco para verificar se É transferência
            var existingPosting = await _accountPostingService.GetAccountsPostings(id).FirstOrDefaultAsync();
            
            if (existingPosting == null)
            {
                return NotFound();
            }

            // Verifica se o registro no BANCO é transferência (não confia no request)
            bool isTransferInDatabase = existingPosting.RelatedId.HasValue;

            if (isTransferInDatabase)
            {
                // É transferência no banco - validar como transferência independente do que veio no request
                if (!accountsPostings.ToAccountId.HasValue)
                {
                    return BadRequest("Conta de destino é obrigatória para transferências.");
                }

                // Valida AccountId
                if (!_accountPostingService.ValidateAccountAndUser(accountsPostings.AccountId))
                {
                    return BadRequest("A conta informada é inválida ou não pertence ao usuário.");
                }

                // Valida ToAccountId
                if (!_accountPostingService.ValidateAccountAndUser(accountsPostings.ToAccountId.Value))
                {
                    return BadRequest("A conta de destino é inválida ou não pertence ao usuário.");
                }

                // Força Type="T" e RelatedId para o service processar como transferência
                accountsPostings.Type = "T";
                accountsPostings.RelatedId = existingPosting.RelatedId;
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

                return Problem(
                    detail: GetDetailedErrorMessage(dex),
                    title: "Erro de concorrência ao atualizar lançamento"
                );
            }
            catch (Exception ex)
            {
                return Problem(
                    detail: GetDetailedErrorMessage(ex),
                    title: "Erro ao atualizar lançamento"
                );
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

            // Validação da conta de destino para transferências
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
                return Problem(
                    detail: GetDetailedErrorMessage(ex),
                    title: "Erro ao criar lançamento"
                );
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
                return Problem(
                    detail: GetDetailedErrorMessage(ex),
                    title: "Erro ao excluir lançamento"
                );
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

        private string GetDetailedErrorMessage(Exception ex)
        {
            var errorDetails = $"Mensagem: {ex.Message}";
            
            if (ex.InnerException != null)
            {
                errorDetails += $"\n\nInner Exception: {ex.InnerException.Message}";
                
                if (ex.InnerException.InnerException != null)
                {
                    errorDetails += $"\n\nInner Exception (2): {ex.InnerException.InnerException.Message}";
                }
            }
            
            errorDetails += $"\n\nTipo: {ex.GetType().FullName}";
            errorDetails += $"\n\nStack Trace:\n{ex.StackTrace}";
            
            return errorDetails;
        }

        [HttpPost("GenerateCardReceipt")]
        public async Task<ActionResult<int>> GenerateCardReceiptFromAccountPosting([FromQuery] int accountPostingId, [FromQuery] int cardId, [FromQuery] int peopleId)
        {
            int id = await _accountPostingService.GenerateCardReceiptFromAccountPosting(accountPostingId, cardId, peopleId);
           
            return Ok(id);
        }
    }
}
