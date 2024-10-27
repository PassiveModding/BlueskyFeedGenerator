using BlueskyFeed.Classifiers;
using BlueskyFeed.Config;
using FishyFlip;
using FishyFlip.Events;
using Microsoft.Extensions.Options;

namespace BlueskyFeed.Services;

public class JetStreamProvider : IHostedService
{
    private ATJetStream? _jetStream;
    private readonly ILogger<JetStreamProvider> _logger;
    private readonly IEnumerable<IClassifier> _classifiers;
    private readonly IOptions<Setup> _setup;

    public JetStreamProvider(ILogger<JetStreamProvider> logger, IEnumerable<IClassifier> classifiers, IOptions<Setup> setup)
    {
        _logger = logger;
        _classifiers = classifiers;
        _setup = setup;
    }

    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_setup.Value.SubscribeToJetStream)
        {
            _logger.LogInformation("JetStreamProvider is disabled");
            return;
        }
        
        _logger.LogInformation("Starting EventProvider");
        _jetStream = new ATJetStreamBuilder()
            .WithLogger(_logger)
            .Build();
        
        _jetStream.OnRecordReceived += HandleReceive;
        
        await _jetStream.ConnectAsync(token: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping EventProvider");
        if (_jetStream != null)
        {
            _jetStream.OnRecordReceived -= HandleReceive;
            _jetStream.Dispose();
        }
        return Task.CompletedTask;
    }

    private void HandleReceive(object? sender, JetStreamATWebSocketRecordEventArgs args)
    {
        foreach (var classifier in _classifiers)
        {
            classifier.Classify(args);
        }
    }
}