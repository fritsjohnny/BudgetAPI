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
    public class CardsPostingsController : ControllerBase
    {
        private readonly ICardPostingService _cardPostingService;

        public CardsPostingsController(ICardPostingService cardPostingService)
        {
            _cardPostingService = cardPostingService;
        }

        // GET: api/CardsPostings
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CardsPostings>>> GetCardsPostings()
        {
            return await _cardPostingService.GetCardsPostings().ToListAsync();
        }

        // GET: api/CardsPostings/5
        [HttpGet("{id}")]
        public async Task<CardsPostingsDTO?> GetCardsPostings(int id)
        {
            CardsPostingsDTO? cardsPostings = await _cardPostingService.GetCardsPostingsById(id).FirstOrDefaultAsync();

            return cardsPostings;
        }

        [HttpGet("ByDescription")]
        public async Task<CardsPostings?> ByDescription([FromQuery] string description)
        {
            CardsPostings? cardsPostings = await _cardPostingService.GetCardsPostingsByDescription(description);

            return cardsPostings;
        }

        [HttpGet("{cardId}/{reference}")]
        public async Task<ActionResult<IEnumerable<CardsPostingsDTO>>> GetCardsPostings(int cardId, string reference)
        {
            List<CardsPostingsDTO>? cardsPostings = await _cardPostingService.GetCardsPostings(cardId, reference).ToListAsync();

            return cardsPostings;
        }

        [HttpGet("People/{peopleId}/{reference}")]
        public async Task<ActionResult<IEnumerable<CardsPostings>>> GetCardsPostingsByPeopleId(int peopleId, string reference)
        {
            List<CardsPostings>? cardsPostings = await _cardPostingService.GetCardsPostingsByPeopleId(peopleId, reference).ToListAsync();

            return cardsPostings;
        }

        [HttpGet("People")]
        public async Task<ActionResult<IEnumerable<CardsPostingsPeople>>> GetCardsPostingsPeople(int cardId, string reference)
        {
            List<CardsPostingsPeople>? cardsPostingsPeople = await _cardPostingService.GetCardsPostingsPeople(cardId, reference).ToListAsync();

            return cardsPostingsPeople;
        }

        [HttpGet("references")]
        public async Task<ActionResult<IEnumerable<CardsPostingsDTO>>> GetCardsPostingsByReferences(string initialReference, string finalReference, int categoryId, bool others)
        {
            List<CardsPostingsDTO>? cardsPostings = await _cardPostingService.GetCardsPostingsByReferences(initialReference, finalReference, categoryId, others).ToListAsync();

            return Ok(cardsPostings);
        }

        [HttpGet("PeopleById")]
        public async Task<ActionResult<CardsPostingsPeople?>> GetCardsPostingsByPeopleIdAsync(int? peopleId, string reference, int cardId)
        {
            CardsPostingsPeople? cardsPostingPeople = await Task.Run(() =>
            {
                return _cardPostingService.GetCardsPostingsByPeopleId(peopleId, reference, cardId);
            });

            return cardsPostingPeople;
        }

        [HttpPut("SetPositions")]
        public async Task<ActionResult<CardsPostings>> SetPositions(List<CardsPostings> cardsPostings)
        {
            await _cardPostingService.SetPositions(cardsPostings);

            return Ok();
        }

        [HttpPut("ReorderByDate/{cardId}/{reference}")]
        public async Task<IActionResult> ReorderPositionsByDate(int cardId, string reference)
        {
            if (cardId <= 0 ||
                string.IsNullOrWhiteSpace(reference) ||
                !_cardPostingService.ValidateCardAndUser(cardId))
            {
                return BadRequest();
            }

            try
            {
                await _cardPostingService.ReorderPositionsByDate(cardId, reference);

                return Ok();
            }
            catch (Exception ex)
            {
                return Problem($"Erro ao reordenar lançamentos do cartão: {ex.Message}");
            }
        }

        // PUT: api/CardsPostings/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutCardsPostings(int id, CardsPostings cardsPostings, bool repeatToNextMonths = false, bool preserveFutureValues = false, bool allowClosedInvoiceOperation = false)
        {
            if (id != cardsPostings.Id || !_cardPostingService.ValidarUsuario(id))
            {
                return BadRequest();
            }

            try
            {
                await _cardPostingService.PutCardsPostings(cardsPostings, repeatToNextMonths, preserveFutureValues, allowClosedInvoiceOperation);
            }
            catch (DbUpdateConcurrencyException dex)
            {
                if (!_cardPostingService.CardsPostingsExists(id))
                {
                    return NotFound();
                }

                return Problem(dex.Message);
            }
            catch (ClosedInvoiceOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                // Verifica se é erro vindo do ExpenseService
                if (ex.InnerException?.Message?.Contains("ExpenseService") == true ||
                    ex.Message.Contains("ExpenseService"))
                {
                    return Problem($"Erro ao atualizar despesa relacionada: {ex.Message}");
                }

                // Erro genérico ou do CardPostingsService
                return Problem($"Erro ao atualizar lançamento no cartão: {ex.Message}");
            }

            return Ok();
        }

        [HttpPut("AllParcels/{id}")]
        public async Task<IActionResult> PutCardsPostingsWithParcels(int id, CardsPostings cardsPostings, bool repeat, int qtyMonths, bool allowClosedInvoiceOperation = false)
        {
            if (id != cardsPostings.Id || !_cardPostingService.ValidarUsuario(id))
            {
                return BadRequest();
            }

            try
            {
                await _cardPostingService.PutCardsPostingsWithParcels(cardsPostings, repeat, qtyMonths, allowClosedInvoiceOperation);
            }
            catch (ClosedInvoiceOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                // Verifica se é erro vindo do ExpenseService
                if (ex.InnerException?.Message?.Contains("ExpenseService") == true ||
                    ex.Message.Contains("ExpenseService"))
                {
                    return Problem($"Erro ao atualizar despesa relacionada: {ex.Message}");
                }

                // Erro genérico ou do CardPostingsService
                return Problem($"Erro ao atualizar lançamento no cartão: {ex.Message}");
            }

            return Ok();
        }

        // POST: api/CardsPostings
        [HttpPost]
        public async Task<ActionResult<CardsPostingsDTO?>> PostCardsPostings(CardsPostings cardsPostings, bool allowClosedInvoiceOperation = false)
        {
            if (!_cardPostingService.ValidateCardAndUser(cardsPostings.CardId))
            {
                return BadRequest();
            }

            try
            {
                await _cardPostingService.PostCardsPostings(cardsPostings, allowClosedInvoiceOperation);

                return await GetCardsPostings(cardsPostings.Id);
            }
            catch (ClosedInvoiceOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                if (ex.InnerException?.Message?.Contains("ExpenseService") == true ||
                    ex.Message.Contains("ExpenseService"))
                {
                    return Problem($"Erro ao vincular lançamento à despesa: {ex.Message}");
                }

                return Problem($"Erro ao salvar lançamento: {ex.Message}");
            }
        }

        [HttpPost("FromNotification")]
        public async Task<ActionResult<CardsPostingsDTO?>> PostCardsPostingsFromNotification(CardsPostings cardsPostings, bool allowClosedInvoiceOperation = false)
        {
            if (!_cardPostingService.ValidateCardAndUser(cardsPostings.CardId))
            {
                return BadRequest();
            }

            try
            {
                await _cardPostingService.PostCardsPostingsFromNotification(cardsPostings, allowClosedInvoiceOperation);

                return await GetCardsPostings(cardsPostings.Id);
            }
            catch (ClosedInvoiceOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                if (ex.InnerException?.Message?.Contains("ExpenseService") == true ||
                    ex.Message.Contains("ExpenseService"))
                {
                    return Problem($"Erro ao vincular lançamento à despesa: {ex.Message}");
                }

                return Problem($"Erro ao transformar notificação em lançamento: {ex.Message}");
            }
        }

        [HttpPost("FromNotification/AllParcels")]
        public async Task<ActionResult<CardsPostingsDTO?>> PostCardsPostingsWithParcelsFromNotification(CardsPostings cardsPostings, bool repeat, int qtyMonths, bool allowClosedInvoiceOperation = false)
        {
            if (!_cardPostingService.ValidateCardAndUser(cardsPostings.CardId))
            {
                return BadRequest();
            }

            try
            {
                await _cardPostingService.PostCardsPostingsWithParcelsFromNotification(cardsPostings, repeat, qtyMonths, allowClosedInvoiceOperation);

                return await GetCardsPostings(cardsPostings.Id);
            }
            catch (ClosedInvoiceOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                if (ex.InnerException?.Message?.Contains("ExpenseService") == true ||
                    ex.Message.Contains("ExpenseService"))
                {
                    return Problem($"Erro ao vincular lançamento parcelado à despesa: {ex.Message}");
                }

                return Problem($"Erro ao transformar notificação parcelada em lançamento: {ex.Message}");
            }
        }

        [HttpPost("AllParcels")]
        public async Task<ActionResult<CardsPostingsDTO?>> PostCardsPostingsWithParcels(CardsPostings cardsPostings, bool repeat, int qtyMonths, bool allowClosedInvoiceOperation = false)
        {
            if (!_cardPostingService.ValidateCardAndUser(cardsPostings.CardId))
            {
                return BadRequest();
            }

            try
            {
                await _cardPostingService.PostCardsPostingsWithParcels(cardsPostings, repeat, qtyMonths, allowClosedInvoiceOperation);

                return await GetCardsPostings(cardsPostings.Id);
            }
            catch (ClosedInvoiceOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                if (ex.InnerException?.Message?.Contains("ExpenseService") == true ||
                    ex.Message.Contains("ExpenseService"))
                {
                    return Problem($"Erro ao vincular lançamento parcelado à despesa: {ex.Message}");
                }

                return Problem($"Erro ao salvar lançamento parcelado: {ex.Message}");
            }
        }

        // DELETE: api/CardsPostings/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCardsPostings(int id, bool allowClosedInvoiceOperation = false)
        {
            CardsPostings? cardPosting = await _cardPostingService.GetCardsPostings(id).FirstOrDefaultAsync();

            if (cardPosting == null || !_cardPostingService.ValidarUsuario(cardPosting.Id))
            {
                return BadRequest();
            }

            try
            {
                await _cardPostingService.DeleteCardsPostings(cardPosting, allowClosedInvoiceOperation);
            }
            catch (ClosedInvoiceOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                if (ex.InnerException?.Message?.Contains("ExpenseService") == true ||
                    ex.Message.Contains("ExpenseService"))
                {
                    return Problem($"Erro ao estornar valor da despesa vinculada: {ex.Message}");
                }

                return Problem($"Erro ao excluir lançamento: {ex.Message}");
            }

            return Ok();
        }

        [HttpPost("{id}/ConvertToExpense")]
        public async Task<ActionResult<Expenses>> ConvertToExpense(
            int id,
            bool allowClosedInvoiceOperation = false)
        {
            try
            {
                Expenses expense = await _cardPostingService.ConvertToExpenseAsync(
                    id,
                    allowClosedInvoiceOperation);

                return Ok(expense);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ClosedInvoiceOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        }
    }
}
