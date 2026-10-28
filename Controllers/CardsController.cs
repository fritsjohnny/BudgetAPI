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
    public class CardsController : ControllerBase
    {
        private readonly ICardService _cardService;
        private readonly Microsoft.AspNetCore.DataProtection.IDataProtector _statementPasswordProtector;

        public CardsController(ICardService cardService, Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtectionProvider)
        {
            _cardService = cardService;
            _statementPasswordProtector = dataProtectionProvider.CreateProtector("BudgetAPI.Cards.StatementPassword.v1");
        }

        // GET: api/Card
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CardsDTO>>> GetCards()
        {
            return await _cardService.GetCards().ToListAsync();
        }

        // GET: api/Card/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Cards>> GetCards(int id)
        {
            var card = await _cardService.GetCards(id).FirstOrDefaultAsync();

            if (card == null)
            {
                return NotFound();
            }

            return card;
        }

        // PUT: api/Card/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutCard(int id, Cards card)
        {
            if (id != card.Id || !_cardService.ValidarUsuario(card.UserId))
            {
                return BadRequest();
            }

            try
            {
                Cards? existingCard = await _cardService.GetCards(id).FirstOrDefaultAsync();
                if (existingCard == null) return NotFound();

                card.StatementPasswordProtected = !string.IsNullOrEmpty(card.StatementPassword)
                    ? Convert.ToBase64String(_statementPasswordProtector.Protect(System.Text.Encoding.UTF8.GetBytes(card.StatementPassword)))
                    : card.ClearStatementPassword
                        ? null
                        : existingCard.StatementPasswordProtected;
                card.StatementPassword = null;
                card.ClearStatementPassword = false;
                await _cardService.PutCard(id, card);
            }
            catch (DbUpdateConcurrencyException dex)
            {
                if (!_cardService.CardExists(id))
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

        // POST: api/Card
        [HttpPost]
        public async Task<ActionResult<Cards>> PostCard(Cards card)
        {
            card.StatementPasswordProtected = string.IsNullOrEmpty(card.StatementPassword)
                ? null
                : Convert.ToBase64String(_statementPasswordProtector.Protect(System.Text.Encoding.UTF8.GetBytes(card.StatementPassword)));
            card.StatementPassword = null;
            card.ClearStatementPassword = false;
            await _cardService.PostCard(card);

            return CreatedAtAction("GetCards", new { id = card.Id }, card);
        }

        // DELETE: api/Card/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCard(int id)
        {
            Cards? card = await _cardService.GetCards(id).FirstOrDefaultAsync();

            if (card == null)
            {
                return NotFound();
            }

            if (!_cardService.ValidarUsuario(card.UserId))
            {
                return BadRequest();
            }

            await _cardService.DeleteCard(card);

            return Ok();
        }

        [HttpGet("Available")]
        public async Task<ActionResult<IEnumerable<CardsDTO>>> GetAvailableCards(string reference, DateTime? currentDate)
        {
            DateTime effectiveCurrentDate = (currentDate ?? DateTime.Today).Date;

            return await _cardService.GetAvailableCards(reference, effectiveCurrentDate).ToListAsync();
        }
    }
}
