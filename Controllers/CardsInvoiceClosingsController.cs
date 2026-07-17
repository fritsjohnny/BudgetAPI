using BudgetAPI.Authorization;
using BudgetAPI.Models;
using BudgetAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetAPI.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CardsInvoiceClosingsController : ControllerBase
    {
        private readonly ICardsInvoiceClosingService _service;

        public CardsInvoiceClosingsController(ICardsInvoiceClosingService service)
        {
            _service = service;
        }

        [HttpPost("Ensure/{cardId}/{reference}")]
        public async Task<ActionResult<CardsInvoiceClosingDTO>> Ensure(int cardId, string reference)
        {
            try
            {
                return Ok(await _service.EnsureAsync(cardId, reference));
            }
            catch (ArgumentException exception)
            {
                return BadRequest(new { message = exception.Message });
            }
            catch (KeyNotFoundException exception)
            {
                return NotFound(new { message = exception.Message });
            }
            catch (CardsInvoiceClosingConflictException exception)
            {
                return Conflict(new { message = exception.Message });
            }
            catch (Exception exception)
            {
                return Problem(exception.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<CardsInvoiceClosingDTO>> Update(int id, UpdateCardsInvoiceClosingDTO request)
        {
            try
            {
                return Ok(await _service.UpdateAsync(id, request.ClosingDate));
            }
            catch (ArgumentException exception)
            {
                return BadRequest(new { message = exception.Message });
            }
            catch (KeyNotFoundException exception)
            {
                return NotFound(new { message = exception.Message });
            }
            catch (Exception exception)
            {
                return Problem(exception.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        }
    }
}
