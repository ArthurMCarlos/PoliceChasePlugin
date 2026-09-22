using AssettoServer.Server;

namespace PoliceChasePlugin.Ai;

public sealed record PoliceAiSlotInfo(byte SessionId, string Model, AiMode Mode);

public enum PolicePursuitTrackingStatus
{
    Active,
    WaitingForSpawn,
    RouteTemporarilyUnavailable,
    MaxDistanceExceeded,
    NoRoute,
    TargetUnavailable
}

public sealed record PolicePursuitTrackingOptions(
    float MaximumSpatialDistanceMeters,
    float MaximumRouteDistanceMeters,
    int MaximumVisitedNodes,
    int RouteGraceMilliseconds,
    PolicePursuitLaneChangeOptions? LaneChange = null,
    PolicePursuitDrivingOptions? Driving = null);

public enum PolicePursuitDrivingState
{
    CatchUp,
    Approach,
    ClosePressure,
    Contact,
    Recovery
}

public enum PolicePursuitDrivingReason
{
    DistanceCatchUp,
    DistanceApproach,
    ClosePressure,
    ContactPressure,
    ContactDisabled,
    ExcessClosingSpeed,
    LaneChangeLimited,
    CollisionRecovery
}

public sealed record PolicePursuitDrivingOptions(
    bool Enabled,
    bool ContactEnabled,
    float CatchUpDistanceMeters,
    float CloseDistanceMeters,
    float ContactDistanceMeters,
    float MaximumSpeedMetersPerSecond,
    float MaximumClosingSpeedMetersPerSecond,
    float ContactClosingSpeedMetersPerSecond);

public sealed record PolicePursuitDrivingDiagnostics(
    long Revision,
    PolicePursuitDrivingState State,
    PolicePursuitDrivingReason Reason,
    float RouteDistanceMeters,
    float PhysicalClearanceMeters,
    float TargetSpeedMetersPerSecond,
    float PoliceSpeedMetersPerSecond,
    float ClosingSpeedMetersPerSecond,
    float DesiredClosingSpeedMetersPerSecond,
    float RequestedSpeedMetersPerSecond,
    bool CollisionReported);

public sealed record PolicePursuitLaneChangeOptions(
    bool Enabled,
    float DistanceMeters,
    int CooldownMilliseconds,
    float LookaheadMeters = 1000);

public enum PolicePursuitLaneChangeEventKind
{
    Required = 0,
    Waiting = 1,
    Started = 2,
    Completed = 3,
    Cancelled = 4,
    RouteRevised = 5,
    Evaluated = 6
}

public enum PolicePursuitLaneChangeDiagnosticReason
{
    Disabled,
    NoPhysicalTarget,
    CurrentLaneValid,
    NoAdjacentLane,
    NonAdjacent,
    OppositeDirection,
    InvalidGeometry,
    NoForwardRoute,
    NoRealJunction,
    BeyondLookahead,
    InsufficientPreparationDistance,
    Cooldown,
    ObstacleAhead,
    ObstacleAlongside,
    ObstacleBehind,
    RouteRevisionChanged,
    RoutePreparation,
    Requested,
    Started,
    Completed,
    Cancelled
}

public enum PolicePursuitLaneMotivation
{
    FutureJunction,
    TargetLaneAlignment
}

public enum PolicePursuitLanePhysicalRelation
{
    SameLane,
    ImmediateLeft,
    ImmediateRight,
    NonAdjacent,
    OppositeDirection,
    InvalidGeometry
}

public enum PolicePursuitLaneChangeSafetyStatus
{
    Safe,
    BlockedFront,
    BlockedSide,
    BlockedRear,
    BlockedRearClosing
}

public enum PoliceLaneChangeDirection
{
    Left,
    Right
}

