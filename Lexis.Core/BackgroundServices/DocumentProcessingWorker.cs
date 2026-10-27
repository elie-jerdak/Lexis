using Lexis.Core.Helpers;
using Lexis.Core.Interfaces; 
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lexis.Core.BackgroundServices;

public sealed class DocumentProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentProcessingWorker> _logger;
    private readonly DocumentProcessingOptions _options;

    public DocumentProcessingWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<DocumentProcessingOptions> options,
        ILogger<DocumentProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Document processing worker starting with concurrency {Concurrency}.",
            _options.MaxConcurrency);

        var workers = Enumerable
            .Range(1, _options.MaxConcurrency)
            .Select(workerId =>
                RunWorkerAsync(workerId, stoppingToken))
            .ToArray();

        await Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(
        int workerId,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope =
                    _scopeFactory.CreateAsyncScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<IDocumentProcessingService>();

                await service.ProcessPendingDocumentsAsync(
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Document processing worker {WorkerId} encountered an error.",
                    workerId);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.IdleDelaySeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation(
            "Document processing worker {WorkerId} stopped.",
            workerId);
    }
}