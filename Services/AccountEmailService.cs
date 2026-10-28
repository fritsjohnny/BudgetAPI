using System.Net;
using System.Net.Mail;

namespace BudgetAPI.Services
{
    public interface IAccountEmailService
    {
        Task SendVerificationAsync(string email, string token, CancellationToken cancellationToken = default);
        Task SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken = default);
    }

    public class AccountEmailService : IAccountEmailService
    {
        private const string FrontendUrl = "https://red-water-046fd9e1e.2.azurestaticapps.net";
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountEmailService> _logger;

        public AccountEmailService(IConfiguration configuration, ILogger<AccountEmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public Task SendVerificationAsync(string email, string token, CancellationToken cancellationToken = default) =>
            SendAsync(email, token, "verify-email", "Confirme seu e-mail do Budget",
                "Use este link para confirmar seu e-mail. O link expira em 24 horas.", cancellationToken);

        public Task SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken = default) =>
            SendAsync(email, token, "reset-password", "Redefinição de senha do Budget",
                "Use este link para escolher uma nova senha. O link expira em 30 minutos. Se você não pediu a redefinição, ignore esta mensagem.", cancellationToken);

        private async Task SendAsync(string email, string token, string route, string subject, string intro, CancellationToken cancellationToken)
        {
            string smtpUser = _configuration["BUDGETAPI_SMTP_USER"] ?? "";
            string smtpPass = _configuration["BUDGETAPI_SMTP_PASS"] ?? "";
            string emailFrom = _configuration["BUDGETAPI_EMAIL_FROM"] ?? "";

            if (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass) ||
                string.IsNullOrWhiteSpace(emailFrom))
            {
                throw new InvalidOperationException("E-mail de conta não configurado. Configure BUDGETAPI_EMAIL_FROM, BUDGETAPI_SMTP_USER e BUDGETAPI_SMTP_PASS.");
            }

            // Angular uses hash routing; keep token and email in the query after the route.
            string link = $"{FrontendUrl}/#/{route}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";

            using var message = new MailMessage
            {
                From = new MailAddress(emailFrom, "Budget"),
                Subject = subject,
                Body = $"{intro}\n\n{link}\n\nSe você não solicitou esta ação, ignore este e-mail.",
                IsBodyHtml = false
            };
            message.To.Add(email);

            using var client = new SmtpClient("smtp.gmail.com", 587)
            {
                Credentials = new NetworkCredential(smtpUser, smtpPass),
                EnableSsl = true
            };

            try
            {
                await client.SendMailAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao enviar e-mail de segurança para {EmailAddress}", MaskEmail(email));
                throw;
            }
        }

        private static string MaskEmail(string email)
        {
            int at = email.IndexOf('@');
            return at <= 1 ? "***" : email[0] + "***" + email[at..];
        }
    }
}
