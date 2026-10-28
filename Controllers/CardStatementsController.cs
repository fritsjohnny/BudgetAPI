using BudgetAPI.Authorization;
using BudgetAPI.Models;
using BudgetAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetAPI.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CardStatementsController : ControllerBase
    {
        private readonly ICardStatementService _cardStatementService;

        public CardStatementsController(ICardStatementService cardStatementService)
        {
            _cardStatementService = cardStatementService;
        }

        [HttpPost("C6/Preview")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<ActionResult<C6StatementPreview>> PreviewC6([FromQuery] int cardId, [FromForm] IFormFile? file)
        {
            if (file == null)
            {
                return BadRequest(new { message = "Selecione o arquivo ZIP da fatura C6." });
            }

            try
            {
                return Ok(await _cardStatementService.PreviewC6Async(file, cardId));
            }
            catch (CardStatementImportException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("PhysicalCards")]
        public async Task<ActionResult<IEnumerable<CardsPhysicalCard>>> GetPhysicalCards([FromQuery] int cardId)
        {
            try
            {
                return Ok(await _cardStatementService.GetPhysicalCardsAsync(cardId));
            }
            catch (CardStatementImportException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("PhysicalCards/{cardId}")]
        public async Task<ActionResult<IEnumerable<CardsPhysicalCard>>> SavePhysicalCards(
            int cardId,
            [FromBody] List<CardsPhysicalCardInput> cards)
        {
            try
            {
                return Ok(await _cardStatementService.SavePhysicalCardsAsync(cardId, cards));
            }
            catch (CardStatementImportException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("DescriptionAliases")]
        public async Task<ActionResult<IEnumerable<CardStatementDescriptionAlias>>> GetDescriptionAliases(
            [FromQuery] int cardId,
            [FromQuery] int physicalCardId)
        {
            try
            {
                return Ok(await _cardStatementService.GetAliasesAsync(cardId, physicalCardId));
            }
            catch (CardStatementImportException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("DescriptionAliases")]
        public async Task<ActionResult<CardStatementDescriptionAlias>> SaveDescriptionAlias(
            [FromBody] CardStatementDescriptionAliasInput input)
        {
            try
            {
                return Ok(await _cardStatementService.SaveAliasAsync(input));
            }
            catch (CardStatementImportException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("DescriptionAliases/{aliasId}")]
        public async Task<IActionResult> DeleteDescriptionAlias([FromQuery] int cardId, int aliasId)
        {
            try
            {
                bool deleted = await _cardStatementService.DeleteAliasAsync(cardId, aliasId);
                return deleted ? NoContent() : NotFound();
            }
            catch (CardStatementImportException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("ReconciliationMatches")]
        public async Task<ActionResult<IEnumerable<CardStatementReconciliationMatchDTO>>> GetReconciliationMatches(
            [FromQuery] int cardId,
            [FromQuery] string reference)
        {
            try
            {
                return Ok(await _cardStatementService.GetReconciliationMatchesAsync(cardId, reference));
            }
            catch (CardStatementImportException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("ReconciliationMatches")]
        public async Task<ActionResult<CardStatementReconciliationMatchDTO>> SaveReconciliationMatch(
            [FromBody] CardStatementReconciliationMatchInput input)
        {
            try
            {
                return Ok(await _cardStatementService.SaveReconciliationMatchAsync(input));
            }
            catch (CardStatementImportException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("ReconciliationMatches/Batch")]
        public async Task<ActionResult<IEnumerable<CardStatementReconciliationMatchDTO>>> SaveReconciliationMatches(
            [FromBody] List<CardStatementReconciliationMatchInput> inputs)
        {
            try
            {
                return Ok(await _cardStatementService.SaveReconciliationMatchesAsync(inputs));
            }
            catch (CardStatementImportException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
