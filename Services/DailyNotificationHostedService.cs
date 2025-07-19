using System.Net;
using System.Net.Mail;
using System.Text;

namespace BudgetAPI.Services
{
    public class DailyNotificationHostedService : IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DailyNotificationHostedService> _logger;
        private Timer? _timer;
        private bool _jaExecutouInicial = false;

        public DailyNotificationHostedService(
            IServiceProvider serviceProvider,
            ILogger<DailyNotificationHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🧪 [DEBUG] HostedService START - {0}", DateTime.Now);
            _logger.LogInformation("⏰ Serviço de notificação diária iniciado.");
            ScheduleNextExecution();
            return Task.CompletedTask;
        }

        private void ScheduleNextExecution()
        {
            _logger.LogInformation("🧪 [DEBUG] Entrou em ScheduleNextExecution às {0}", DateTime.Now);

            var now = DateTime.Now;

            // Executar imediatamente na primeira entrada
            if (!_jaExecutouInicial)
            {
                _logger.LogInformation("🚀 Primeira execução imediata às {0}", now);
                _ = ExecuteTaskAsync(_jaExecutouInicial);
                _jaExecutouInicial = true;
            }

            // Próxima hora cheia (ex: se agora é 10:15, será 11:00)
            var proximaHoraCheia = now.AddHours(1).Date.AddHours(now.Hour + 1);
            var delay = proximaHoraCheia - now;

            _timer = new Timer(async _ =>
            {
                await ExecuteTaskAsync(_jaExecutouInicial);
                ScheduleNextExecution(); // Agendar a próxima execução
            }, null, delay, Timeout.InfiniteTimeSpan);

            _logger.LogInformation("⏰ Próxima execução agendada para {0}", proximaHoraCheia);
        }

        private async Task ExecuteTaskAsync(bool jaExecutouInicial)
        {
            var logBuilder = new StringBuilder();

            logBuilder.AppendLine($"[INÍCIO HostedService] {DateTime.UtcNow:dd/MM/yyyy HH:mm:ss} UTC");
            logBuilder.AppendLine($"➡️ Execução {(jaExecutouInicial ? "agendada" : "imediata")}");

            _logger.LogError("🚀 Executando tarefa de notificação global em {0}", DateTime.Now);

            using var scope = _serviceProvider.CreateScope();
            var jobService = scope.ServiceProvider.GetRequiredService<INotificationJobService>();

            try
            {
                await jobService.EnviarNotificacoesGlobaisAsync(jaExecutouInicial);
                logBuilder.AppendLine("✅ Execução da tarefa concluída com sucesso.");
            }
            catch (Exception ex)
            {
                logBuilder.AppendLine($"❌ Erro ao executar tarefa: {ex.Message}");
                _logger.LogError(ex, "❌ Erro ao executar notificação de background");
            }

            logBuilder.AppendLine($"[FIM HostedService] {DateTime.UtcNow:dd/MM/yyyy HH:mm:ss} UTC");

            //if (!Debugger.IsAttached)
            {
                await SendDebugEmail("📋 Log - DailyNotificationHostedService", logBuilder.ToString());
            }
        }

        private async Task SendDebugEmail(string subject, string body)
        {
            try
            {
                var message = new MailMessage();
                message.From = new MailAddress("frits.johnny@gmail.com");
                message.To.Add("johnny.frits@outlook.com");
                message.Subject = subject;
                message.Body = body;
                message.IsBodyHtml = false;

                using var client = new SmtpClient("smtp.gmail.com", 587)
                {
                    Credentials = new NetworkCredential("frits.johnny@gmail.com", "jyovlyendozbwhyi"),
                    EnableSsl = true
                };

                await client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro ao enviar e-mail de log (HostedService)");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🛑 Serviço de notificação diária finalizado.");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
