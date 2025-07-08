using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace BudgetAPI.Services
{
    public class FirebaseNotificationService
    {
        private static bool _initialized = false;
        private readonly ILogger<FirebaseNotificationService> _logger;

        public FirebaseNotificationService(ILogger<FirebaseNotificationService> logger)
        {
            _logger = logger;

            if (!_initialized)
            {
                FirebaseApp.Create(new AppOptions()
                {
                    Credential = GoogleCredential.FromFile("Keys/firebase-adminsdk.json") // ajuste se mudar o nome
                });
                _initialized = true;
            }
        }

        public async Task<bool> SendPushAsync(string fcmToken, string title, string body)
        {
            try
            {
                var message = new Message()
                {
                    Token = fcmToken,
                    Notification = new Notification
                    {
                        Title = title,
                        Body = body
                    }
                };

                string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                _logger.LogInformation("✅ Push enviado: {Response}", response);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro ao enviar push notification");
                return false;
            }
        }
    }
}
