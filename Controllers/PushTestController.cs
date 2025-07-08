using BudgetAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PushTestController : ControllerBase
    {
        private readonly FirebaseNotificationService _firebase;
        private readonly INotificationJobService _notificationJobService;

        public PushTestController(FirebaseNotificationService firebase, INotificationJobService notificationJobService)
        {
            _firebase = firebase;
            _notificationJobService = notificationJobService;
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
            await _notificationJobService.EnviarNotificacoesGlobaisAsync(false);
            return Ok("Notificações enviadas.");
        }
    }
}
