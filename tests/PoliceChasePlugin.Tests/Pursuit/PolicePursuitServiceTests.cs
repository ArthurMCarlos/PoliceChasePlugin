using PoliceChasePlugin.Ai;
using PoliceChasePlugin.Players;
using PoliceChasePlugin.Pursuit;
using PoliceChasePlugin.Tests.Ai;
using PoliceChasePlugin.Tests.Players;
using Serilog;

namespace PoliceChasePlugin.Tests.Pursuit;

[TestFixture]
[NonParallelizable]
public class PolicePursuitServiceTests
{
    private CollectingLogSink _sink = null!;

    [SetUp]
    public void SetUp()
    {
        _sink = new CollectingLogSink();
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(_sink).CreateLogger();
    }

    [TearDown]
    public void TearDown()
    {
        Log.CloseAndFlush();
    }

    [Test]
    public void ActiveRouteRequestsCalculatedSpeed()
    {
        var context = CreateContext();
        context.State.NextTrackingResult = new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.Active,
            150,
            20 / 3.6f);

        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(context.State.TrackRequests,
                Is.EqualTo(new[]
                {
                    ((byte)10, new PolicePursuitTrackingOptions(
                        1500, 20_000, 50_000, 2000,
                        new PolicePursuitLaneChangeOptions(true, 60, 3000, 1000)))
                }));
            Assert.That(context.State.SpeedRequests.Single() * 3.6f,
                Is.EqualTo(45).Within(0.01));
            Assert.That(_sink.Events.Any(e =>
                e.RenderMessage().Contains("[PoliceChase] Pursuit started")), Is.True);
        });
    }

    [Test]
    public void WaitsForPoliceNativeSpawn()
    {
        var context = CreateContext(policeInitialized: false);

        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(context.State.TrackRequests, Is.Empty);
            Assert.That(context.State.SpeedRequests, Is.Empty);
            Assert.That(context.State.ReleaseCount, Is.Zero);
        });
    }

    [Test]
    public void TemporaryRouteLossKeepsExistingPursuitWithoutBlindAcceleration()
    {
        var context = CreateContext();
        context.State.NextTrackingResult = ActiveResult();
        context.Service.UpdateOnce();
        context.State.NextTrackingResult = new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.RouteTemporarilyUnavailable,
            null,
            30);

        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(context.State.ReleaseCount, Is.Zero);
            Assert.That(context.State.SpeedRequests, Has.Count.EqualTo(1));
            Assert.That(_sink.Events.Any(e =>
                e.RenderMessage().Contains("[PoliceChase] Pursuit route temporarily lost")), Is.True);
        });
    }

    [Test]
    public void TemporaryRouteLossLogsSearchDiagnosticsOnlyOnce()
    {
        var context = CreateContext();
        context.State.NextTrackingResult = ActiveResult();
        context.Service.UpdateOnce();
        context.State.NextTrackingResult = new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.RouteTemporarilyUnavailable,
            null,
            30,
            SearchDiagnostics: new PolicePursuitSearchDiagnostics(
                PolicePointId: 171036,
                PreviousTargetPointId: 171048,
                SelectedTargetPointId: null,
                SpatialPointIds: [227470, 57704],
                LaneEquivalentPointIds: [171048],
                Rejections:
                [
                    new PolicePursuitTargetRejection(
                        265571,
                        PolicePursuitTargetRejectionReason.OppositeDirection)
                ],
                SearchFailure: PolicePursuitRouteSearchFailure.DistanceLimit,
                VisitedNodes: 1234,
                MaximumExploredDistanceMeters: 19_999,
                JunctionEdgesExamined: 2));

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        var diagnosticLogs = _sink.Events
            .Select(logEvent => logEvent.RenderMessage())
            .Where(message => message.Contains("Pursuit route temporarily lost"))
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(diagnosticLogs, Has.Length.EqualTo(1));
            Assert.That(diagnosticLogs[0], Does.Contain("policePoint 171036"));
            Assert.That(diagnosticLogs[0], Does.Contain("previousTargetPoint 171048"));
            Assert.That(diagnosticLogs[0], Does.Contain("227470"));
            Assert.That(diagnosticLogs[0], Does.Contain("171048"));
            Assert.That(diagnosticLogs[0], Does.Contain("DistanceLimit"));
            Assert.That(diagnosticLogs[0], Does.Contain("visitedNodes 1234"));
            Assert.That(diagnosticLogs[0], Does.Contain("junctionEdges 2"));
        });
    }

    [Test]
    public void RecoveredRouteResumesSpeedAndLogsOnce()
    {
        var context = CreateContext();
        context.State.NextTrackingResult = ActiveResult();
        context.Service.UpdateOnce();
        context.State.NextTrackingResult = new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.RouteTemporarilyUnavailable,
            null,
            30);
        context.Service.UpdateOnce();
        context.State.NextTrackingResult = ActiveResult();

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(context.State.SpeedRequests, Has.Count.EqualTo(3));
            Assert.That(_sink.Events.Count(e =>
                e.RenderMessage().Contains("Pursuit route recovered")), Is.EqualTo(1));
        });
    }

    [TestCase(PolicePursuitTrackingStatus.MaxDistanceExceeded, "max-distance-exceeded")]
    [TestCase(PolicePursuitTrackingStatus.NoRoute, "no-route")]
    [TestCase(PolicePursuitTrackingStatus.TargetUnavailable, "target-unavailable")]
    public void DefinitiveLossReleasesPursuitOnce(
        PolicePursuitTrackingStatus status,
        string reason)
    {
        var context = CreateContext();
        context.State.NextTrackingResult = ActiveResult();
        context.Service.UpdateOnce();
        context.State.NextTrackingResult = new PolicePursuitTrackingResult(status, null, 0);

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(context.State.ReleaseCount, Is.EqualTo(1));
            Assert.That(_sink.Events.Count(e =>
                e.RenderMessage().Contains(reason)), Is.EqualTo(1));
        });
    }

    [Test]
    public void TargetDisconnectReleasesActivePursuit()
    {
        var context = CreateContext();
        context.State.NextTrackingResult = ActiveResult();
        context.Service.UpdateOnce();

        context.PlayerSource.Disconnect(10);
        context.Service.UpdateOnce();

        Assert.That(context.State.ReleaseCount, Is.EqualTo(1));
    }

    [Test]
    public void PoliceStateBecomingUninitializedEndsActivePursuitOnce()
    {
        var context = CreateContext();
        context.State.NextTrackingResult = ActiveResult();
        context.Service.UpdateOnce();
        context.State.IsInitialized = false;

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(context.State.ReleaseCount, Is.EqualTo(1));
            Assert.That(_sink.Events.Count(e =>
                e.RenderMessage().Contains("police-state-uninitialized")), Is.EqualTo(1));
        });
    }

    [Test]
    public void TargetChangeReleasesOldControlBeforeTrackingNewTarget()
    {
        var context = CreateContext();
        context.State.NextTrackingResult = ActiveResult();
        context.Service.UpdateOnce();
        context.PlayerSource.Connect(11, "Next", ready: true);
        context.PlayerSource.Disconnect(10);

        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(context.State.ReleaseCount, Is.EqualTo(1));
            Assert.That(context.State.TrackRequests.Last().TargetSessionId, Is.EqualTo(11));
        });
    }

    [Test]
    public void ReleaseIsIdempotent()
    {
        var context = CreateContext();
        context.State.NextTrackingResult = ActiveResult();
        context.Service.UpdateOnce();

        context.Service.Release();
        context.Service.Release();

        Assert.That(context.State.ReleaseCount, Is.EqualTo(1));
    }

    [Test]
    public void NoRouteSuspendsSameTargetUntilProbeFindsRoute()
    {
        var context = CreateContext();
        context.State.Enqueue(ActiveResult(revision: 1));
        context.State.Enqueue(new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.NoRoute, null, 0));
        context.State.Enqueue(ActiveResult(revision: 2));

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        context.Clock.AdvanceMilliseconds(1999);
        context.Service.UpdateOnce();
        context.Clock.AdvanceMilliseconds(1);
        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(context.State.TrackRequests, Has.Count.EqualTo(3));
            Assert.That(LogCount("Pursuit started"), Is.EqualTo(2));
            Assert.That(LogCount("Pursuit ended"), Is.EqualTo(1));
            Assert.That(LogCount("route probe succeeded"), Is.EqualTo(1));
        });
    }

    [Test]
    public void FailedProbeDoesNotRestartOrLogAnotherEnd()
    {
        var context = CreateContext();
        context.State.Enqueue(ActiveResult(revision: 1));
        context.State.Enqueue(new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.NoRoute, null, 0));
        context.State.Enqueue(new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.NoRoute, null, 0));

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        context.Clock.AdvanceMilliseconds(2000);
        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(context.State.TrackRequests, Has.Count.EqualTo(3));
            Assert.That(LogCount("Pursuit started"), Is.EqualTo(1));
            Assert.That(LogCount("Pursuit ended"), Is.EqualTo(1));
        });
    }

    [Test]
    public void RouteRevisionAndJunctionDecisionsLogOnlyOnTransitions()
    {
        var context = CreateContext();
        var selected = ActiveResult(
            revision: 1,
            PolicePursuitRouteUpdateKind.Selected,
            [new PolicePursuitJunctionDecision(7, true, 300)]);
        var recalculated = ActiveResult(
            revision: 2,
            PolicePursuitRouteUpdateKind.Recalculated,
            [new PolicePursuitJunctionDecision(7, true, 300)]);
        context.State.Enqueue(selected);
        context.State.Enqueue(selected);
        context.State.Enqueue(recalculated);

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(LogCount("Pursuit route selected"), Is.EqualTo(1));
            Assert.That(LogCount("Pursuit route recalculated"), Is.EqualTo(1));
            Assert.That(LogCount("Pursuit junction decision"), Is.EqualTo(1));
        });
    }

    [Test]
    public void LaneChangeEventsLogOnlyOncePerRevision()
    {
        var context = CreateContext();
        var required = ActiveResult(laneChange: new PolicePursuitLaneChangeDiagnostics(
            1,
            PolicePursuitLaneChangeEventKind.Required,
            100,
            101,
            PoliceLaneChangeDirection.Left,
            4,
            55));
        var waiting = ActiveResult(laneChange: new PolicePursuitLaneChangeDiagnostics(
            2,
            PolicePursuitLaneChangeEventKind.Waiting,
            100,
            101,
            PoliceLaneChangeDirection.Left,
            4,
            54)
        {
            Reason = PolicePursuitLaneChangeDiagnosticReason.ObstacleBehind,
            SafetyStatus = PolicePursuitLaneChangeSafetyStatus.BlockedRearClosing
        });
        context.State.Enqueue(required);
        context.State.Enqueue(required);
        context.State.Enqueue(waiting);
        context.State.Enqueue(waiting);

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(LogCount("Lane change required"), Is.EqualTo(1));
            Assert.That(LogCount("Lane change waiting"), Is.EqualTo(1));
            Assert.That(_sink.Events.Any(e =>
                e.RenderMessage().Contains("BlockedRearClosing")), Is.True);
        });
    }

    [Test]
    public void LaneChangeEvaluationsDeduplicateDistanceOnlyChanges()
    {
        var context = CreateContext();
        var first = EvaluationDiagnostics(
            revision: 1,
            reason: PolicePursuitLaneChangeDiagnosticReason.RoutePreparation,
            junctionId: 2,
            distanceToDecision: 500);
        var sameMeaning = EvaluationDiagnostics(
            revision: 2,
            reason: PolicePursuitLaneChangeDiagnosticReason.RoutePreparation,
            junctionId: 2,
            distanceToDecision: 498) with
        {
            PolicePointId = 312937,
            RouteRevision = 6
        };
        var changed = EvaluationDiagnostics(
            revision: 3,
            reason: PolicePursuitLaneChangeDiagnosticReason.BeyondLookahead,
            junctionId: 3,
            distanceToDecision: 1200);
        context.State.Enqueue(ActiveResult(laneChange: first));
        context.State.Enqueue(ActiveResult(laneChange: sameMeaning));
        context.State.Enqueue(ActiveResult(laneChange: changed));

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(LogCount("Lane change evaluation"), Is.EqualTo(2));
            Assert.That(_sink.Events.Any(e =>
                e.RenderMessage().Contains("RoutePreparation")), Is.True);
            Assert.That(_sink.Events.Any(e =>
                e.RenderMessage().Contains("BeyondLookahead")), Is.True);
        });
    }

    [Test]
    public void TemporaryRouteLossStillLogsLaneChangeLifecycleEvent()
    {
        var context = CreateContext();
        context.State.Enqueue(ActiveResult());
        context.State.Enqueue(new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.RouteTemporarilyUnavailable,
            null,
            20 / 3.6f,
            LaneChangeDiagnostics: new PolicePursuitLaneChangeDiagnostics(
                7,
                PolicePursuitLaneChangeEventKind.Completed,
                100,
                101,
                PoliceLaneChangeDirection.Left,
                4,
                null)));

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.That(LogCount("Lane change completed"), Is.EqualTo(1));
    }

    [Test]
    public void EvaluationLogsWhenSecondRejectedLaneEvidenceChanges()
    {
        var context = CreateContext();
        var first = EvaluationDiagnostics(1,
            PolicePursuitLaneChangeDiagnosticReason.NoForwardRoute, 2, 500) with
        {
            CandidateLaneRoutes = [LaneCandidate(10, 2), LaneCandidate(20, 7)]
        };
        var changed = first with
        {
            Revision = 2,
            PolicePointId = 312937,
            RouteRevision = 6,
            CandidateLaneRoutes = [LaneCandidate(10, 2), LaneCandidate(20, 8)]
        };
        context.State.Enqueue(ActiveResult(laneChange: first));
        context.State.Enqueue(ActiveResult(laneChange: changed));

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.That(LogCount("Lane change evaluation"), Is.EqualTo(2));
    }

    private static PolicePursuitLaneRouteDiagnostic LaneCandidate(
        int pointId,
        int junctionId) =>
        new(
            pointId,
            pointId == 10 ? PoliceLaneChangeDirection.Left : PoliceLaneChangeDirection.Right,
            PolicePursuitRouteSearchFailure.None,
            600,
            600,
            1,
            junctionId,
            500,
            PolicePursuitLaneChangeDiagnosticReason.NoForwardRoute);

    private static PolicePursuitLaneChangeDiagnostics EvaluationDiagnostics(
        long revision,
        PolicePursuitLaneChangeDiagnosticReason reason,
        int junctionId,
        float distanceToDecision) =>
        new(
            revision,
            PolicePursuitLaneChangeEventKind.Evaluated,
            312936,
            175784,
            PoliceLaneChangeDirection.Right,
            5,
            distanceToDecision)
        {
            Reason = reason,
            PolicePointId = 312936,
            PreferredPhysicalTargetPointId = 311797,
            JunctionId = junctionId
        };

    private static PolicePursuitTrackingResult ActiveResult() =>
        new(PolicePursuitTrackingStatus.Active, 150, 20 / 3.6f);

    private static PolicePursuitTrackingResult ActiveResult(
        PolicePursuitLaneChangeDiagnostics laneChange) =>
        new(
            PolicePursuitTrackingStatus.Active,
            150,
            20 / 3.6f,
            LaneChangeDiagnostics: laneChange);

    private static PolicePursuitTrackingResult ActiveResult(
        long revision,
        PolicePursuitRouteUpdateKind updateKind = PolicePursuitRouteUpdateKind.Selected,
        IReadOnlyList<PolicePursuitJunctionDecision>? decisions = null) =>
        new(
            PolicePursuitTrackingStatus.Active,
            150,
            20 / 3.6f,
            new PolicePursuitRouteDiagnostics(
                revision,
                updateKind,
                100,
                200,
                150,
                12,
                decisions ?? []));

    private static TestContext CreateContext(bool policeInitialized = true)
    {
        var configuration = new PoliceChaseConfiguration
        {
            PoliceCarSessionId = 12,
            PoliceCarModel = "police"
        };
        var state = new FakePoliceAiState(policeInitialized);
        var aiSource = new FakePoliceAiSlotSource();
        aiSource.AddFixedSlot(12, "police");
        aiSource.States.Add(state);
        var aiService = new PoliceAiService(aiSource, configuration);
        aiService.Prepare();

        var playerSource = new FakePolicePlayerSource();
        playerSource.Connect(10, "Target", ready: true);
        var targetService = new PoliceTargetService(playerSource);
        targetService.Start();

        var clock = new ManualTimeProvider();
        return new TestContext(
            new PolicePursuitService(configuration, aiService, targetService, clock),
            state,
            playerSource,
            clock);
    }

    private int LogCount(string text) =>
        _sink.Events.Count(logEvent => logEvent.RenderMessage().Contains(text));

    private sealed record TestContext(
        PolicePursuitService Service,
        FakePoliceAiState State,
        FakePolicePlayerSource PlayerSource,
        ManualTimeProvider Clock);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp;

        public void AdvanceMilliseconds(long milliseconds) =>
            _timestamp += milliseconds;
    }
}
