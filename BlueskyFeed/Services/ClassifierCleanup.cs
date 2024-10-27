using BlueskyFeed.Classifiers;

namespace BlueskyFeed.Services;

public class ClassifierCleanup : IHostedService, IDisposable
{
    private readonly ILogger<ClassifierCleanup> _logger;
    private readonly IEnumerable<IClassifier> _classifiers;
    private Timer? _timer;

    public ClassifierCleanup(ILogger<ClassifierCleanup> logger, IEnumerable<IClassifier> classifiers)
    {
        _logger = logger;
        _classifiers = classifiers;
    }
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }
    
    private void DoWork(object? state)
    {
        foreach (var classifier in _classifiers)
        {
            classifier.Cleanup();
        }
    }
    
    public void Dispose()
    {
        _timer?.Dispose();
    }
}