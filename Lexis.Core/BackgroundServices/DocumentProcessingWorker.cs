using Lexis.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lexis.Core.BackgroundServices
{
    public class DocumentProcessingWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DocumentProcessingWorker> _logger;


        public DocumentProcessingWorker(IServiceScopeFactory scopeFactory, ILogger<DocumentProcessingWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    var processingService = scope.ServiceProvider.GetRequiredService<IDocumentProcessingService>();


                    await processingService
                        .ProcessPendingDocumentsAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Document processing worker stopping.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Document processing worker failed.");
                }

                await Task.Delay(
                    TimeSpan.FromMinutes(1),
                    stoppingToken);
            }
        }
    }
}
