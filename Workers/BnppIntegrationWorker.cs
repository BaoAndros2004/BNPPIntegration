namespace BNPPIntegration.Workers
{
    public sealed class BnppIntegrationWorker : BackgroundService
    {
        private readonly ILogger<BnppIntegrationWorker> _logger;

        public BnppIntegrationWorker(ILogger<BnppIntegrationWorker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BNPP Integration worker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
