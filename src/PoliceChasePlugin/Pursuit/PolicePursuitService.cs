using PoliceChasePlugin.Ai;
using PoliceChasePlugin.Players;
using Serilog;

namespace PoliceChasePlugin.Pursuit;

public interface IPolicePursuitService
{
    Task RunAsync(CancellationToken stoppingToken);
    void Release();
}

public sealed class PolicePursuitService : IPolicePursuitService
{
    private readonly PoliceChaseConfiguration _configuration;
    private readonly PoliceAiService _policeAiService;
    private readonly PoliceTargetService _targetService;

    private byte? _activeTargetSessionId;
    private bool _routeTemporarilyLost;

    public PolicePursuitService(
        PoliceChaseConfiguration configuration,
        PoliceAiService policeAiService,
        PoliceTargetService targetService)
    {
        _configuration = configuration;
        _policeAiService = policeAiService;
        _targetService = targetService;
    }

    public void UpdateOnce()
    {
        var state = _policeAiService.SelectedState;
        var targetSessionId = _targetService.CurrentTargetSessionId;

        if (state == null || !state.IsInitialized)
            return;

        if (!targetSessionId.HasValue)
        {
            ReleaseInternal("target-disconnected");
            return;
        }

        if (_activeTargetSessionId.HasValue
            && _activeTargetSessionId.Value != targetSessionId.Value)
        {
            ReleaseInternal("target-changed");
        }

        var result = state.TrackPursuit(
            targetSessionId.Value,
            _configuration.PursuitMaxDistanceMeters);

        switch (result.Status)
        {
            case PolicePursuitTrackingStatus.Active:
                HandleActive(state, targetSessionId.Value, result);
                break;
            case PolicePursuitTrackingStatus.RouteTemporarilyUnavailable:
                HandleTemporaryRouteLoss(targetSessionId.Value);
                break;
            case PolicePursuitTrackingStatus.WaitingForSpawn:
                break;
            case PolicePursuitTrackingStatus.MaxDistanceExceeded:
                ReleaseInternal("max-distance-exceeded");
                break;
            case PolicePursuitTrackingStatus.NoRoute:
                ReleaseInternal("no-route");
                break;
            case PolicePursuitTrackingStatus.TargetUnavailable:
                ReleaseInternal("target-unavailable");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result.Status), result.Status, null);
        }
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(
            _configuration.PursuitUpdateIntervalMilliseconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            UpdateOnce();
        }
    }

    public void Release()
    {
        ReleaseInternal("service-stopping");
    }

    private void HandleActive(
        IPoliceAiState state,
        byte targetSessionId,
        PolicePursuitTrackingResult result)
    {
        if (!result.RouteDistanceMeters.HasValue)
            throw new InvalidOperationException("Active pursuit requires a route distance");

        var starting = !_activeTargetSessionId.HasValue;
        _activeTargetSessionId = targetSessionId;

        if (starting)
        {
            Log.Information(
                "[PoliceChase] Pursuit started: police {PoliceSessionId}, target {TargetSessionId}",
                _policeAiService.SelectedSlot?.SessionId,
                targetSessionId);
        }
        else if (_routeTemporarilyLost)
        {
            Log.Information(
                "[PoliceChase] Pursuit route recovered: target {TargetSessionId}",
                targetSessionId);
        }

        _routeTemporarilyLost = false;
        var desiredSpeed = PolicePursuitSpeedPolicy.Calculate(
            result.TargetSpeedMetersPerSecond,
            result.RouteDistanceMeters.Value,
            _configuration.PursuitDesiredDistanceMeters,
            _configuration.PursuitMaxSpeedKph);
        state.SetDesiredSpeed(desiredSpeed);
    }

    private void HandleTemporaryRouteLoss(byte targetSessionId)
    {
        if (!_activeTargetSessionId.HasValue)
            return;

        if (!_routeTemporarilyLost)
        {
            Log.Information(
                "[PoliceChase] Pursuit route temporarily lost: target {TargetSessionId}",
                targetSessionId);
        }

        _routeTemporarilyLost = true;
    }

    private void ReleaseInternal(string reason)
    {
        if (!_activeTargetSessionId.HasValue)
            return;

        var targetSessionId = _activeTargetSessionId.Value;
        _policeAiService.SelectedState?.ReleasePursuit();
        _activeTargetSessionId = null;
        _routeTemporarilyLost = false;
        Log.Information(
            "[PoliceChase] Pursuit ended: target {TargetSessionId}, reason {Reason}",
            targetSessionId,
            reason);
    }
}