public sealed record PolicePursuitLaneChangeDiagnostics(
    long Revision,
    PolicePursuitLaneChangeEventKind EventKind,
    int? FromPointId,
    int? ToPointId,
    PoliceLaneChangeDirection? Direction,
    long RouteRevision,
    float? DistanceToDecisionMeters)
{
    public PolicePursuitLaneChangeDiagnosticReason Reason { get; init; }
    public int PolicePointId { get; init; }
    public int? PreferredPhysicalTargetPointId { get; init; }
    public int? JunctionId { get; init; }
    public PolicePursuitLaneMotivation? Motivation { get; init; }
    public PolicePursuitLanePhysicalRelation? PhysicalRelation { get; init; }
    public PolicePursuitLaneChangeSafetyStatus? SafetyStatus { get; init; }
    public PolicePursuitLaneRouteDiagnostic? CurrentLaneRoute { get; init; }
    public IReadOnlyList<PolicePursuitLaneRouteDiagnostic> CandidateLaneRoutes { get; init; } = [];
    public float? RequiredTransitionDistanceMeters { get; init; }
    public float? SourceAvailableDistanceMeters { get; init; }
    public float? DestinationAvailableDistanceMeters { get; init; }
}

public sealed record PolicePursuitLaneRouteDiagnostic(
    int? PointId,
    PoliceLaneChangeDirection? Direction,
    PolicePursuitRouteSearchFailure SearchFailure,
    float? RouteDistanceMeters,
    float MaximumExploredDistanceMeters,
    int JunctionEdgesExamined,
    int? JunctionId,
    float? DistanceToDecisionMeters,
    PolicePursuitLaneChangeDiagnosticReason Reason)
{
    public PolicePursuitLanePhysicalRelation? PhysicalRelation { get; init; }
    public PolicePursuitLaneMotivation? Motivation { get; init; }
}

public sealed record PolicePursuitJunctionDecision(
    int JunctionId,
    bool TakeBranch,
    int EndPointId);

public enum PolicePursuitRouteUpdateKind
{
    Selected,
    Reused,
    Extended,
    Recalculated,
    Recovered
}

public sealed record PolicePursuitRouteDiagnostics(
    long Revision,
    PolicePursuitRouteUpdateKind UpdateKind,
    int PolicePointId,
    int TargetPointId,
    float RouteDistanceMeters,
    int VisitedNodes,
    IReadOnlyList<PolicePursuitJunctionDecision> JunctionDecisions);

public enum PolicePursuitTargetRejectionReason
{
    InvalidDistance,
    OutsideMaximumDistance,
    MissingForwardDirection,
    OppositeDirection
}

public sealed record PolicePursuitTargetRejection(
    int PointId,
    PolicePursuitTargetRejectionReason Reason);

public enum PolicePursuitRouteSearchFailure
{
    None,
    InvalidRequest,
    DistanceLimit,
    NodeLimit,
    Unreachable
}

public sealed record PolicePursuitSearchDiagnostics(
    int PolicePointId,
    int? PreviousTargetPointId,
    int? SelectedTargetPointId,
    IReadOnlyList<int> SpatialPointIds,
    IReadOnlyList<int> LaneEquivalentPointIds,
    IReadOnlyList<PolicePursuitTargetRejection> Rejections,
    PolicePursuitRouteSearchFailure SearchFailure,
    int VisitedNodes,
    float MaximumExploredDistanceMeters,
    int JunctionEdgesExamined);

public sealed record PolicePursuitTrackingResult(
    PolicePursuitTrackingStatus Status,
    float? RouteDistanceMeters,
    float TargetSpeedMetersPerSecond,
    PolicePursuitRouteDiagnostics? RouteDiagnostics = null,
    PolicePursuitSearchDiagnostics? SearchDiagnostics = null,
    PolicePursuitLaneChangeDiagnostics? LaneChangeDiagnostics = null,
    PolicePursuitDrivingDiagnostics? DrivingDiagnostics = null);

public interface IPoliceAiState
{
    bool IsInitialized { get; }
    PolicePursuitTrackingResult TrackPursuit(
        byte targetSessionId,
        PolicePursuitTrackingOptions options);
    void SetDesiredSpeed(float metersPerSecond);
    void ReleasePursuit();
}

public interface IPoliceAiSlotSource
{
    IReadOnlyList<PoliceAiSlotInfo> GetSlots();
    IReadOnlyList<IPoliceAiState> PrepareSingleState(byte sessionId);
}
