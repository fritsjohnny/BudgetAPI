namespace BudgetAPI.Services
{
    public class KeepAliveService : IHostedService, IDisposable
    {
        private readonly ILogger<KeepAliveService> _logger;
        private Timer? _timer; 
        private readonly HttpClient _httpClient;
        private readonly string _pingUrl;

        public KeepAliveService(ILogger<KeepAliveService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _httpClient = new HttpClient();

            _pingUrl = configuration["KeepAlive:PingUrl"] ?? string.Empty;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("KeepAliveService iniciado.");

            // Executa agora e depois a cada 2 minutos
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(2));

            return Task.CompletedTask;
        }

        private async void DoWork(object? state) 
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_pingUrl))
                {
                    _logger.LogError("URL para ping não encontrada!");
                    return; 
                }

                var response = await _httpClient.GetAsync(_pingUrl);
                _logger.LogInformation("Ping enviado - status: {StatusCode}", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enviar ping para a API");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("KeepAliveService finalizado.");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _httpClient?.Dispose();
        }
    }
}
