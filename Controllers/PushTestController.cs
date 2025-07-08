using BudgetAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PushTestController : ControllerBase
    {
        private readonly FirebaseNotificationService _firebase;
        private readonly IExpenseService _expenseService;

        public PushTestController(FirebaseNotificationService firebase, IExpenseService expenseService)
        {
            _firebase = firebase;
            _expenseService = expenseService;
        }

        [HttpPost]
        public async Task<IActionResult> SendPush([FromBody] string token)
        {
            var success = await _firebase.SendPushAsync(token, "Teste Budget", "Essa é uma notificação de teste");
            
            return success ? Ok("Push enviado com sucesso!") : StatusCode(500, "Falha ao enviar push");
        }

        [HttpPost("check-due")]
        public async Task<IActionResult> CheckDue()
        {
            await _expenseService.SendUpcomingOrOverdueNotificationsAsync();
            return Ok("Notificações enviadas.");
        }
    }
}
