using System.Net;
using System.Net.Mail;
using System.Text;

namespace BudgetAPI.Services
{
    public class DailyNotificationHostedService : IHostedService, IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DailyNotificationHostedService> _logger;
        private Timer? _timer;
        private bool _jaExecutouInicial = false;
        private readonly StringBuilder _logBuilder = new();


        public DailyNotificationHostedService(
            IConfiguration configuration, 
            IServiceProvider serviceProvider,
            ILogger<DailyNotificationHostedService> logger)
        {
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🧪 [DEBUG] HostedService START - {0}", DateTime.Now);
            _logBuilder.AppendLine($"🟢 Serviço iniciado às {DateTime.UtcNow:dd/MM/yyyy HH:mm:ss} UTC");

            await SendDebugEmail("🟢 HostedService", _logBuilder.ToString());

            ScheduleNextExecution();
        }

        private void ScheduleNextExecution()
        {
            var now = DateTime.Now;
            _logger.LogInformation("🧪 [DEBUG] Entrou em ScheduleNextExecution às {0}", now);
            _logBuilder.AppendLine($"⏰ Agendamento iniciado às {now:dd/MM/yyyy HH:mm:ss}");

            // Executar imediatamente na primeira entrada
            if (!_jaExecutouInicial)
            {
                _logger.LogInformation("🚀 Primeira execução imediata às {0}", now);
                _logBuilder.AppendLine($"🚀 Primeira execução imediata às {now:dd/MM/yyyy HH:mm:ss}");

                _ = ExecuteTaskAsync(_jaExecutouInicial);
                _jaExecutouInicial = true;
            }

            // Próxima hora cheia
            DateTime proximaHoraCheia = now.AddMinutes(60 - now.Minute)
                                           .AddSeconds(-now.Second)
                                           .AddMilliseconds(-now.Millisecond);

            TimeSpan delay = proximaHoraCheia - now;

            _timer = new Timer(_ =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteTaskAsync(_jaExecutouInicial);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Erro fatal no Timer do HostedService");
                        _logBuilder.AppendLine($"❌ Erro fatal no Timer: {ex.Message}");
                    }
                    finally
                    {
                        ScheduleNextExecution();
                    }
                });
            }, null, delay, Timeout.InfiniteTimeSpan);

            _logger.LogInformation("⏰ Próxima execução agendada para {0}", proximaHoraCheia);
            _logBuilder.AppendLine($"📅 Próxima execução agendada para {proximaHoraCheia:dd/MM/yyyy HH:mm:ss}");
        }

        private async Task ExecuteTaskAsync(bool jaExecutouInicial)
        {
            _logBuilder.AppendLine("");
            _logBuilder.AppendLine($"[INÍCIO HostedService] {DateTime.UtcNow:dd/MM/yyyy HH:mm:ss} UTC");
            _logBuilder.AppendLine($"➡️ Execução {(jaExecutouInicial ? "agendada" : "inicial")}");

            _logger.LogInformation(
                "🚀 Executando tarefa de notificação global ({Tipo})",
                jaExecutouInicial ? "AGENDADA" : "INICIAL"
            );

            // 🔥 OTIMIZAÇÃO CRÍTICA
            if (!jaExecutouInicial)
            {
                _logBuilder.AppendLine("⏩ Execução inicial ignorada para evitar acesso ao banco.");
                _logger.LogInformation("⏩ Execução inicial ignorada (warm-up apenas).");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var jobService = scope.ServiceProvider.GetRequiredService<INotificationJobService>();

            try
            {
                await jobService.EnviarNotificacoesGlobaisAsync(true);
                _logBuilder.AppendLine("✅ Execução da tarefa concluída com sucesso.");
            }
            catch (Exception ex)
            {
                _logBuilder.AppendLine($"❌ Erro ao executar tarefa: {ex.Message}");
                _logger.LogError(ex, "❌ Erro ao executar notificação de background");
            }

            _logBuilder.AppendLine($"[FIM HostedService] {DateTime.UtcNow:dd/MM/yyyy HH:mm:ss} UTC");
            _logBuilder.Clear();
        }

        private async Task SendDebugEmail(string subject, string body)
        {
            try
            {
                using var message = new MailMessage();

                string smtpUser  = _configuration["BUDGETAPI_SMTP_USER"] ?? "";
                string smtpPass  = _configuration["BUDGETAPI_SMTP_PASS"] ?? "";
                string emailFrom = _configuration["BUDGETAPI_EMAIL_FROM"] ?? "";
                string emailTo   = _configuration["BUDGETAPI_EMAIL_TO"] ?? "";

                if (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
                {
                    _logger.LogWarning("⚠️ SMTP não configurado (BUDGETAPI_SMTP_USER/BUDGETAPI_SMTP_PASS). E-mail não será enviado.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(emailFrom) || string.IsNullOrWhiteSpace(emailTo))
                {
                    _logger.LogWarning("⚠️ E-mail não configurado (BUDGETAPI_EMAIL_FROM/BUDGETAPI_EMAIL_TO). E-mail não será enviado.");
                    return;
                }

                message.From = new MailAddress(emailFrom);
                message.To.Add(emailTo);
                message.Subject = subject;
                message.Body = body;
                message.IsBodyHtml = false;

                using var client = new SmtpClient("smtp.gmail.com", 587)
                {
                    Credentials = new NetworkCredential(smtpUser, smtpPass),
                    EnableSsl = true
                };

                await client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro ao enviar e-mail de log (HostedService)");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            var utcNow = DateTime.UtcNow;
            var localNow = DateTime.Now;

            _logger.LogInformation("🛑 Serviço de notificação diária finalizado.");
            _logBuilder.AppendLine("");
            _logBuilder.AppendLine("🛑 StopAsync chamado - Encerrando HostedService");
            _logBuilder.AppendLine($"📅 UTC:    {utcNow:dd/MM/yyyy HH:mm:ss}");
            _logBuilder.AppendLine($"🕘 Local:  {localNow:dd/MM/yyyy HH:mm:ss}");
            _logBuilder.AppendLine($"🖥️ Ambiente: {(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "não definido")}");
            _logBuilder.AppendLine($"🧠 GC Memory: {GC.GetTotalMemory(forceFullCollection: false):N0} bytes");
            _logBuilder.AppendLine($"📦 Working Set: {Environment.WorkingSet:N0} bytes");
            _logBuilder.AppendLine($"🔁 Executou inicial: {_jaExecutouInicial}");

            try
            {
                await SendDebugEmail("📋 Log de parada do HostedService", _logBuilder.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro ao enviar e-mail de parada");
            }

            _logBuilder.Clear();
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
