using BlueskyFeed.Config;
using FishyFlip;
using FishyFlip.Models;

namespace BlueskyFeed.Services;

public class ProtoHandler : IDisposable
{
    private readonly ILogger<ProtoHandler> _logger;
    private ATProtocol? _proto;
    private Session? _session;

    public ProtoHandler(ILogger<ProtoHandler> logger)
    {
        _logger = logger;
    }
    
    private async Task Setup(AtProtoConfig config, CancellationToken cancellationToken = default)
    {
        if (_proto != null)
        {
            return;
        }
        
        _logger.LogInformation("Starting ATProtocolBuilder");
        var builder = new ATProtocolBuilder()
            .WithLogger(_logger)
            .EnableAutoRenewSession(true)
            // TODO: Service uri
            .Build();

        var session =  await builder.AuthenticateWithPasswordAsync(config.LoginIdentifier, config.LoginToken, cancellationToken);
        if (session == null)
        {
            throw new Exception("Failed to authenticate");
        }

        _logger.LogInformation("Did: {Did}, Doc: {Doc}", session.Did, session.DidDoc);

        _proto = builder;
        _session = session;
    }
    
    public async Task<ATProtocol> GetProtocolAsync(AtProtoConfig config, CancellationToken cancellationToken = default)
    {
        await Setup(config, cancellationToken);
        return _proto!;
    }
    
    public async Task<Session> GetSessionAsync(AtProtoConfig config, CancellationToken cancellationToken = default)
    {
        await Setup(config, cancellationToken);
        return _session!;
    }

    public void Dispose()
    {
        _proto?.Dispose();
        _logger.LogInformation("Disposed");
    }
}