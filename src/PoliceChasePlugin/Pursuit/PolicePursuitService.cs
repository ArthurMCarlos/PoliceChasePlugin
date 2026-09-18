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
    private readonly PolicePursuitTrackingOptions _trackingOptions;
    private readonly TimeProvider _timeProvider;

    private byte? _activeTargetSessionId;
    private byte? _suspendedTargetSessionId;
    private long _lastNoRouteProbeTimestamp;
    private long? _lastLoggedRouteRevision;
    private long? _lastLoggedLaneChangeRevision;
    private readonly Dictionary<int, bool> _loggedJunctionDecisions = new();
    private bool _routeTemporarilyLost;

    public PolicePursuitService(
        PoliceChaseConfiguration configuration,
        PoliceAiService policeAiService,
        PoliceTargetService targetService,
        TimeProvider? timeProvider = null)
    {
        _configuration = configuration;
        _policeAiService = policeAiService;
        _targetService = targetService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _trackingOptions = new PolicePursuitTrackingOptions(
            configuration.PursuitMaxDistanceMeters,
            configuration.PursuitRouteSearchMaxDistanceMeters,
            configuration.PursuitRouteSearchMaxVisitedNodes,
            configuration.PursuitRouteGraceMilliseconds,
            new PolicePursuitLaneChangeOptions(
                configuration.PursuitLaneChangeEnabled,
                configuration.PursuitLaneChangeDistanceMeters,
                configuration.PursuitLaneChangeCooldownMilliseconds));
    }

    public void UpdateOnce()
    {
        var targetSessionId = _targetService.CurrentTargetSessionId;

        if (!targetSessionId.HasValue)
        {
            ReleaseInternal("target-disconnected");
            ClearSuspension();
            return;
        }

        if (_activeTargetSessionId.HasValue
            && _activeTargetSessionId.Value != targetSessionId.Value)
        {
            ReleaseInternal("target-changed");
        }

        if (_suspendedTargetSessionId.HasValue
            && _suspendedTargetSessionId.Value != targetSessionId.Value)
        {
            ClearSuspension();
            ResetRouteDiagnostics();
        }

        var state = _policeAiService.SelectedState;
        if (state == null)
            return;

        if (!state.IsInitialized)
        {
            ReleaseInternal("police-state-uninitialized");
            return;
        }

        var probing = _suspendedTargetSessionId == targetSessionId.Value;
        if (probing && !IsProbeDue())
            return;

        if (probing)
            _lastNoRouteProbeTimestamp = _timeProvider.GetTimestamp();

        var result = state.TrackPursuit(
            targetSessionId.Value,
            _trackingOptions);

        switch (result.Status)
        {
            case PolicePursuitTrackingStatus.Active:
                if (probing)
                {
                    Log.Information(
                        "[PoliceChase] Pursuit route probe succeeded: target {TargetSessionId}",
                        targetSessionId.Value);
                    ClearSuspension();
                }
                HandleActive(state, targetSessionId.Value, result);
                break;
            case PolicePursuitTrackingStatus.RouteTemporarilyUnavailable:
                HandleTemporaryRouteLoss(
                    targetSessionId.Value,
                    result.SearchDiagnostics);
                break;
            case PolicePursuitTrackingStatus.WaitingForSpawn:
                break;
            case PolicePursuitTrackingStatus.MaxDistanceExceeded:
                ReleaseInternal("max-distance-exceeded");
                ClearSuspension();
                break;
            case PolicePursuitTrackingStatus.NoRoute:
                if (_activeTargetSessionId.HasValue)
                {
                    LogSearchFailure(
                        "definitive",
                        targetSessionId.Value,
                        result.SearchDiagnostics);
                    ReleaseInternal("no-route");
                }
                Suspend(targetSessionId.Value);
                break;
            case PolicePursuitTrackingStatus.TargetUnavailable:
                ReleaseInternal("target-unavailable");
                ClearSuspension();
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
        ClearSuspension();
    }

    private void HandleActive(
        IPoliceAiState state,
        byte targetSessionId,
        PolicePursuitTrackingResult result)
    {
        if (!result.RouteDistanceMeters.HasValue)
            throw new InvalidOperationException("Active pursuit requires a route distance");

        var starting = !_activeTargetSessionId.HasValue;
        if (starting)
            ResetRouteDiagnostics();
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
        LogRouteTransitions(
            targetSessionId,
            result.RouteDiagnostics,
            result.SearchDiagnostics,
            starting);
        LogLaneChangeTransition(targetSessionId, result.LaneChangeDiagnostics);
        var desiredSpeed = PolicePursuitSpeedPolicy.Calculate(
            result.TargetSpeedMetersPerSecond,
            result.RouteDistanceMeters.Value,
            _configuration.PursuitDesiredDistanceMeters,
            _configuration.PursuitMaxSpeedKph);
        state.SetDesiredSpeed(desiredSpeed);
    }

    private void HandleTemporaryRouteLoss(
        byte targetSessionId,
        PolicePursuitSearchDiagnostics? diagnostics)
    {
        if (!_activeTargetSessionId.HasValue)
            return;

        if (!_routeTemporarilyLost)
        {
            LogSearchFailure("temporarily lost", targetSessionId, diagnostics);
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
        ResetRouteDiagnostics();
        Log.Information(
            "[PoliceChase] Pursuit ended: target {TargetSessionId}, reason {Reason}",
            targetSessionId,
            reason);
    }

    private bool IsProbeDue()
    {
        var elapsed = _timeProvider.GetElapsedTime(
            _lastNoRouteProbeTimestamp,
            _timeProvider.GetTimestamp());
        return elapsed >= TimeSpan.FromMilliseconds(
            _configuration.PursuitNoRouteProbeIntervalMilliseconds);
    }

    private void Suspend(byte targetSessionId)
    {
        _suspendedTargetSessionId = targetSessionId;
        _lastNoRouteProbeTimestamp = _timeProvider.GetTimestamp();
    }

    private void ClearSuspension()
    {
        _suspendedTargetSessionId = null;
        _lastNoRouteProbeTimestamp = 0;
    }

    private void ResetRouteDiagnostics()
    {
        _lastLoggedRouteRevision = null;
        _lastLoggedLaneChangeRevision = null;
        _loggedJunctionDecisions.Clear();
    }

    private void LogLaneChangeTransition(
        byte targetSessionId,
        PolicePursuitLaneChangeDiagnostics? diagnostics)
    {
        if (diagnostics == null || _lastLoggedLaneChangeRevision == diagnostics.Revision)
            return;

        switch (diagnostics.EventKind)
        {
            case PolicePursuitLaneChangeEventKind.Required:
                Log.Information(
                    "[PoliceChase] Lane change required: target {TargetSessionId}, from {FromPointId}, to {ToPointId}, direction {Direction}, distanceToDecision {DistanceToDecisionMeters:0.0}",
                    targetSessionId,
                    diagnostics.FromPointId,
                    diagnostics.ToPointId,
                    diagnostics.Direction,
                    diagnostics.DistanceToDecisionMeters);
                break;
            case PolicePursuitLaneChangeEventKind.Waiting:
                Log.Information(
                    "[PoliceChase] Lane change waiting: target {TargetSessionId}, from {FromPointId}, to {ToPointId}, direction {Direction}, reason {BlockingReason}",
                    targetSessionId,
                    diagnostics.FromPointId,
                    diagnostics.ToPointId,
                    diagnostics.Direction,
                    diagnostics.BlockingReason);
                break;
            case PolicePursuitLaneChangeEventKind.Started:
                Log.Information(
                    "[PoliceChase] Lane change started: target {TargetSessionId}, from {FromPointId}, to {ToPointId}, direction {Direction}",
                    targetSessionId,
                    diagnostics.FromPointId,
                    diagnostics.ToPointId,
                    diagnostics.Direction);
                break;
            case PolicePursuitLaneChangeEventKind.Completed:
                Log.Information(
                    "[PoliceChase] Lane change completed: target {TargetSessionId}, destination {ToPointId}, routeRevision {RouteRevision}",
                    targetSessionId,
                    diagnostics.ToPointId,
                    diagnostics.RouteRevision);
                break;
            case PolicePursuitLaneChangeEventKind.Cancelled:
                Log.Information(
                    "[PoliceChase] Lane change cancelled: target {TargetSessionId}, from {FromPointId}, to {ToPointId}, routeRevision {RouteRevision}",
                    targetSessionId,
                    diagnostics.FromPointId,
                    diagnostics.ToPointId,
                    diagnostics.RouteRevision);
                break;
            case PolicePursuitLaneChangeEventKind.RouteRevised:
                Log.Information(
                    "[PoliceChase] Lane change route revised: target {TargetSessionId}, from {FromPointId}, to {ToPointId}, routeRevision {RouteRevision}",
                    targetSessionId,
                    diagnostics.FromPointId,
                    diagnostics.ToPointId,
                    diagnostics.RouteRevision);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(diagnostics.EventKind), diagnostics.EventKind, null);
        }

        _lastLoggedLaneChangeRevision = diagnostics.Revision;
    }

    private void LogRouteTransitions(
        byte targetSessionId,
        PolicePursuitRouteDiagnostics? diagnostics,
        PolicePursuitSearchDiagnostics? searchDiagnostics,
        bool starting)
    {
        if (diagnostics == null || _lastLoggedRouteRevision == diagnostics.Revision)
            return;

        if (starting || diagnostics.UpdateKind == PolicePursuitRouteUpdateKind.Selected)
        {
            Log.Information(
                "[PoliceChase] Pursuit route selected: police {PoliceSessionId}, target {TargetSessionId}, policePoint {PolicePointId}, targetPoint {TargetPointId}, distance {RouteDistanceMeters:0.0}, junctions {JunctionCount}",
                _policeAiService.SelectedSlot?.SessionId,
                targetSessionId,
                diagnostics.PolicePointId,
                diagnostics.TargetPointId,
                diagnostics.RouteDistanceMeters,
                diagnostics.JunctionDecisions.Count);
        }
        else if (diagnostics.UpdateKind is PolicePursuitRouteUpdateKind.Recalculated
                 or PolicePursuitRouteUpdateKind.Recovered)
        {
            Log.Information(
                "[PoliceChase] Pursuit route recalculated: target {TargetSessionId}, revision {Revision}, policePoint {PolicePointId}, targetPoint {TargetPointId}, distance {RouteDistanceMeters:0.0}",
                targetSessionId,
                diagnostics.Revision,
                diagnostics.PolicePointId,
                diagnostics.TargetPointId,
                diagnostics.RouteDistanceMeters);
        }

        var currentJunctionIds = diagnostics.JunctionDecisions
            .Select(decision => decision.JunctionId)
            .ToHashSet();
        foreach (var expired in _loggedJunctionDecisions.Keys
                     .Where(junctionId => !currentJunctionIds.Contains(junctionId))
                     .ToArray())
        {
            _loggedJunctionDecisions.Remove(expired);
        }

        foreach (var decision in diagnostics.JunctionDecisions)
        {
            if (_loggedJunctionDecisions.TryGetValue(decision.JunctionId, out var previous)
                && previous == decision.TakeBranch)
            {
                continue;
            }

            _loggedJunctionDecisions[decision.JunctionId] = decision.TakeBranch;
            Log.Information(
                "[PoliceChase] Pursuit junction decision: target {TargetSessionId}, junction {JunctionId}, takeBranch {TakeBranch}, endPoint {EndPointId}",
                targetSessionId,
                decision.JunctionId,
                decision.TakeBranch,
                decision.EndPointId);
        }

        if (searchDiagnostics != null)
        {
            Log.Information(
                "[PoliceChase] Pursuit route candidates: target {TargetSessionId}, policePoint {PolicePointId}, previousTargetPoint {PreviousTargetPointId}, selectedTargetPoint {SelectedTargetPointId}, spatialCandidates {SpatialPointIds}, laneEquivalents {LaneEquivalentPointIds}, rejected {RejectedCandidates}, searchFailure {SearchFailure}, visitedNodes {VisitedNodes}, exploredDistance {MaximumExploredDistanceMeters:0.0}, junctionEdges {JunctionEdgesExamined}",
                targetSessionId,
                searchDiagnostics.PolicePointId,
                searchDiagnostics.PreviousTargetPointId,
                searchDiagnostics.SelectedTargetPointId,
                FormatPointIds(searchDiagnostics.SpatialPointIds),
                FormatPointIds(searchDiagnostics.LaneEquivalentPointIds),
                FormatRejections(searchDiagnostics.Rejections),
                searchDiagnostics.SearchFailure,
                searchDiagnostics.VisitedNodes,
                searchDiagnostics.MaximumExploredDistanceMeters,
                searchDiagnostics.JunctionEdgesExamined);
        }

        _lastLoggedRouteRevision = diagnostics.Revision;
    }

    private static void LogSearchFailure(
        string transition,
        byte targetSessionId,
        PolicePursuitSearchDiagnostics? diagnostics)
    {
        if (diagnostics == null)
        {
            Log.Information(
                "[PoliceChase] Pursuit route {Transition:l}: target {TargetSessionId}, diagnostics unavailable",
                transition,
                targetSessionId);
            return;
        }

        Log.Information(
            "[PoliceChase] Pursuit route {Transition:l}: target {TargetSessionId}, policePoint {PolicePointId}, previousTargetPoint {PreviousTargetPointId}, selectedTargetPoint {SelectedTargetPointId}, spatialCandidates {SpatialPointIds}, laneEquivalents {LaneEquivalentPointIds}, rejected {RejectedCandidates}, searchFailure {SearchFailure}, visitedNodes {VisitedNodes}, exploredDistance {MaximumExploredDistanceMeters:0.0}, junctionEdges {JunctionEdgesExamined}",
            transition,
            targetSessionId,
            diagnostics.PolicePointId,
            diagnostics.PreviousTargetPointId,
            diagnostics.SelectedTargetPointId,
            FormatPointIds(diagnostics.SpatialPointIds),
            FormatPointIds(diagnostics.LaneEquivalentPointIds),
            FormatRejections(diagnostics.Rejections),
            diagnostics.SearchFailure,
            diagnostics.VisitedNodes,
            diagnostics.MaximumExploredDistanceMeters,
            diagnostics.JunctionEdgesExamined);
    }

    private static string FormatPointIds(IReadOnlyList<int> pointIds) =>
        pointIds.Count == 0 ? "[]" : $"[{string.Join(",", pointIds)}]";

    private static string FormatRejections(
        IReadOnlyList<PolicePursuitTargetRejection> rejections) =>
        rejections.Count == 0
            ? "[]"
            : $"[{string.Join(",", rejections.Select(rejection =>
                $"{rejection.PointId}:{rejection.Reason}"))}]";
}
