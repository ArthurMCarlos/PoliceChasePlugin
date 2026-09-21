using AssettoServer.Server;
using AssettoServer.Server.Ai;
using CoreTrackingStatus = AssettoServer.Server.Ai.AiPursuitTrackingStatus;
using CoreTrackingOptions = AssettoServer.Server.Ai.AiPursuitTrackingOptions;
using CoreRouteDiagnostics = AssettoServer.Server.Ai.AiPursuitRouteDiagnostics;
using CoreSearchDiagnostics = AssettoServer.Server.Ai.AiPursuitSearchDiagnostics;
using CoreRouteUpdateKind = AssettoServer.Server.Ai.Routing.AiPursuitRouteUpdateKind;
using CoreRejectionReason = AssettoServer.Server.Ai.Routing.AiPursuitTargetRejectionReason;
using CoreSearchFailure = AssettoServer.Server.Ai.Routing.AiRouteSearchFailure;
using CoreLaneChangeOptions = AssettoServer.Server.Ai.AiPursuitLaneChangeOptions;
using CoreLaneChangeDiagnostics = AssettoServer.Server.Ai.AiPursuitLaneChangeDiagnostics;
using CoreLaneChangeEventKind = AssettoServer.Server.Ai.AiPursuitLaneChangeEventKind;
using CoreLaneChangeDirection = AssettoServer.Server.Ai.Routing.AiLaneChangeDirection;
using CoreLaneChangeReason = AssettoServer.Server.Ai.AiPursuitLaneChangeDiagnosticReason;
using CoreLaneChangeSafetyStatus = AssettoServer.Server.Ai.AiLaneChangeSafetyStatus;
using CoreLaneRouteDiagnostic = AssettoServer.Server.Ai.Routing.AiPursuitLaneRouteDiagnostic;
using CoreLaneEvaluationReason = AssettoServer.Server.Ai.Routing.AiPursuitLaneEvaluationReason;
using CoreLaneMotivation = AssettoServer.Server.Ai.Routing.AiPursuitLaneMotivation;
using CoreLanePhysicalRelation = AssettoServer.Server.Ai.Routing.AiPursuitLanePhysicalRelation;

namespace PoliceChasePlugin.Ai;

internal interface INativePolicePursuitState
{
    bool IsInitialized { get; }
    PolicePursuitTrackingResult TrackPursuit(
        byte targetSessionId,
        PolicePursuitTrackingOptions options);
    void SetDesiredSpeed(float metersPerSecond);
    void ReleasePursuit();
}

internal sealed class AssettoServerNativePolicePursuitState : INativePolicePursuitState
{
    private readonly AiState _state;
    private readonly Func<byte, EntryCar?> _findTarget;

    public bool IsInitialized => _state.Initialized;

    public AssettoServerNativePolicePursuitState(
        AiState state,
        Func<byte, EntryCar?> findTarget)
    {
        _state = state;
        _findTarget = findTarget;
    }

    public PolicePursuitTrackingResult TrackPursuit(
        byte targetSessionId,
        PolicePursuitTrackingOptions options)
    {
        var target = _findTarget(targetSessionId);
        if (target == null || target.Client == null)
        {
            return new PolicePursuitTrackingResult(
                PolicePursuitTrackingStatus.TargetUnavailable,
                null,
                0);
        }

        var result = _state.TrackPursuit(target, new CoreTrackingOptions(
            options.MaximumSpatialDistanceMeters,
            options.MaximumRouteDistanceMeters,
            options.MaximumVisitedNodes,
            options.RouteGraceMilliseconds,
            options.LaneChange == null
                ? null
                : new CoreLaneChangeOptions(
                    options.LaneChange.Enabled,
                    options.LaneChange.DistanceMeters,
                    options.LaneChange.CooldownMilliseconds,
                    options.LaneChange.LookaheadMeters)));
        return new PolicePursuitTrackingResult(
            MapStatus(result.Status),
            result.RouteDistanceMeters,
            result.TargetSpeedMetersPerSecond,
            result.RouteDiagnostics == null
                ? null
                : MapDiagnostics(result.RouteDiagnostics),
            result.SearchDiagnostics == null
                ? null
                : MapSearchDiagnostics(result.SearchDiagnostics),
            result.LaneChangeDiagnostics == null
                ? null
                : MapLaneChangeDiagnostics(result.LaneChangeDiagnostics));
    }

