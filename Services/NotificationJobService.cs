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
        private readonly BudgetContext _context;
        private readonly FirebaseNotificationService _firebase;
        private readonly ILogger<NotificationJobService> _logger;

        public NotificationJobService(
            BudgetContext context,
            FirebaseNotificationService firebase,
            ILogger<NotificationJobService> logger)
        {
            _context = context;
            _firebase = firebase;
            _logger = logger;
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
                        6 => NotificacaoTipo.Todas,
                        12 or 18 => NotificacaoTipo.Parciais,
                        _ => NotificacaoTipo.Nenhuma
                    };

                    logBuilder.AppendLine($"📌 Tipo de notificação definida: {tipo}");

                    if (tipo == NotificacaoTipo.Nenhuma && jaExecutouInicial)
                    {
                        logBuilder.AppendLine($"⏩ Ignorado: {user.Name} ({horaLocal}h local)");
                        totalIgnorados++;

                        _logger.LogDebug("⏩ Ignorando usuário {User} - hora local {Hour}", user.Name, horaLocal);
                        continue; // só envia notificações às 6h, 12h, 18h locais
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

                    foreach (Expenses e in expenses)
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

                        string body = $"🗃️ {referenceFormatted}\n" +
                                      $"🧾 {e.Description}\n" +
                                      $"💸 {e.ToPay.ToString("C", culture)}\n" +
                                      $"🗓️ {dueDate:dd/MM/yyyy}";

                        // Gera uma tag única por despesa
                        string tag = $"despesa-{e.Id}";

                        bool result = await _firebase.SendPushAsync(user.FcmToken!, title, body, tag);

                        await Task.Delay(100);

                        if (result)
                        {
                            totalEnviados++;
                            logBuilder.AppendLine($"✅ Enviado: {user.Name} - {title}");
                            _logger.LogDebug("📤 Push enviado: {Title} para {User}", title, user.Name);
                        }
                        else
                        {
                            totalFalhas++;
                            logBuilder.AppendLine($"⚠️ Falha: {user.Name} - {title}");
                            _logger.LogDebug("⚠️ Falha ao enviar para {User}", user.Name);
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
                    await SendDebugEmail("❌ Log de Execução - Exception", logBuilder.ToString());
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
                _logger.LogError(ex, "❌ Erro ao enviar e-mail de log");
            }
        }
    }
}
