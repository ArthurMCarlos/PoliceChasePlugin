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
                        1500, 20_000, 50_000, 2000))
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

    private static PolicePursuitTrackingResult ActiveResult() =>
        new(PolicePursuitTrackingStatus.Active, 150, 20 / 3.6f);

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

        return new TestContext(
            new PolicePursuitService(configuration, aiService, targetService),
            state,
            playerSource);
    }

    private sealed record TestContext(
        PolicePursuitService Service,
        FakePoliceAiState State,
        FakePolicePlayerSource PlayerSource);
}