    public void SetDesiredSpeed(float metersPerSecond) =>
        _state.SetPursuitDesiredSpeed(metersPerSecond);

    public void ReleasePursuit() => _state.ReleasePursuit();

    internal static PolicePursuitTrackingStatus MapStatus(CoreTrackingStatus status) =>
        status switch
        {
            CoreTrackingStatus.Active => PolicePursuitTrackingStatus.Active,
            CoreTrackingStatus.WaitingForSpawn => PolicePursuitTrackingStatus.WaitingForSpawn,
            CoreTrackingStatus.RouteTemporarilyUnavailable =>
                PolicePursuitTrackingStatus.RouteTemporarilyUnavailable,
            CoreTrackingStatus.MaxDistanceExceeded =>
                PolicePursuitTrackingStatus.MaxDistanceExceeded,
            CoreTrackingStatus.NoRoute => PolicePursuitTrackingStatus.NoRoute,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    internal static PolicePursuitRouteDiagnostics MapDiagnostics(
        CoreRouteDiagnostics diagnostics) =>
        new PolicePursuitRouteDiagnostics(
            diagnostics.Revision,
            MapUpdateKind(diagnostics.UpdateKind),
            diagnostics.PolicePointId,
            diagnostics.TargetPointId,
            diagnostics.RouteDistanceMeters,
            diagnostics.VisitedNodes,
            diagnostics.JunctionDecisions
                .Select(decision => new PolicePursuitJunctionDecision(
                    decision.JunctionId,
                    decision.TakeBranch,
                    decision.EndPointId))
                .ToArray());

    internal static PolicePursuitSearchDiagnostics MapSearchDiagnostics(
        CoreSearchDiagnostics diagnostics) =>
        new(
            diagnostics.PolicePointId,
            diagnostics.PreviousTargetPointId,
            diagnostics.SelectedTargetPointId,
            diagnostics.SpatialPointIds.ToArray(),
            diagnostics.LaneEquivalentPointIds.ToArray(),
            diagnostics.Rejections
                .Select(rejection => new PolicePursuitTargetRejection(
                    rejection.PointId,
                    MapRejectionReason(rejection.Reason)))
                .ToArray(),
            MapSearchFailure(diagnostics.SearchFailure),
            diagnostics.VisitedNodes,
            diagnostics.MaximumExploredDistanceMeters,
            diagnostics.JunctionEdgesExamined);

    internal static PolicePursuitLaneChangeDiagnostics MapLaneChangeDiagnostics(
        CoreLaneChangeDiagnostics diagnostics) =>
        new PolicePursuitLaneChangeDiagnostics(
            diagnostics.Revision,
            MapLaneChangeEventKind(diagnostics.EventKind),
            diagnostics.FromPointId,
            diagnostics.ToPointId,
            diagnostics.Direction.HasValue
                ? MapLaneChangeDirection(diagnostics.Direction.Value)
                : null,
            diagnostics.RouteRevision,
            diagnostics.DistanceToDecisionMeters)
        {
            Reason = MapLaneChangeReason(diagnostics.Reason),
            PolicePointId = diagnostics.PolicePointId,
            PreferredPhysicalTargetPointId = diagnostics.PreferredPhysicalTargetPointId,
            JunctionId = diagnostics.JunctionId,
            Motivation = diagnostics.Motivation.HasValue
                ? MapLaneMotivation(diagnostics.Motivation.Value)
                : null,
            PhysicalRelation = diagnostics.PhysicalRelation.HasValue
                ? MapLanePhysicalRelation(diagnostics.PhysicalRelation.Value)
                : null,
            SafetyStatus = diagnostics.SafetyStatus.HasValue
                ? MapLaneChangeSafetyStatus(diagnostics.SafetyStatus.Value)
                : null,
            CurrentLaneRoute = diagnostics.CurrentLaneRoute == null
                ? null
                : MapLaneRouteDiagnostic(diagnostics.CurrentLaneRoute),
            CandidateLaneRoutes = diagnostics.CandidateLaneRoutes
                .Select(MapLaneRouteDiagnostic)
                .ToArray()
        };

    private static PolicePursuitLaneRouteDiagnostic MapLaneRouteDiagnostic(
        CoreLaneRouteDiagnostic diagnostic) =>
        new PolicePursuitLaneRouteDiagnostic(
            diagnostic.PointId,
            diagnostic.Direction.HasValue
                ? MapLaneChangeDirection(diagnostic.Direction.Value)
                : null,
            MapSearchFailure(diagnostic.SearchFailure),
            diagnostic.RouteDistanceMeters,
            diagnostic.MaximumExploredDistanceMeters,
            diagnostic.JunctionEdgesExamined,
            diagnostic.JunctionId,
            diagnostic.DistanceToDecisionMeters,
            MapLaneEvaluationReason(diagnostic.Reason))
        {
            PhysicalRelation = diagnostic.PhysicalRelation.HasValue
                ? MapLanePhysicalRelation(diagnostic.PhysicalRelation.Value)
                : null
        };

    private static PolicePursuitLaneChangeEventKind MapLaneChangeEventKind(
        CoreLaneChangeEventKind eventKind) =>
        eventKind switch
        {
            CoreLaneChangeEventKind.Evaluated => PolicePursuitLaneChangeEventKind.Evaluated,
            CoreLaneChangeEventKind.Required => PolicePursuitLaneChangeEventKind.Required,
            CoreLaneChangeEventKind.Waiting => PolicePursuitLaneChangeEventKind.Waiting,
            CoreLaneChangeEventKind.Started => PolicePursuitLaneChangeEventKind.Started,
            CoreLaneChangeEventKind.Completed => PolicePursuitLaneChangeEventKind.Completed,
            CoreLaneChangeEventKind.Cancelled => PolicePursuitLaneChangeEventKind.Cancelled,
            CoreLaneChangeEventKind.RouteRevised => PolicePursuitLaneChangeEventKind.RouteRevised,
            _ => throw new ArgumentOutOfRangeException(nameof(eventKind), eventKind, null)
        };

    private static PolicePursuitLaneChangeDiagnosticReason MapLaneChangeReason(
        CoreLaneChangeReason reason) =>
        reason switch
        {
            CoreLaneChangeReason.Disabled => PolicePursuitLaneChangeDiagnosticReason.Disabled,
            CoreLaneChangeReason.NoPhysicalTarget => PolicePursuitLaneChangeDiagnosticReason.NoPhysicalTarget,
            CoreLaneChangeReason.CurrentLaneValid => PolicePursuitLaneChangeDiagnosticReason.CurrentLaneValid,
            CoreLaneChangeReason.NoAdjacentLane => PolicePursuitLaneChangeDiagnosticReason.NoAdjacentLane,
            CoreLaneChangeReason.NonAdjacent => PolicePursuitLaneChangeDiagnosticReason.NonAdjacent,
            CoreLaneChangeReason.OppositeDirection => PolicePursuitLaneChangeDiagnosticReason.OppositeDirection,
            CoreLaneChangeReason.InvalidGeometry => PolicePursuitLaneChangeDiagnosticReason.InvalidGeometry,
            CoreLaneChangeReason.NoForwardRoute => PolicePursuitLaneChangeDiagnosticReason.NoForwardRoute,
            CoreLaneChangeReason.NoRealJunction => PolicePursuitLaneChangeDiagnosticReason.NoRealJunction,
            CoreLaneChangeReason.BeyondLookahead => PolicePursuitLaneChangeDiagnosticReason.BeyondLookahead,
            CoreLaneChangeReason.InsufficientPreparationDistance => PolicePursuitLaneChangeDiagnosticReason.InsufficientPreparationDistance,
            CoreLaneChangeReason.Cooldown => PolicePursuitLaneChangeDiagnosticReason.Cooldown,
            CoreLaneChangeReason.ObstacleAhead => PolicePursuitLaneChangeDiagnosticReason.ObstacleAhead,
            CoreLaneChangeReason.ObstacleAlongside => PolicePursuitLaneChangeDiagnosticReason.ObstacleAlongside,
            CoreLaneChangeReason.ObstacleBehind => PolicePursuitLaneChangeDiagnosticReason.ObstacleBehind,
            CoreLaneChangeReason.RouteRevisionChanged => PolicePursuitLaneChangeDiagnosticReason.RouteRevisionChanged,
            CoreLaneChangeReason.RoutePreparation => PolicePursuitLaneChangeDiagnosticReason.RoutePreparation,
            CoreLaneChangeReason.Requested => PolicePursuitLaneChangeDiagnosticReason.Requested,
            CoreLaneChangeReason.Started => PolicePursuitLaneChangeDiagnosticReason.Started,
            CoreLaneChangeReason.Completed => PolicePursuitLaneChangeDiagnosticReason.Completed,
            CoreLaneChangeReason.Cancelled => PolicePursuitLaneChangeDiagnosticReason.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };

    private static PolicePursuitLaneChangeSafetyStatus MapLaneChangeSafetyStatus(
        CoreLaneChangeSafetyStatus status) =>
        status switch
        {
            CoreLaneChangeSafetyStatus.Safe => PolicePursuitLaneChangeSafetyStatus.Safe,
            CoreLaneChangeSafetyStatus.BlockedFront => PolicePursuitLaneChangeSafetyStatus.BlockedFront,
            CoreLaneChangeSafetyStatus.BlockedSide => PolicePursuitLaneChangeSafetyStatus.BlockedSide,
            CoreLaneChangeSafetyStatus.BlockedRear => PolicePursuitLaneChangeSafetyStatus.BlockedRear,
            CoreLaneChangeSafetyStatus.BlockedRearClosing => PolicePursuitLaneChangeSafetyStatus.BlockedRearClosing,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    private static PolicePursuitLaneChangeDiagnosticReason MapLaneEvaluationReason(
        CoreLaneEvaluationReason reason) =>
        reason switch
        {
            CoreLaneEvaluationReason.CurrentLaneValid => PolicePursuitLaneChangeDiagnosticReason.CurrentLaneValid,
            CoreLaneEvaluationReason.NoAdjacentLane => PolicePursuitLaneChangeDiagnosticReason.NoAdjacentLane,
            CoreLaneEvaluationReason.NonAdjacent => PolicePursuitLaneChangeDiagnosticReason.NonAdjacent,
            CoreLaneEvaluationReason.OppositeDirection => PolicePursuitLaneChangeDiagnosticReason.OppositeDirection,
            CoreLaneEvaluationReason.InvalidGeometry => PolicePursuitLaneChangeDiagnosticReason.InvalidGeometry,
            CoreLaneEvaluationReason.NoForwardRoute => PolicePursuitLaneChangeDiagnosticReason.NoForwardRoute,
            CoreLaneEvaluationReason.NoRealJunction => PolicePursuitLaneChangeDiagnosticReason.NoRealJunction,
            CoreLaneEvaluationReason.BeyondLookahead => PolicePursuitLaneChangeDiagnosticReason.BeyondLookahead,
            CoreLaneEvaluationReason.InsufficientPreparationDistance => PolicePursuitLaneChangeDiagnosticReason.InsufficientPreparationDistance,
            CoreLaneEvaluationReason.RoutePreparation => PolicePursuitLaneChangeDiagnosticReason.RoutePreparation,
            CoreLaneEvaluationReason.Cooldown => PolicePursuitLaneChangeDiagnosticReason.Cooldown,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };

    private static PolicePursuitLaneMotivation MapLaneMotivation(
        CoreLaneMotivation motivation) =>
        motivation switch
        {
            CoreLaneMotivation.FutureJunction =>
                PolicePursuitLaneMotivation.FutureJunction,
            CoreLaneMotivation.TargetLaneAlignment =>
                PolicePursuitLaneMotivation.TargetLaneAlignment,
            _ => throw new ArgumentOutOfRangeException(nameof(motivation), motivation, null)
        };

    private static PolicePursuitLanePhysicalRelation MapLanePhysicalRelation(
        CoreLanePhysicalRelation relation) =>
        relation switch
        {
            CoreLanePhysicalRelation.SameLane => PolicePursuitLanePhysicalRelation.SameLane,
            CoreLanePhysicalRelation.ImmediateLeft => PolicePursuitLanePhysicalRelation.ImmediateLeft,
            CoreLanePhysicalRelation.ImmediateRight => PolicePursuitLanePhysicalRelation.ImmediateRight,
            CoreLanePhysicalRelation.NonAdjacent => PolicePursuitLanePhysicalRelation.NonAdjacent,
            CoreLanePhysicalRelation.OppositeDirection => PolicePursuitLanePhysicalRelation.OppositeDirection,
            CoreLanePhysicalRelation.InvalidGeometry => PolicePursuitLanePhysicalRelation.InvalidGeometry,
            _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, null)
        };

    private static PoliceLaneChangeDirection MapLaneChangeDirection(
        CoreLaneChangeDirection direction) =>
        direction switch
        {
            CoreLaneChangeDirection.Left => PoliceLaneChangeDirection.Left,
            CoreLaneChangeDirection.Right => PoliceLaneChangeDirection.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };

    private static PolicePursuitTargetRejectionReason MapRejectionReason(
        CoreRejectionReason reason) =>
        reason switch
        {
            CoreRejectionReason.InvalidDistance =>
                PolicePursuitTargetRejectionReason.InvalidDistance,
            CoreRejectionReason.OutsideMaximumDistance =>
                PolicePursuitTargetRejectionReason.OutsideMaximumDistance,
            CoreRejectionReason.MissingForwardDirection =>
                PolicePursuitTargetRejectionReason.MissingForwardDirection,
            CoreRejectionReason.OppositeDirection =>
                PolicePursuitTargetRejectionReason.OppositeDirection,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };

    private static PolicePursuitRouteSearchFailure MapSearchFailure(
        CoreSearchFailure failure) =>
        failure switch
        {
            CoreSearchFailure.None => PolicePursuitRouteSearchFailure.None,
            CoreSearchFailure.InvalidRequest => PolicePursuitRouteSearchFailure.InvalidRequest,
            CoreSearchFailure.DistanceLimit => PolicePursuitRouteSearchFailure.DistanceLimit,
            CoreSearchFailure.NodeLimit => PolicePursuitRouteSearchFailure.NodeLimit,
            CoreSearchFailure.Unreachable => PolicePursuitRouteSearchFailure.Unreachable,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };

    private static PolicePursuitRouteUpdateKind MapUpdateKind(
        CoreRouteUpdateKind updateKind) =>
        updateKind switch
        {
            CoreRouteUpdateKind.Selected => PolicePursuitRouteUpdateKind.Selected,
            CoreRouteUpdateKind.Reused => PolicePursuitRouteUpdateKind.Reused,
            CoreRouteUpdateKind.Extended => PolicePursuitRouteUpdateKind.Extended,
            CoreRouteUpdateKind.Recalculated => PolicePursuitRouteUpdateKind.Recalculated,
            CoreRouteUpdateKind.Recovered => PolicePursuitRouteUpdateKind.Recovered,
            _ => throw new ArgumentOutOfRangeException(nameof(updateKind), updateKind, null)
        };
}

internal sealed class AssettoServerPoliceAiState : IPoliceAiState
{
    private readonly INativePolicePursuitState _native;

