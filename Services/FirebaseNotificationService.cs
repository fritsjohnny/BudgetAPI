using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace BudgetAPI.Services
{
    public class FirebaseNotificationService
    {
        private static bool _initialized = false;
        private readonly ILogger<FirebaseNotificationService> _logger;

        public FirebaseNotificationService(ILogger<FirebaseNotificationService> logger, IConfiguration configuration)
        {
            _logger = logger;

            if (!_initialized)
            {
                string keyJson = configuration["Firebase:KeyJson"] ?? "";
                string keyPath = configuration["Firebase:KeyFilePath"] ?? "";

                GoogleCredential credential;

                if (!string.IsNullOrWhiteSpace(keyJson))
                {
                    credential = GoogleCredential.FromJson(keyJson);
                }
                else if (!string.IsNullOrWhiteSpace(keyPath))
                {
                    credential = GoogleCredential.FromFile(keyPath);
                }
                else
                {
                    throw new InvalidOperationException("Firebase não configurado. Defina Firebase:KeyJson ou Firebase:KeyFilePath.");
                }

                FirebaseApp.Create(new AppOptions { Credential = credential });

                _initialized = true;
            }
        }

        public async Task<bool> SendPushAsync(string fcmToken, string title, string body, string tag)
        {
            try
            {
                var message = new Message()
                {
                    Token        = fcmToken,
                    Notification = new Notification
                    {
                        Title = title,
                        Body  = body
                    },
                    Android = new AndroidConfig
                    {
                        Notification = new AndroidNotification
                        {
                            Tag = tag
                        }
                    }
                };

                string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                _logger.LogDebug("✅ Push enviado: {Response}", response);
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
