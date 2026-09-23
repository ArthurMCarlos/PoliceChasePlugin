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
                        new PolicePursuitLaneChangeOptions(true, 60, 3000, 1000),
                        new PolicePursuitDrivingOptions(
                            false, true, 100, 15, 3, 50, 35 / 3.6f, 5 / 3.6f)))
                }));
            Assert.That(context.State.SpeedRequests.Single() * 3.6f,
                Is.EqualTo(45).Within(0.01));
            Assert.That(_sink.Events.Any(e =>
                e.RenderMessage().Contains("[PoliceChase] Pursuit started")), Is.True);
        });
    }

    [Test]
    public void AggressiveModePassesDrivingOptionsAndDoesNotApplyLegacySpeedPolicy()
    {
        var context = CreateContext(configure: configuration =>
        {
            configuration.PursuitAggressiveDrivingEnabled = true;
            configuration.PursuitContactEnabled = false;
            configuration.PursuitCatchUpDistanceMeters = 110;
            configuration.PursuitCloseDistanceMeters = 16;
            configuration.PursuitContactDistanceMeters = 4;
            configuration.PursuitMaxSpeedKph = 144;
            configuration.PursuitMaxClosingSpeedKph = 36;
            configuration.PursuitContactClosingSpeedKph = 3.6f;
        });
        context.State.NextTrackingResult = ActiveResult(driving: DrivingDiagnostics(
            revision: 1,
            PolicePursuitDrivingState.CatchUp,
            PolicePursuitDrivingReason.DistanceCatchUp));

        context.Service.UpdateOnce();

        var driving = context.State.TrackRequests.Single().Options.Driving;
        Assert.Multiple(() =>
        {
            Assert.That(driving, Is.EqualTo(new PolicePursuitDrivingOptions(
                true, false, 110, 16, 4, 40, 10, 1)));
            Assert.That(context.State.SpeedRequests, Is.Empty);
        });
    }

    [Test]
    public void DisabledAggressiveModeUsesLegacySpeedPolicy()
    {
        var context = CreateContext();
        context.State.NextTrackingResult = ActiveResult();

        context.Service.UpdateOnce();

        Assert.That(context.State.SpeedRequests.Single() * 3.6f,
            Is.EqualTo(45).Within(0.01));
    }

    [Test]
    public void PitConfigurationIsOptInAndConvertedToMetersPerSecond()
    {
        var context = CreateContext(configure: configuration =>
        {
            configuration.PursuitAggressiveDrivingEnabled = true;
            configuration.PursuitContactEnabled = true;
            configuration.PursuitPitEnabled = true;
            configuration.PursuitPitMaxClosingSpeedKph = 9;
        });
        context.State.NextTrackingResult = ActiveResult();
        context.Service.UpdateOnce();
        Assert.That(context.State.TrackRequests.Single().Options.Driving?.Pit,
            Is.EqualTo(new PolicePursuitPitOptions(true, 6, 2.5f, .8f, 1200, 3000)));
    }

    [Test]
    public void PitEventsLogOncePerRevisionAndResetOnTargetRelease()
    {
        var context = CreateContext(configure: configuration =>
        {
            configuration.PursuitAggressiveDrivingEnabled = true;
            configuration.PursuitContactEnabled = true;
            configuration.PursuitPitEnabled = true;
        });
        var pit = new PolicePursuitPitDiagnostics(1, PolicePursuitPitEventKind.Started,
            PolicePursuitPitSide.Left, PolicePursuitPitAbortReason.None, 4, 1, .8f);
        context.State.Enqueue(new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.Active, 4, 20, PitDiagnostics: pit));
        context.State.Enqueue(new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.Active, 4, 20, PitDiagnostics: pit));
        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        Assert.That(LogCount("Pursuit PIT"), Is.EqualTo(1));
        context.Service.Release();
        context.State.Enqueue(new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.Active, 4, 20, PitDiagnostics: pit));
        context.Service.UpdateOnce();
        Assert.That(LogCount("Pursuit PIT"), Is.EqualTo(2));
    }

    [Test]
    public void PitRejectionLogsOnReasonChangeOrAfterFiveSecondsOnly()
    {
        var context = CreateContext(configure: configuration =>
        {
            configuration.PursuitAggressiveDrivingEnabled = true;
            configuration.PursuitPitEnabled = true;
        });
        var blocked = new PolicePursuitPitEligibilityDiagnostics(
            PolicePursuitPitPhase.Idle, PolicePursuitPitAbortReason.GeometryInvalid,
            true, true, true, false, 4, 1, 4, 0, .9f,
            false, true, true, 5, 3.6f, PolicePursuitDrivingState.ClosePressure,
            PolicePursuitDrivingReason.ClosePressure);
        context.State.Enqueue(ActiveResult() with { PitEligibilityDiagnostics = blocked });
        context.State.Enqueue(ActiveResult() with { PitEligibilityDiagnostics = blocked });
        context.State.Enqueue(ActiveResult() with { PitEligibilityDiagnostics = blocked with
            { RejectionReason = PolicePursuitPitAbortReason.BlockedSide } });
        context.State.Enqueue(ActiveResult() with { PitEligibilityDiagnostics = blocked with
            { Phase = PolicePursuitPitPhase.Armed, RejectionReason = null } });
        context.State.Enqueue(ActiveResult() with { PitEligibilityDiagnostics = blocked with
            { RejectionReason = PolicePursuitPitAbortReason.BlockedSide } });

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        Assert.That(LogCount("PIT eligibility rejected"), Is.EqualTo(2),
            "The same rejection must stay quiet across an Armed tick");
        context.State.Enqueue(ActiveResult() with { PitEligibilityDiagnostics = blocked with
            { RejectionReason = PolicePursuitPitAbortReason.BlockedSide } });
        context.Clock.AdvanceMilliseconds(5000);
        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(LogCount("PIT eligibility rejected"), Is.EqualTo(3));
            Assert.That(_sink.Events.Any(e => e.RenderMessage().Contains(
                "reason GeometryInvalid; navigationActive True; laneFits True")), Is.True);
            Assert.That(_sink.Events.Any(e => e.RenderMessage().Contains(
                "ahead 4.0m; lateral 0.0m; headingDot 0.90")), Is.True);
        });
    }

    [Test]
    public void PitRejectionIsVisibleEvenWhenPoliceIsFarAway()
    {
        var context = CreateContext(configure: configuration =>
        {
            configuration.PursuitAggressiveDrivingEnabled = true;
            configuration.PursuitPitEnabled = true;
        });
        context.State.Enqueue(ActiveResult() with
        {
            PitEligibilityDiagnostics = new PolicePursuitPitEligibilityDiagnostics(
                PolicePursuitPitPhase.Idle, PolicePursuitPitAbortReason.OutOfRange,
                true, true, true, true, 4, 0, 150, 0, 1,
                false, true, true, 150, 3,
                PolicePursuitDrivingState.CatchUp,
                PolicePursuitDrivingReason.DistanceCatchUp)
        });

        context.Service.UpdateOnce();

        Assert.That(LogCount("PIT eligibility rejected"), Is.EqualTo(1));
    }

    [Test]
    public void AggressiveModeKeepsTemporaryRouteLossWithoutLegacySpeedWrite()
    {
        var context = CreateContext(configure: configuration =>
            configuration.PursuitAggressiveDrivingEnabled = true);
        context.State.Enqueue(ActiveResult(driving: DrivingDiagnostics(
            1,
            PolicePursuitDrivingState.Approach,
            PolicePursuitDrivingReason.DistanceApproach)));
        context.State.Enqueue(new PolicePursuitTrackingResult(
            PolicePursuitTrackingStatus.RouteTemporarilyUnavailable,
            null,
            30));

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(context.State.SpeedRequests, Is.Empty);
            Assert.That(context.State.ReleaseCount, Is.Zero);
        });
    }

    [Test]
    public void DrivingLogsDeduplicateContinuousMeasurementsWithinRevision()
    {
        var context = CreateContext(configure: configuration =>
            configuration.PursuitAggressiveDrivingEnabled = true);
        context.State.Enqueue(ActiveResult(driving: DrivingDiagnostics(
            1,
            PolicePursuitDrivingState.CatchUp,
            PolicePursuitDrivingReason.DistanceCatchUp)));
        context.State.Enqueue(ActiveResult(driving: DrivingDiagnostics(
            1,
            PolicePursuitDrivingState.CatchUp,
            PolicePursuitDrivingReason.DistanceCatchUp,
            routeDistance: 140,
            clearance: 130)));

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.That(LogCount("Pursuit driving state"), Is.EqualTo(1));
    }

    [Test]
    public void DrivingLogsEverySemanticStateTransitionWithMeasurements()
    {
        var context = CreateContext(configure: configuration =>
            configuration.PursuitAggressiveDrivingEnabled = true);
        var transitions = new[]
        {
            (PolicePursuitDrivingState.CatchUp, PolicePursuitDrivingReason.DistanceCatchUp),
            (PolicePursuitDrivingState.Approach, PolicePursuitDrivingReason.DistanceApproach),
            (PolicePursuitDrivingState.ClosePressure, PolicePursuitDrivingReason.ClosePressure),
            (PolicePursuitDrivingState.Contact, PolicePursuitDrivingReason.ContactPressure),
            (PolicePursuitDrivingState.Recovery, PolicePursuitDrivingReason.CollisionRecovery)
        };
        for (var i = 0; i < transitions.Length; i++)
        {
            context.State.Enqueue(ActiveResult(driving: DrivingDiagnostics(
                i + 1,
                transitions[i].Item1,
                transitions[i].Item2,
                collisionReported: transitions[i].Item1 == PolicePursuitDrivingState.Recovery)));
        }

        for (var i = 0; i < transitions.Length; i++)
            context.Service.UpdateOnce();

        var logs = _sink.Events
            .Select(logEvent => logEvent.RenderMessage())
            .Where(message => message.Contains("Pursuit driving state"))
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(logs, Has.Length.EqualTo(5));
            Assert.That(logs[0], Does.Contain("routeDistance 150.0m"));
            Assert.That(logs[0], Does.Contain("playerSpeed 72.0km/h"));
            Assert.That(logs[0], Does.Contain("policeSpeed 90.0km/h"));
            Assert.That(logs[0], Does.Contain("closingSpeed 18.0km/h"));
            Assert.That(logs[0], Does.Contain("targetSpeed 108.0km/h"));
            Assert.That(logs[^1], Does.Contain("CollisionRecovery"));
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
    public void AlignmentEvaluationLogsOnceUntilItsMotivationChanges()
    {
        var context = CreateContext();
        var alignment = EvaluationDiagnostics(
            1,
            PolicePursuitLaneChangeDiagnosticReason.RoutePreparation,
            null,
            0) with
        {
            DistanceToDecisionMeters = null,
            Motivation = PolicePursuitLaneMotivation.TargetLaneAlignment,
            PhysicalRelation = PolicePursuitLanePhysicalRelation.ImmediateLeft,
            CandidateLaneRoutes =
            [
                new PolicePursuitLaneRouteDiagnostic(
                    175784,
                    PoliceLaneChangeDirection.Left,
                    PolicePursuitRouteSearchFailure.None,
                    95,
                    95,
                    0,
                    null,
                    null,
                    PolicePursuitLaneChangeDiagnosticReason.RoutePreparation)
                {
                    Motivation = PolicePursuitLaneMotivation.TargetLaneAlignment
                }
            ]
        };
        context.State.Enqueue(ActiveResult(laneChange: alignment));
        context.State.Enqueue(ActiveResult(laneChange: alignment with
        {
            Revision = 2,
            CandidateLaneRoutes =
            [alignment.CandidateLaneRoutes.Single() with { RouteDistanceMeters = 93 }]
        }));
        context.State.Enqueue(ActiveResult(laneChange: alignment with
        {
            Revision = 3,
            Motivation = PolicePursuitLaneMotivation.FutureJunction
        }));

        context.Service.UpdateOnce();
        context.Service.UpdateOnce();
        context.Service.UpdateOnce();

        Assert.Multiple(() =>
        {
            Assert.That(LogCount("Lane change evaluation"), Is.EqualTo(2));
            Assert.That(_sink.Events.Any(e =>
                e.RenderMessage().Contains("TargetLaneAlignment")), Is.True);
        });
    }

    [Test]
    public void AlignmentLifecycleLogKeepsMotivationAndRelation()
    {
        var context = CreateContext();
        context.State.Enqueue(ActiveResult(laneChange:
            new PolicePursuitLaneChangeDiagnostics(
                1,
                PolicePursuitLaneChangeEventKind.Required,
                171761,
                283881,
                PoliceLaneChangeDirection.Left,
                6,
                null)
            {
                Reason = PolicePursuitLaneChangeDiagnosticReason.Requested,
                Motivation = PolicePursuitLaneMotivation.TargetLaneAlignment,
                PhysicalRelation = PolicePursuitLanePhysicalRelation.ImmediateLeft
            }));

        context.Service.UpdateOnce();

        var message = _sink.Events.Single(e =>
            e.RenderMessage().Contains("Lane change required")).RenderMessage();
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("TargetLaneAlignment"));
            Assert.That(message, Does.Contain("ImmediateLeft"));
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

    [Test]
    public void PhysicalCapacityRejectionLogsSeparateTargetAndTransitionDistances()
    {
        var context = CreateContext();
        var diagnostics = EvaluationDiagnostics(
            1,
            PolicePursuitLaneChangeDiagnosticReason.InsufficientPreparationDistance,
            null,
            0) with
        {
            DistanceToDecisionMeters = null,
            Motivation = PolicePursuitLaneMotivation.TargetLaneAlignment,
            PhysicalRelation = PolicePursuitLanePhysicalRelation.ImmediateLeft,
            RequiredTransitionDistanceMeters = 60,
            SourceAvailableDistanceMeters = 50,
            DestinationAvailableDistanceMeters = 60,
            CandidateLaneRoutes =
            [
                new PolicePursuitLaneRouteDiagnostic(
                    175784,
                    PoliceLaneChangeDirection.Left,
                    PolicePursuitRouteSearchFailure.None,
                    30,
                    30,
                    0,
                    null,
                    null,
                    PolicePursuitLaneChangeDiagnosticReason.RoutePreparation)
                {
                    Motivation = PolicePursuitLaneMotivation.TargetLaneAlignment,
                    PhysicalRelation = PolicePursuitLanePhysicalRelation.ImmediateLeft
                }
            ]
        };
        context.State.Enqueue(ActiveResult(laneChange: diagnostics));

        context.Service.UpdateOnce();

        var message = _sink.Events.Single(e =>
            e.RenderMessage().Contains("Lane change evaluation")).RenderMessage();
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("TargetLaneAlignment"));
            Assert.That(message, Does.Contain("routeToTarget 30"));
            Assert.That(message, Does.Contain("requiredTransition 60"));
            Assert.That(message, Does.Contain("sourceAvailable 50"));
            Assert.That(message, Does.Contain("destinationAvailable 60"));
        });
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
        int? junctionId,
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

    private static PolicePursuitTrackingResult ActiveResult(
        PolicePursuitDrivingDiagnostics? driving = null) =>
        new(
            PolicePursuitTrackingStatus.Active,
            150,
            20 / 3.6f,
            DrivingDiagnostics: driving);

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

    private static PolicePursuitDrivingDiagnostics DrivingDiagnostics(
        long revision,
        PolicePursuitDrivingState state,
        PolicePursuitDrivingReason reason,
        float routeDistance = 150,
        float clearance = 140,
        bool collisionReported = false) =>
        new(
            revision,
            state,
            reason,
            routeDistance,
            clearance,
            TargetSpeedMetersPerSecond: 20,
            PoliceSpeedMetersPerSecond: 25,
            ClosingSpeedMetersPerSecond: 5,
            DesiredClosingSpeedMetersPerSecond: 10,
            RequestedSpeedMetersPerSecond: 30,
            collisionReported);

    private static TestContext CreateContext(
        bool policeInitialized = true,
        Action<PoliceChaseConfiguration>? configure = null)
    {
        var configuration = new PoliceChaseConfiguration
        {
            PoliceCarSessionId = 12,
            PoliceCarModel = "police"
        };
        configure?.Invoke(configuration);
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
