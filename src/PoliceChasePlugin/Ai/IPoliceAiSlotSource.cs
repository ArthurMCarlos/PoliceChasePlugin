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
    PolicePursuitLaneChangeOptions? LaneChange = null);

public sealed record PolicePursuitLaneChangeOptions(
    bool Enabled,
    float DistanceMeters,
    int CooldownMilliseconds,
    float LookaheadMeters = 1000);

public enum PolicePursuitLaneChangeEventKind
{
    Evaluated,
    Required,
    Waiting,
    Started,
    Completed,
    Cancelled,
    RouteRevised
}

public enum PolicePursuitLaneChangeDiagnosticReason
{
    Disabled,
    NoPhysicalTarget,
    CurrentLaneValid,
    NoAdjacentLane,
    OppositeDirection,
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
    int FromPointId,
    int ToPointId,
    PoliceLaneChangeDirection Direction,
    long RouteRevision,
    float? DistanceToDecisionMeters,
    string? BlockingReason)
{
    public PolicePursuitLaneChangeDiagnosticReason Reason { get; init; }
    public int PolicePointId { get; init; }
    public int? PreferredPhysicalTargetPointId { get; init; }
    public int? JunctionId { get; init; }
    public PolicePursuitLaneChangeSafetyStatus? SafetyStatus { get; init; }
    public PolicePursuitLaneRouteDiagnostic? CurrentLaneRoute { get; init; }
    public IReadOnlyList<PolicePursuitLaneRouteDiagnostic> CandidateLaneRoutes { get; init; } = [];
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
    PolicePursuitLaneChangeDiagnosticReason Reason);

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
    PolicePursuitLaneChangeDiagnostics? LaneChangeDiagnostics = null);

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
