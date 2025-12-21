namespace BudgetAPI.Services
{
    public class KeepAliveService : IHostedService, IDisposable
    {
        private readonly ILogger<KeepAliveService> _logger;
        private Timer? _timer; 
        private readonly HttpClient _httpClient;
        //private readonly string _pingUrl = "https://budgetapimanagementservice.azure-api.net/api/health";
        private readonly string _pingUrl = "https://budgetappapi-e2dhfhgpgwctgueq.brazilsouth-01.azurewebsites.net/api/health";

        public KeepAliveService(ILogger<KeepAliveService> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
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
