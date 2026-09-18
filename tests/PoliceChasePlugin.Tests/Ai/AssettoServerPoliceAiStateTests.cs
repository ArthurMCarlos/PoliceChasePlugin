using CoreStatus = AssettoServer.Server.Ai.AiPursuitTrackingStatus;
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

        var result = state.TrackPursuit(10, 1500);
        state.SetDesiredSpeed(50);
        state.ReleasePursuit();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(native.NextResult));
            Assert.That(native.TrackRequests, Is.EqualTo(new[] { ((byte)10, 1500f) }));
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

    private sealed class FakeNativePolicePursuitState : INativePolicePursuitState
    {
        public bool IsInitialized { get; set; } = true;
        public PolicePursuitTrackingResult NextResult { get; set; } =
            new(PolicePursuitTrackingStatus.WaitingForSpawn, null, 0);
        public List<(byte TargetSessionId, float MaxDistanceMeters)> TrackRequests { get; } = new();
        public List<float> SpeedRequests { get; } = new();
        public int ReleaseCount { get; private set; }

        public PolicePursuitTrackingResult TrackPursuit(
            byte targetSessionId,
            float maxDistanceMeters)
        {
            TrackRequests.Add((targetSessionId, maxDistanceMeters));
            return NextResult;
        }

        public void SetDesiredSpeed(float metersPerSecond) =>
            SpeedRequests.Add(metersPerSecond);

        public void ReleasePursuit() => ReleaseCount++;
    }
}
