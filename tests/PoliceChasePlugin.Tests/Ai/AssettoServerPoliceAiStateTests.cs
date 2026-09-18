using CoreStatus = AssettoServer.Server.Ai.AiPursuitTrackingStatus;
using CoreUpdateKind = AssettoServer.Server.Ai.Routing.AiPursuitRouteUpdateKind;
using CoreDiagnostics = AssettoServer.Server.Ai.AiPursuitRouteDiagnostics;
using CoreDecision = AssettoServer.Server.Ai.AiPursuitJunctionDecision;
using CoreSearchDiagnostics = AssettoServer.Server.Ai.AiPursuitSearchDiagnostics;
using CoreRejection = AssettoServer.Server.Ai.Routing.AiPursuitTargetRejection;
using CoreRejectionReason = AssettoServer.Server.Ai.Routing.AiPursuitTargetRejectionReason;
using CoreSearchFailure = AssettoServer.Server.Ai.Routing.AiRouteSearchFailure;
using PoliceChasePlugin.Ai;

namespace PoliceChasePlugin.Tests.Ai;

[TestFixture]
public class AssettoServerPoliceAiStateTests
{
    [Test]
    public void DelegatesTrackingSpeedAndReleaseToNativeState()
    {
        var native = new FakeNativePolicePursuitState
        {
            NextResult = new PolicePursuitTrackingResult(
                PolicePursuitTrackingStatus.Active,
                125,
                30)
        };
        var state = new AssettoServerPoliceAiState(native);

        var options = new PolicePursuitTrackingOptions(1500, 20_000, 50_000, 2000);
        var result = state.TrackPursuit(10, options);
        state.SetDesiredSpeed(50);
        state.ReleasePursuit();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(native.NextResult));
            Assert.That(native.TrackRequests, Is.EqualTo(new[] { ((byte)10, options) }));
            Assert.That(native.SpeedRequests, Is.EqualTo(new[] { 50f }));
            Assert.That(native.ReleaseCount, Is.EqualTo(1));
        });
    }

    [TestCase(CoreStatus.Active, PolicePursuitTrackingStatus.Active)]
    [TestCase(CoreStatus.WaitingForSpawn, PolicePursuitTrackingStatus.WaitingForSpawn)]
    [TestCase(CoreStatus.RouteTemporarilyUnavailable, PolicePursuitTrackingStatus.RouteTemporarilyUnavailable)]
    [TestCase(CoreStatus.MaxDistanceExceeded, PolicePursuitTrackingStatus.MaxDistanceExceeded)]
    [TestCase(CoreStatus.NoRoute, PolicePursuitTrackingStatus.NoRoute)]
    public void MapsEveryCoreTrackingStatus(
        CoreStatus core,
        PolicePursuitTrackingStatus expected)
    {
        Assert.That(AssettoServerNativePolicePursuitState.MapStatus(core), Is.EqualTo(expected));
    }

    [Test]
    public void MapsCoreRouteDiagnosticsWithoutLosingIds()
    {
        var core = new CoreDiagnostics(
            3,
            CoreUpdateKind.Recalculated,
            100,
            200,
            125,
            42,
            [new CoreDecision(7, true, 300)]);

        var mapped = AssettoServerNativePolicePursuitState.MapDiagnostics(core);

        Assert.Multiple(() =>
        {
            Assert.That(mapped.Revision, Is.EqualTo(3));
            Assert.That(mapped.UpdateKind, Is.EqualTo(PolicePursuitRouteUpdateKind.Recalculated));
            Assert.That(mapped.PolicePointId, Is.EqualTo(100));
            Assert.That(mapped.TargetPointId, Is.EqualTo(200));
            Assert.That(mapped.RouteDistanceMeters, Is.EqualTo(125));
            Assert.That(mapped.VisitedNodes, Is.EqualTo(42));
            Assert.That(mapped.JunctionDecisions,
                Is.EqualTo(new[] { new PolicePursuitJunctionDecision(7, true, 300) }));
        });
    }

    [Test]
    public void MapsCoreSearchDiagnosticsWithoutLosingEvidence()
    {
        var core = new CoreSearchDiagnostics(
            PolicePointId: 171036,
            PreviousTargetPointId: 171048,
            SelectedTargetPointId: null,
            SpatialPointIds: [227470, 57704],
            LaneEquivalentPointIds: [171048],
            Rejections:
            [
                new CoreRejection(
                    265571,
                    CoreRejectionReason.OppositeDirection)
            ],
            SearchFailure: CoreSearchFailure.DistanceLimit,
            VisitedNodes: 1234,
            MaximumExploredDistanceMeters: 19_999,
            JunctionEdgesExamined: 2);

        var mapped = AssettoServerNativePolicePursuitState.MapSearchDiagnostics(core);

        Assert.Multiple(() =>
        {
            Assert.That(mapped.PolicePointId, Is.EqualTo(171036));
            Assert.That(mapped.PreviousTargetPointId, Is.EqualTo(171048));
            Assert.That(mapped.SpatialPointIds, Is.EqualTo(new[] { 227470, 57704 }));
            Assert.That(mapped.LaneEquivalentPointIds, Is.EqualTo(new[] { 171048 }));
            Assert.That(mapped.Rejections.Single(), Is.EqualTo(
                new PolicePursuitTargetRejection(
                    265571,
                    PolicePursuitTargetRejectionReason.OppositeDirection)));
            Assert.That(mapped.SearchFailure,
                Is.EqualTo(PolicePursuitRouteSearchFailure.DistanceLimit));
            Assert.That(mapped.MaximumExploredDistanceMeters, Is.EqualTo(19_999));
        });
    }

    private sealed class FakeNativePolicePursuitState : INativePolicePursuitState
    {
        public bool IsInitialized { get; set; } = true;
        public PolicePursuitTrackingResult NextResult { get; set; } =
            new(PolicePursuitTrackingStatus.WaitingForSpawn, null, 0);
        public List<(byte TargetSessionId, PolicePursuitTrackingOptions Options)> TrackRequests { get; } = new();
        public List<float> SpeedRequests { get; } = new();
        public int ReleaseCount { get; private set; }

        public PolicePursuitTrackingResult TrackPursuit(
            byte targetSessionId,
            PolicePursuitTrackingOptions options)
        {
            TrackRequests.Add((targetSessionId, options));
            return NextResult;
        }

        public void SetDesiredSpeed(float metersPerSecond) =>
            SpeedRequests.Add(metersPerSecond);

        public void ReleasePursuit() => ReleaseCount++;
    }
}