    public bool IsInitialized => _native.IsInitialized;

    public AssettoServerPoliceAiState(
        AiState nativeState,
        Func<byte, EntryCar?> findTarget)
        : this(new AssettoServerNativePolicePursuitState(nativeState, findTarget))
    {
    }

    internal AssettoServerPoliceAiState(INativePolicePursuitState native)
    {
        _native = native;
    }

    public PolicePursuitTrackingResult TrackPursuit(
        byte targetSessionId,
        PolicePursuitTrackingOptions options) =>
        _native.TrackPursuit(targetSessionId, options);

    public void SetDesiredSpeed(float metersPerSecond) =>
        _native.SetDesiredSpeed(metersPerSecond);

    public void ReleasePursuit() => _native.ReleasePursuit();
}

internal interface INativePoliceAiSlot
{
    byte SessionId { get; }
    string Model { get; }
    AiMode Mode { get; }
    int AiMinOverbooking { get; set; }
    int? AiMaxOverbooking { get; set; }
    void SetAiControl(bool aiControlled);
    void SetAiOverbooking(int count);
    IReadOnlyList<IPoliceAiState> GetStates();
}

internal sealed class AssettoServerNativePoliceAiSlot : INativePoliceAiSlot
{
    private readonly EntryCar _slot;
    private readonly Func<byte, EntryCar?> _findTarget;

