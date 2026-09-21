using CoreStatus = AssettoServer.Server.Ai.AiPursuitTrackingStatus;
using CoreUpdateKind = AssettoServer.Server.Ai.Routing.AiPursuitRouteUpdateKind;
using CoreDiagnostics = AssettoServer.Server.Ai.AiPursuitRouteDiagnostics;
using CoreDecision = AssettoServer.Server.Ai.AiPursuitJunctionDecision;
using CoreSearchDiagnostics = AssettoServer.Server.Ai.AiPursuitSearchDiagnostics;
using CoreRejection = AssettoServer.Server.Ai.Routing.AiPursuitTargetRejection;
using CoreRejectionReason = AssettoServer.Server.Ai.Routing.AiPursuitTargetRejectionReason;
using CoreSearchFailure = AssettoServer.Server.Ai.Routing.AiRouteSearchFailure;
using CoreLaneChangeDiagnostics = AssettoServer.Server.Ai.AiPursuitLaneChangeDiagnostics;
using CoreLaneChangeEventKind = AssettoServer.Server.Ai.AiPursuitLaneChangeEventKind;
using CoreLaneChangeDirection = AssettoServer.Server.Ai.Routing.AiLaneChangeDirection;
using CoreLaneChangeReason = AssettoServer.Server.Ai.AiPursuitLaneChangeDiagnosticReason;
using CoreLaneChangeSafety = AssettoServer.Server.Ai.AiLaneChangeSafetyStatus;
using CoreLaneRouteDiagnostic = AssettoServer.Server.Ai.Routing.AiPursuitLaneRouteDiagnostic;
using CoreLaneEvaluationReason = AssettoServer.Server.Ai.Routing.AiPursuitLaneEvaluationReason;
using CoreLaneMotivation = AssettoServer.Server.Ai.Routing.AiPursuitLaneMotivation;
using CoreLanePhysicalRelation = AssettoServer.Server.Ai.Routing.AiPursuitLanePhysicalRelation;
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

    [TestCase(CoreLaneChangeEventKind.Required, PolicePursuitLaneChangeEventKind.Required)]
    [TestCase(CoreLaneChangeEventKind.Evaluated, PolicePursuitLaneChangeEventKind.Evaluated)]
    [TestCase(CoreLaneChangeEventKind.Waiting, PolicePursuitLaneChangeEventKind.Waiting)]
    [TestCase(CoreLaneChangeEventKind.Started, PolicePursuitLaneChangeEventKind.Started)]
    [TestCase(CoreLaneChangeEventKind.Completed, PolicePursuitLaneChangeEventKind.Completed)]
    [TestCase(CoreLaneChangeEventKind.Cancelled, PolicePursuitLaneChangeEventKind.Cancelled)]
    [TestCase(CoreLaneChangeEventKind.RouteRevised, PolicePursuitLaneChangeEventKind.RouteRevised)]
    public void MapsEveryLaneChangeEvent(
        CoreLaneChangeEventKind core,
        PolicePursuitLaneChangeEventKind expected)
    {
        var mapped = AssettoServerNativePolicePursuitState.MapLaneChangeDiagnostics(
            new CoreLaneChangeDiagnostics(
                7,
                core,
                100,
                200,
                CoreLaneChangeDirection.Left,
                3,
                55)
            {
                Reason = CoreLaneChangeReason.ObstacleAhead,
                SafetyStatus = CoreLaneChangeSafety.BlockedFront
            });

        Assert.Multiple(() =>
        {
            Assert.That(mapped.Revision, Is.EqualTo(7));
            Assert.That(mapped.EventKind, Is.EqualTo(expected));
            Assert.That(mapped.FromPointId, Is.EqualTo(100));
            Assert.That(mapped.ToPointId, Is.EqualTo(200));
            Assert.That(mapped.Direction, Is.EqualTo(PoliceLaneChangeDirection.Left));
            Assert.That(mapped.RouteRevision, Is.EqualTo(3));
            Assert.That(mapped.DistanceToDecisionMeters, Is.EqualTo(55));
            Assert.That(mapped.SafetyStatus,
                Is.EqualTo(PolicePursuitLaneChangeSafetyStatus.BlockedFront));
        });
    }

    [Test]
    public void MapsTypedLaneChangeEvaluationEvidence()
    {
        var core = new CoreLaneChangeDiagnostics(
            9,
            CoreLaneChangeEventKind.Evaluated,
            312936,
            175784,
            CoreLaneChangeDirection.Right,
            5,
            420)
        {
            Reason = CoreLaneChangeReason.RoutePreparation,
            PolicePointId = 312936,
            PreferredPhysicalTargetPointId = 311797,
            JunctionId = 2,
            SafetyStatus = CoreLaneChangeSafety.BlockedSide,
            CurrentLaneRoute = new CoreLaneRouteDiagnostic(
                312936,
                null,
                CoreSearchFailure.DistanceLimit,
                null,
                19_999,
                22,
                null,
                null,
                CoreLaneEvaluationReason.NoForwardRoute),
            CandidateLaneRoutes =
            [
                new CoreLaneRouteDiagnostic(
                    175784,
                    CoreLaneChangeDirection.Right,
                    CoreSearchFailure.None,
                    600,
                    600,
                    1,
                    2,
                    420,
                    CoreLaneEvaluationReason.RoutePreparation)
            ]
        };

        var mapped = AssettoServerNativePolicePursuitState.MapLaneChangeDiagnostics(core);

        Assert.Multiple(() =>
        {
            Assert.That(mapped.Reason,
                Is.EqualTo(PolicePursuitLaneChangeDiagnosticReason.RoutePreparation));
            Assert.That(mapped.PolicePointId, Is.EqualTo(312936));
            Assert.That(mapped.PreferredPhysicalTargetPointId, Is.EqualTo(311797));
            Assert.That(mapped.JunctionId, Is.EqualTo(2));
            Assert.That(mapped.SafetyStatus,
                Is.EqualTo(PolicePursuitLaneChangeSafetyStatus.BlockedSide));
            Assert.That(mapped.CurrentLaneRoute!.SearchFailure,
                Is.EqualTo(PolicePursuitRouteSearchFailure.DistanceLimit));
            Assert.That(mapped.CurrentLaneRoute.MaximumExploredDistanceMeters,
                Is.EqualTo(19_999));
            Assert.That(mapped.CandidateLaneRoutes.Single().JunctionId, Is.EqualTo(2));
        });
    }

    [Test]
    public void MapsDirectAlignmentDiagnosticWithoutInventingJunction()
    {
        var mapped = AssettoServerNativePolicePursuitState.MapLaneChangeDiagnostics(
            new CoreLaneChangeDiagnostics(
                10,
                CoreLaneChangeEventKind.Evaluated,
                171761,
                283881,
                CoreLaneChangeDirection.Left,
                6,
                94.6f)
            {
                Reason = CoreLaneChangeReason.RoutePreparation,
                PolicePointId = 171761,
                PreferredPhysicalTargetPointId = 283943,
                Motivation = CoreLaneMotivation.TargetLaneAlignment,
                PhysicalRelation = CoreLanePhysicalRelation.ImmediateLeft
            });

        Assert.Multiple(() =>
        {
            Assert.That(mapped.Motivation,
                Is.EqualTo(PolicePursuitLaneMotivation.TargetLaneAlignment));
            Assert.That(mapped.PhysicalRelation,
                Is.EqualTo(PolicePursuitLanePhysicalRelation.ImmediateLeft));
            Assert.That(mapped.JunctionId, Is.Null);
        });
    }

    [TestCase(CoreLaneChangeDirection.Left, PoliceLaneChangeDirection.Left)]
    [TestCase(CoreLaneChangeDirection.Right, PoliceLaneChangeDirection.Right)]
    public void MapsEveryLaneChangeDirection(
        CoreLaneChangeDirection core,
        PoliceLaneChangeDirection expected)
    {
        var mapped = AssettoServerNativePolicePursuitState.MapLaneChangeDiagnostics(
            new CoreLaneChangeDiagnostics(
                1,
                CoreLaneChangeEventKind.Required,
                1,
                2,
                core,
                1,
                50));

        Assert.That(mapped.Direction, Is.EqualTo(expected));
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
