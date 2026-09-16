using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Services;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace PoliceChasePlugin;

public sealed class PoliceChaseService : CriticalBackgroundService, IAssettoServerAutostart
{
    private readonly PoliceChaseConfiguration _configuration;

    public PoliceChaseService(
        PoliceChaseConfiguration configuration,
        IHostApplicationLifetime applicationLifetime) : base(applicationLifetime)
    {
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.Enabled)
        {
            Log.Information("[PoliceChase] Plugin disabled by configuration");
            return;
        }

        Log.Information("[PoliceChase] Plugin initialized");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Log.Information("[PoliceChase] Plugin stopping");
        }
    }
}