    public byte SessionId => _slot.SessionId;
    public string Model => _slot.Model;
    public AiMode Mode => _slot.AiMode;

    public int AiMinOverbooking
    {
        get => _slot.AiMinOverbooking;
        set => _slot.AiMinOverbooking = value;
    }

    public int? AiMaxOverbooking
    {
        get => _slot.AiMaxOverbooking;
        set => _slot.AiMaxOverbooking = value;
    }

    public AssettoServerNativePoliceAiSlot(
        EntryCar slot,
        Func<byte, EntryCar?> findTarget)
    {
        _slot = slot;
        _findTarget = findTarget;
    }

    public void SetAiControl(bool aiControlled) =>
        _slot.SetAiControl(aiControlled);

    public void SetAiOverbooking(int count) =>
        _slot.SetAiOverbooking(count);

    public IReadOnlyList<IPoliceAiState> GetStates()
    {
        var initialized = new List<AiState>();
        var uninitialized = new List<AiState>();
        _slot.GetInitializedStates(initialized, uninitialized);

        return initialized
            .Concat(uninitialized)
            .Select(state => (IPoliceAiState)new AssettoServerPoliceAiState(
                state,
                _findTarget))
            .ToArray();
    }
}

internal sealed class AssettoServerPoliceAiSlotSource : IPoliceAiSlotSource
{
    private readonly Func<IReadOnlyList<INativePoliceAiSlot>> _getSlots;

