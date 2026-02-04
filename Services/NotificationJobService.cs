using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using BudgetAPI.Data;
using BudgetAPI.Models;
using Microsoft.EntityFrameworkCore;


namespace BudgetAPI.Services
{
    public enum NotificacaoTipo
    {
        Nenhuma,
        Todas,
        Parciais
    }

    public interface INotificationJobService
    {
        Task EnviarNotificacoesGlobaisAsync(bool jaExecutouInicial);
    }

    public class NotificationJobService : INotificationJobService
    {
        private readonly IConfiguration _configuration;
        private readonly BudgetContext _context;
        private readonly FirebaseNotificationService _firebase;
        private readonly ILogger<NotificationJobService> _logger;

        public NotificationJobService(
            IConfiguration configuration, 
            BudgetContext context,
            FirebaseNotificationService firebase,
            ILogger<NotificationJobService> logger)
        {
            _configuration = configuration;
            _context       = context;
            _firebase      = firebase;
            _logger        = logger;
        }

        public async Task EnviarNotificacoesGlobaisAsync(bool jaExecutouInicial)
        {
            var logBuilder = new StringBuilder();

            logBuilder.AppendLine($"[INÍCIO] Execução às {DateTime.UtcNow:dd/MM/yyyy HH:mm:ss} UTC");

            int totalEnviados  = 0;
            int totalFalhas    = 0;
            int totalIgnorados = 0;


            var culture = new CultureInfo("pt-BR");

            List<Users> users = await _context.Users.Where(u => !string.IsNullOrEmpty(u.FcmToken) && !string.IsNullOrEmpty(u.TimezoneId))
                                                    .ToListAsync();

            foreach (Users? user in users)
            {
                try
                {
                    TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(user.TimezoneId!);
                    DateTime userNow      = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
                    int horaLocal         = userNow.Hour;
                    DateTime today        = userNow.Date;
                    DateTime maxDate      = today.AddDays(3);

                    // Log detalhado da data e hora local do usuário
                    logBuilder.AppendLine($"🕒 Usuário: {user.Name}");
                    logBuilder.AppendLine($"🌐 TimezoneId: {user.TimezoneId}");
                    logBuilder.AppendLine($"🗓️ Data local: {today:dd/MM/yyyy}");
                    logBuilder.AppendLine($"🕘 Hora local: {horaLocal}");
                    logBuilder.AppendLine($"🔜 Considerando despesas até: {maxDate:dd/MM/yyyy}");

                    // Determina tipo de notificação com base na hora local
                    NotificacaoTipo tipo = horaLocal switch
                    {
                        8 => NotificacaoTipo.Todas,
                        12 or 18 => NotificacaoTipo.Parciais,
                        _ => NotificacaoTipo.Nenhuma
                    };

                    logBuilder.AppendLine($"📌 Tipo de notificação definida: {tipo}");

                    if (tipo == NotificacaoTipo.Nenhuma && jaExecutouInicial)
                    {
                        logBuilder.AppendLine($"⏩ Ignorado: {user.Name} ({horaLocal}h local)");
                        totalIgnorados++;

                        _logger.LogDebug("⏩ Ignorando usuário {User} - hora local {Hour}", user.Name, horaLocal);
                        continue; // só envia notificações às 8h, 12h, 18h locais
                    }

                    IQueryable<Expenses> query = _context.Expenses.Where(e => e.UserId == user.Id &&
                                                                              e.DueDate != null &&
                                                                              e.Paid != e.ToPay);

                    if (tipo == NotificacaoTipo.Parciais)
                    {
                        logBuilder.AppendLine($"🔍 Filtro aplicado: PARCIAIS — despesas com DueDate até {today:dd/MM/yyyy}");
                        query = query.Where(e => e.DueDate!.Value.Date <= today);
                    }
                    else // tipo == Todas
                    {
                        logBuilder.AppendLine($"🔍 Filtro aplicado: TODAS — despesas com DueDate até {maxDate:dd/MM/yyyy}");
                        query = query.Where(e => e.DueDate!.Value.Date <= maxDate);
                    }

                    List<Expenses> expenses = await query.OrderBy(e => e.DueDate)
                                                         .ToListAsync();

                    logBuilder.AppendLine($"📦 Total de despesas para {user.Name}: {expenses.Count}");

                    foreach (Expenses e in expenses)
                    {
                        try
                        {
                            DateTime dueDate = e.DueDate!.Value.Date;
                            int diffDays = (dueDate - today).Days;

                            string title = diffDays switch
                            {
                                < 0 when diffDays == -1 => "Despesa venceu ontem",
                                < 0 => $"Despesa vencida há {Math.Abs(diffDays)} dias",
                                0 => "Despesa vence hoje",
                                > 0 => $"Despesa a vencer em {diffDays} dia{(diffDays > 1 ? "s" : "")}"
                            };

                            string referenceFormatted = DateTime.ParseExact(e.Reference!, "yyyyMM", CultureInfo.InvariantCulture).ToString("MM/yyyy");

                            string body =  $"🗓️ {dueDate:dd/MM/yyyy}\n" +
                      $"🧾 {e.Description}\n" +
                      $"💸 {e.ToPay.ToString("C", culture)}\n" +
                      $"🗃️ { referenceFormatted}";

                            string tag = $"despesa-{e.Id}";

                            bool result = await _firebase.SendPushAsync(user.FcmToken!, title, body, tag);
                            await Task.Delay(100); // Protege contra colapso do FCM

                            if (result)
                            {
                                totalEnviados++;
                                logBuilder.AppendLine($"✅ Enviado: {user.Name} - {title}");
                            }
                            else
                            {
                                totalFalhas++;
                                logBuilder.AppendLine($"⚠️ Falha: {user.Name} - {title}");
                            }
                        }
                        catch (Exception ex)
                        {
                            totalFalhas++;
                            logBuilder.AppendLine($"❌ Erro ao enviar despesa {e.Id} para {user.Name}: {ex.Message}");
                            _logger.LogError(ex, "❌ Erro ao enviar notificação de despesa {Id} para usuário {User}", e.Id, user.Name);
                            await SendDebugEmail("❌ Log de Execução - Exception try do foreach (Expenses e in expenses)", logBuilder.ToString());
                        }
                    }
                }
                catch (TimeZoneNotFoundException)
                {
                    logBuilder.AppendLine($"❌ Timezone inválido: {user.Id} - {user.TimezoneId}");
                    _logger.LogDebug("❌ Timezone inválido para usuário {0}: {1}", user.Id, user.TimezoneId);
                    await SendDebugEmail("❌ Log de Execução - TimeZoneNotFoundException", logBuilder.ToString());

                }
                catch (Exception ex)
                {
                    logBuilder.AppendLine($"❌ Erro ao processar usuário {user.Name}: {ex.Message}");
                    _logger.LogError(ex, "❌ Erro ao processar usuário {0}", user.Name);
                    await SendDebugEmail("❌ Log de Execução - Exception try do foreach (Users? user in users)", logBuilder.ToString());
                }
            }

            logBuilder.AppendLine($"[FIM] Enviados: {totalEnviados}, Falhas: {totalFalhas}, Ignorados: {totalIgnorados}");
            logBuilder.AppendLine($"[FIM] Término às {DateTime.UtcNow:dd/MM/yyyy HH:mm:ss} UTC");

            //if (!Debugger.IsAttached)
            {
                //await SendDebugEmail("📋 Log de Execução - Notificações", logBuilder.ToString());
            }
        }

