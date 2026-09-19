using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Services;
using Microsoft.Extensions.Hosting;
using PoliceChasePlugin.Ai;
using PoliceChasePlugin.Players;
using PoliceChasePlugin.Pursuit;
using Serilog;

namespace PoliceChasePlugin;

public sealed class PoliceChaseService : CriticalBackgroundService, IAssettoServerAutostart
{
    private readonly PoliceChaseConfiguration _configuration;
    private readonly PoliceAiService _policeAiService;
    private readonly PoliceTargetService _targetService;
    private readonly IPolicePursuitService _pursuitService;

    public PoliceChaseService(
        PoliceChaseConfiguration configuration,
        PoliceAiService policeAiService,
        PoliceTargetService targetService,
        IPolicePursuitService pursuitService,
        IHostApplicationLifetime applicationLifetime) : base(applicationLifetime)
    {
        _configuration = configuration;
        _policeAiService = policeAiService;
        _targetService = targetService;
        _pursuitService = pursuitService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.Enabled)
        {
            Log.Information("[PoliceChase] Plugin disabled by configuration");
            return;
        }

        if (_configuration.PursuitLaneChangeEnabled)
        {
            Log.Information(
                "[PoliceChase] Route-aware lane changing enabled: lookahead {LookaheadMeters:0.0} m, transition {TransitionMeters:0.0} m, cooldown {CooldownMilliseconds} ms",
                _configuration.PursuitLaneChangeLookaheadMeters,
                _configuration.PursuitLaneChangeDistanceMeters,
                _configuration.PursuitLaneChangeCooldownMilliseconds);
        }
        else
        {
            Log.Information(
                "[PoliceChase] Route-aware lane changing disabled by configuration");
        }

        _policeAiService.Prepare();
        _targetService.Start();
        Log.Information("[PoliceChase] Plugin initialized");

        try
        {
            await _pursuitService.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        finally
        {
            _pursuitService.Release();
            _targetService.Stop();
            Log.Information("[PoliceChase] Plugin stopping");
        }
    }
}
