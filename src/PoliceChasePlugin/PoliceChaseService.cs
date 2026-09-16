using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Services;
using Microsoft.Extensions.Hosting;
using PoliceChasePlugin.Ai;
using PoliceChasePlugin.Players;
using Serilog;

namespace PoliceChasePlugin;

public sealed class PoliceChaseService : CriticalBackgroundService, IAssettoServerAutostart
{
    private readonly PoliceChaseConfiguration _configuration;
    private readonly PoliceAiService _policeAiService;
    private readonly PoliceTargetService _targetService;

    public PoliceChaseService(
        PoliceChaseConfiguration configuration,
        PoliceAiService policeAiService,
        PoliceTargetService targetService,
        IHostApplicationLifetime applicationLifetime) : base(applicationLifetime)
    {
        _configuration = configuration;
        _policeAiService = policeAiService;
        _targetService = targetService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.Enabled)
        {
            Log.Information("[PoliceChase] Plugin disabled by configuration");
            return;
        }

        _policeAiService.Prepare();
        _targetService.Start();
        Log.Information("[PoliceChase] Plugin initialized");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        finally
        {
            _targetService.Stop();
            Log.Information("[PoliceChase] Plugin stopping");
        }
    }
}