        private async Task SendDebugEmail(string subject, string body)
        {
            try
            {
                using var message = new MailMessage();

                string emailFrom = _configuration["BUDGETAPI_EMAIL_FROM"] ?? "";
                string emailTo   = _configuration["BUDGETAPI_EMAIL_TO"] ?? "";

                if (string.IsNullOrWhiteSpace(emailFrom) || string.IsNullOrWhiteSpace(emailTo))
                {
                    _logger.LogWarning("⚠️ E-mail não configurado (BUDGETAPI_EMAIL_FROM/BUDGETAPI_EMAIL_TO). Auditoria por e-mail não será enviada.");
                    return;
                }

                message.From = new MailAddress(emailFrom);
                message.To.Add(emailTo);
                message.Subject    = subject;
                message.Body       = body;
                message.IsBodyHtml = false;

                string smtpUser = _configuration["BUDGETAPI_SMTP_USER"] ?? "";
                string smtpPass = _configuration["BUDGETAPI_SMTP_PASS"] ?? "";

                if (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
                {
                    _logger.LogWarning("⚠️ SMTP não configurado (BUDGETAPI_SMTP_USER/BUDGETAPI_SMTP_PASS). E-mail de auditoria não será enviado.");
                    return;
                }

                using var client = new SmtpClient("smtp.gmail.com", 587)
                {
                    Credentials = new NetworkCredential(smtpUser, smtpPass),
                    EnableSsl   = true
                };

                await client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro ao enviar e-mail de log");
            }
        }
    }
}
