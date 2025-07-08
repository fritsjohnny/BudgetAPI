using System.Globalization;
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

                    // Determina tipo de notificação com base na hora local
                    NotificacaoTipo tipo = horaLocal switch
                    {
                        6 => NotificacaoTipo.Todas,
                        12 or 18 => NotificacaoTipo.Parciais,
                        _ => NotificacaoTipo.Nenhuma
                    };

                    if (tipo == NotificacaoTipo.Nenhuma && jaExecutouInicial)
                    {
                        _logger.LogDebug("⏩ Ignorando usuário {User} - hora local {Hour}", user.Name, horaLocal);
                        continue; // só envia notificações às 6h, 12h, 18h locais
                    }

                    IQueryable<Expenses> query = _context.Expenses.Where(e => e.UserId == user.Id &&
                                                                              e.DueDate != null &&
                                                                              e.Paid != e.ToPay);
                                                                                    

                    if (tipo == NotificacaoTipo.Parciais)
                    {
                        query = query.Where(e => e.DueDate!.Value.Date <= today);
                    }
                    else // tipo == Todas
                    {
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

                        string body =
                    $"🧾 {e.Description}\n" +
                    $"💸 {e.ToPay.ToString("C", culture)}\n" +
                    $"🗓️ {dueDate:dd/MM/yyyy}";

                        bool result = await _firebase.SendPushAsync(user.FcmToken!, title, body);

                        if (result)
                            _logger.LogDebug("📤 Push enviado: {Title} para {User}", title, user.Name);
                        else
                            _logger.LogDebug("⚠️ Falha ao enviar para {User}", user.Name);
                    }
                }
                catch (TimeZoneNotFoundException)
                {
                    _logger.LogDebug("❌ Timezone inválido para usuário {0}: {1}", user.Id, user.TimezoneId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Erro ao processar usuário {0}", user.Name);
                }
            }
        }
    }
}