    public AssettoServerPoliceAiSlotSource(EntryCarManager entryCarManager)
    {
        _getSlots = () =>
        {
            EntryCar? FindTarget(byte sessionId) =>
                entryCarManager.EntryCars.SingleOrDefault(car => car.SessionId == sessionId);

            return entryCarManager.EntryCars
                .Select(slot => (INativePoliceAiSlot)new AssettoServerNativePoliceAiSlot(
                    slot,
                    FindTarget))
                .ToArray();
        };
    }

    internal AssettoServerPoliceAiSlotSource(IReadOnlyList<INativePoliceAiSlot> slots)
    {
        _getSlots = () => slots;
    }

    public IReadOnlyList<PoliceAiSlotInfo> GetSlots() =>
        _getSlots()
            .Select(slot => new PoliceAiSlotInfo(slot.SessionId, slot.Model, slot.Mode))
            .ToArray();

    public IReadOnlyList<IPoliceAiState> PrepareSingleState(byte sessionId)
    {
        var slot = _getSlots().Single(candidate => candidate.SessionId == sessionId);
        slot.AiMinOverbooking = 1;
        slot.AiMaxOverbooking = 1;
        slot.SetAiControl(true);
        slot.SetAiOverbooking(1);
        return slot.GetStates();
    }
}
