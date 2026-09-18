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
    int RouteGraceMilliseconds);

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
    PolicePursuitSearchDiagnostics? SearchDiagnostics = null);

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
