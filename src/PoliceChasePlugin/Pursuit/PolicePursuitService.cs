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
    private LaneChangeLogSignature? _lastLaneChangeEvaluationSignature;
    private DrivingLogSignature? _lastDrivingLogSignature;
    private long? _lastLoggedPitRevision;
    private PolicePursuitPitAbortReason? _lastPitEligibilityReason;
    private long? _lastPitEligibilityTimestamp;
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
                configuration.PursuitLaneChangeCooldownMilliseconds,
                configuration.PursuitLaneChangeLookaheadMeters),
            new PolicePursuitDrivingOptions(
                configuration.PursuitAggressiveDrivingEnabled,
                configuration.PursuitContactEnabled,
                configuration.PursuitCatchUpDistanceMeters,
                configuration.PursuitCloseDistanceMeters,
                configuration.PursuitContactDistanceMeters,
                configuration.PursuitMaxSpeedKph / 3.6f,
                configuration.PursuitMaxClosingSpeedKph / 3.6f,
                configuration.PursuitContactClosingSpeedKph / 3.6f,
                configuration.PursuitPitEnabled ? new PolicePursuitPitOptions(
                    configuration.PursuitPitEnabled,
                    configuration.PursuitPitMaxDistanceMeters,
                    configuration.PursuitPitMaxClosingSpeedKph / 3.6f,
                    configuration.PursuitPitLateralOffsetMeters,
                    configuration.PursuitPitCommitMilliseconds,
                    configuration.PursuitPitCooldownMilliseconds) : null));
        if (configuration.PursuitPitEnabled)
            Log.Information(
                "[PoliceChase] PIT diagnostic config: enabled {Enabled}; maxDistance {MaxDistance:F1}m; maxClosing {MaxClosing:F1}km/h; offset {Offset:F2}m; commit {Commit}ms; cooldown {Cooldown}ms",
                configuration.PursuitPitEnabled,
                configuration.PursuitPitMaxDistanceMeters,
                configuration.PursuitPitMaxClosingSpeedKph,
                configuration.PursuitPitLateralOffsetMeters,
                configuration.PursuitPitCommitMilliseconds,
                configuration.PursuitPitCooldownMilliseconds);
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
        if (result.Status != PolicePursuitTrackingStatus.Active)
        {
            LogPitTransition(targetSessionId.Value, result.PitDiagnostics);
            LogLaneChangeTransition(targetSessionId.Value, result.LaneChangeDiagnostics);
        }

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
        LogPitTransition(targetSessionId, result.PitDiagnostics);
        LogPitEligibility(targetSessionId, result.PitEligibilityDiagnostics);

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
        LogDrivingTransition(targetSessionId, result.DrivingDiagnostics);
        if (!_configuration.PursuitAggressiveDrivingEnabled)
        {
            var desiredSpeed = PolicePursuitSpeedPolicy.Calculate(
                result.TargetSpeedMetersPerSecond,
                result.RouteDistanceMeters.Value,
                _configuration.PursuitDesiredDistanceMeters,
                _configuration.PursuitMaxSpeedKph);
            state.SetDesiredSpeed(desiredSpeed);
        }
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
        _lastLaneChangeEvaluationSignature = null;
        _lastDrivingLogSignature = null;
        _lastLoggedPitRevision = null;
        _lastPitEligibilityReason = null;
        _lastPitEligibilityTimestamp = null;
        _loggedJunctionDecisions.Clear();
    }

    private void LogDrivingTransition(
        byte targetSessionId,
        PolicePursuitDrivingDiagnostics? diagnostics)
    {
        if (diagnostics == null)
            return;

        var signature = new DrivingLogSignature(
            diagnostics.Revision,
            diagnostics.State,
            diagnostics.Reason,
            diagnostics.CollisionReported);
        if (_lastDrivingLogSignature == signature)
            return;
        _lastDrivingLogSignature = signature;

        Log.Information(
            "[PoliceChase] Pursuit driving state: {State}; target {TargetSessionId}; " +
            "routeDistance {RouteDistance:F1}m; clearance {Clearance:F1}m; " +
            "playerSpeed {PlayerSpeed:F1}km/h; policeSpeed {PoliceSpeed:F1}km/h; " +
            "closingSpeed {ClosingSpeed:F1}km/h; targetSpeed {RequestedSpeed:F1}km/h; reason {Reason}",
            diagnostics.State,
            targetSessionId,
            diagnostics.RouteDistanceMeters,
            diagnostics.PhysicalClearanceMeters,
            diagnostics.TargetSpeedMetersPerSecond * 3.6f,
            diagnostics.PoliceSpeedMetersPerSecond * 3.6f,
            diagnostics.ClosingSpeedMetersPerSecond * 3.6f,
            diagnostics.RequestedSpeedMetersPerSecond * 3.6f,
            diagnostics.Reason);
    }

    private void LogPitTransition(
        byte targetSessionId,
        PolicePursuitPitDiagnostics? diagnostics)
    {
        if (diagnostics == null || _lastLoggedPitRevision == diagnostics.Revision)
            return;
        _lastLoggedPitRevision = diagnostics.Revision;
        Log.Information(
            "[PoliceChase] Pursuit PIT {EventKind}; target {TargetSessionId}; " +
            "side {Side}; clearance {Clearance:F1}m; closingSpeed {ClosingSpeed:F1}km/h; " +
            "offset {Offset:F2}m; reason {Reason}",
            diagnostics.EventKind,
            targetSessionId,
            diagnostics.Side,
            diagnostics.PhysicalClearanceMeters,
            diagnostics.ClosingSpeedMetersPerSecond * 3.6f,
            diagnostics.OffsetMeters,
            diagnostics.Reason);
    }

    private void LogPitEligibility(
        byte targetSessionId,
        PolicePursuitPitEligibilityDiagnostics? diagnostics)
    {
        if (!_configuration.PursuitPitEnabled || diagnostics == null
            || diagnostics.Phase != PolicePursuitPitPhase.Idle
            || diagnostics.RejectionReason is not { } reason)
            return;

        var now = _timeProvider.GetTimestamp();
        if (_lastPitEligibilityReason == reason
            && _lastPitEligibilityTimestamp.HasValue
            && _timeProvider.GetElapsedTime(_lastPitEligibilityTimestamp.Value, now)
                < TimeSpan.FromSeconds(5))
            return;
        _lastPitEligibilityReason = reason;
        _lastPitEligibilityTimestamp = now;
        Log.Information(
            "[PoliceChase] PIT eligibility rejected: target {TargetSessionId}; reason {Reason}; " +
            "navigationActive {NavigationActive}; laneFits {LaneFits}; offsetReady {OffsetReady}; " +
            "targetAligned {TargetAligned}; ahead {Ahead:F1}m; lateral {Lateral:F1}m; headingDot {HeadingDot:F2}; " +
            "laneWidth {LaneWidth:F1}m; currentOffset {CurrentOffset:F2}m; junctionNear {JunctionNear}; " +
            "leftSafe {LeftSafe}; rightSafe {RightSafe}; lanePhase {LanePhase}; routeRevision {RouteRevision}; " +
            "clearance {Clearance:F1}m; closingSpeed {ClosingSpeed:F1}km/h; driving {DrivingState}/{DrivingReason}",
            targetSessionId, reason, diagnostics.NavigationActive, diagnostics.LaneFitsOffset,
            diagnostics.OffsetReady, diagnostics.TargetAligned,
            diagnostics.TargetLongitudinalMeters, diagnostics.TargetLateralMeters,
            diagnostics.HeadingDot, diagnostics.LaneWidthMeters,
            diagnostics.CurrentOffsetMeters, diagnostics.JunctionNear,
            diagnostics.LeftSafe, diagnostics.RightSafe, diagnostics.LaneChangePhase,
            diagnostics.RouteRevision, diagnostics.PhysicalClearanceMeters,
            diagnostics.ClosingSpeedMetersPerSecond * 3.6f,
            diagnostics.DrivingState, diagnostics.DrivingReason);
    }

    private void LogLaneChangeTransition(
        byte targetSessionId,
        PolicePursuitLaneChangeDiagnostics? diagnostics)
    {
        if (diagnostics == null)
            return;
        if (diagnostics.EventKind == PolicePursuitLaneChangeEventKind.Evaluated)
        {
            var signature = new LaneChangeLogSignature(
                diagnostics.EventKind,
                diagnostics.Reason,
                diagnostics.PreferredPhysicalTargetPointId,
                diagnostics.FromPointId,
                diagnostics.ToPointId,
                diagnostics.Direction,
                diagnostics.JunctionId,
                diagnostics.Motivation,
                diagnostics.PhysicalRelation,
                diagnostics.SafetyStatus,
                CreateLaneCandidateEvidence(diagnostics));
            if (_lastLaneChangeEvaluationSignature == signature)
                return;
            _lastLaneChangeEvaluationSignature = signature;
        }
        else if (_lastLoggedLaneChangeRevision == diagnostics.Revision)
        {
            return;
        }

        var laneContext = CreateLaneContext(diagnostics);

        switch (diagnostics.EventKind)
        {
            case PolicePursuitLaneChangeEventKind.Evaluated:
                var current = diagnostics.CurrentLaneRoute;
                var candidate = diagnostics.CandidateLaneRoutes.FirstOrDefault(route =>
                    route.PointId == diagnostics.ToPointId)
                    ?? diagnostics.CandidateLaneRoutes.FirstOrDefault();
                Log.Information(
                    "[PoliceChase] Lane change evaluation: target {TargetSessionId}{LaneContext:l}, policePoint {PolicePointId}, physicalTarget {PhysicalTargetPointId}, currentFailure {CurrentFailure}, currentDistance {CurrentDistanceMeters}, candidate {CandidatePointId}, candidateFailure {CandidateFailure}, routeToTarget {CandidateRouteDistanceMeters}, junction {JunctionId}, distanceToDecision {DistanceToDecisionMeters}, requiredTransition {RequiredTransitionDistanceMeters}, sourceAvailable {SourceAvailableDistanceMeters}, destinationAvailable {DestinationAvailableDistanceMeters}, reason {Reason}",
                    targetSessionId,
                    laneContext,
                    diagnostics.PolicePointId,
                    diagnostics.PreferredPhysicalTargetPointId,
                    current?.SearchFailure,
                    current?.RouteDistanceMeters,
                    candidate?.PointId ?? diagnostics.ToPointId,
                    candidate?.SearchFailure,
                    candidate?.RouteDistanceMeters,
                    diagnostics.JunctionId,
                    diagnostics.DistanceToDecisionMeters,
                    diagnostics.RequiredTransitionDistanceMeters,
                    diagnostics.SourceAvailableDistanceMeters,
                    diagnostics.DestinationAvailableDistanceMeters,
                    diagnostics.Reason);
                break;
            case PolicePursuitLaneChangeEventKind.Required:
                Log.Information(
                    "[PoliceChase] Lane change required: target {TargetSessionId}, from {FromPointId}, to {ToPointId}, direction {Direction}, distanceToDecision {DistanceToDecisionMeters:0.0}{LaneContext:l}",
                    targetSessionId,
                    diagnostics.FromPointId,
                    diagnostics.ToPointId,
                    diagnostics.Direction,
                    diagnostics.DistanceToDecisionMeters,
                    laneContext);
                break;
            case PolicePursuitLaneChangeEventKind.Waiting:
                Log.Information(
                    "[PoliceChase] Lane change waiting: target {TargetSessionId}, from {FromPointId}, to {ToPointId}, direction {Direction}, reason {Reason}{LaneContext:l}",
                    targetSessionId,
                    diagnostics.FromPointId,
                    diagnostics.ToPointId,
                    diagnostics.Direction,
                    diagnostics.SafetyStatus?.ToString() ?? diagnostics.Reason.ToString(),
                    laneContext);
                break;
            case PolicePursuitLaneChangeEventKind.Started:
                Log.Information(
                    "[PoliceChase] Lane change started: target {TargetSessionId}, from {FromPointId}, to {ToPointId}, direction {Direction}{LaneContext:l}",
                    targetSessionId,
                    diagnostics.FromPointId,
                    diagnostics.ToPointId,
                    diagnostics.Direction,
                    laneContext);
                break;
            case PolicePursuitLaneChangeEventKind.Completed:
                Log.Information(
                    "[PoliceChase] Lane change completed: target {TargetSessionId}, destination {ToPointId}, routeRevision {RouteRevision}{LaneContext:l}",
                    targetSessionId,
                    diagnostics.ToPointId,
                    diagnostics.RouteRevision,
                    laneContext);
                break;
            case PolicePursuitLaneChangeEventKind.Cancelled:
                Log.Information(
                    "[PoliceChase] Lane change cancelled: target {TargetSessionId}, from {FromPointId}, to {ToPointId}, routeRevision {RouteRevision}{LaneContext:l}",
                    targetSessionId,
                    diagnostics.FromPointId,
                    diagnostics.ToPointId,
                    diagnostics.RouteRevision,
                    laneContext);
                break;
            case PolicePursuitLaneChangeEventKind.RouteRevised:
                Log.Information(
                    "[PoliceChase] Lane change route revised: target {TargetSessionId}, from {FromPointId}, to {ToPointId}, routeRevision {RouteRevision}{LaneContext:l}",
                    targetSessionId,
                    diagnostics.FromPointId,
                    diagnostics.ToPointId,
                    diagnostics.RouteRevision,
                    laneContext);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(diagnostics.EventKind), diagnostics.EventKind, null);
        }

        if (diagnostics.EventKind != PolicePursuitLaneChangeEventKind.Evaluated)
            _lastLoggedLaneChangeRevision = diagnostics.Revision;
    }

    private static string CreateLaneContext(
        PolicePursuitLaneChangeDiagnostics diagnostics)
    {
        var motivation = diagnostics.Motivation.HasValue
            ? $", motivation {diagnostics.Motivation.Value}"
            : string.Empty;
        var relation = diagnostics.PhysicalRelation.HasValue
            ? $", relation {diagnostics.PhysicalRelation.Value}"
            : string.Empty;
        return motivation + relation;
    }

    private sealed record LaneChangeLogSignature(
        PolicePursuitLaneChangeEventKind EventKind,
        PolicePursuitLaneChangeDiagnosticReason Reason,
        int? PreferredPhysicalTargetPointId,
        int? FromPointId,
        int? ToPointId,
        PoliceLaneChangeDirection? Direction,
        int? JunctionId,
        PolicePursuitLaneMotivation? Motivation,
        PolicePursuitLanePhysicalRelation? PhysicalRelation,
        PolicePursuitLaneChangeSafetyStatus? SafetyStatus,
        string CandidateEvidence);

    private sealed record DrivingLogSignature(
        long Revision,
        PolicePursuitDrivingState State,
        PolicePursuitDrivingReason Reason,
        bool CollisionReported);

    private static string CreateLaneCandidateEvidence(
        PolicePursuitLaneChangeDiagnostics diagnostics) =>
        string.Join("|", diagnostics.CandidateLaneRoutes
            .OrderBy(candidate => candidate.Direction)
            .ThenBy(candidate => candidate.PointId)
            .Select(candidate =>
                $"{candidate.PointId}:{candidate.Direction}:{candidate.SearchFailure}:" +
                $"{candidate.JunctionId}:{candidate.Reason}:{candidate.PhysicalRelation}"));

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
