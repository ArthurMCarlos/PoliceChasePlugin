using AssettoServer.Server;
using PoliceChasePlugin.Ai;

namespace PoliceChasePlugin.Tests.Ai;

internal sealed class FakePoliceAiState : IPoliceAiState
{
    public bool IsInitialized { get; }
    public PolicePursuitTrackingResult NextTrackingResult { get; set; } =
        new(PolicePursuitTrackingStatus.WaitingForSpawn, null, 0);
    public List<(byte TargetSessionId, float MaxDistanceMeters)> TrackRequests { get; } = new();
    public List<float> SpeedRequests { get; } = new();
    public int ReleaseCount { get; private set; }

    public FakePoliceAiState(bool isInitialized)
    {
        IsInitialized = isInitialized;
    }

    public PolicePursuitTrackingResult TrackPursuit(
        byte targetSessionId,
        float maxDistanceMeters)
    {
        TrackRequests.Add((targetSessionId, maxDistanceMeters));
        return NextTrackingResult;
    }

    public void SetDesiredSpeed(float metersPerSecond) =>
        SpeedRequests.Add(metersPerSecond);

    public void ReleasePursuit() => ReleaseCount++;
}

internal sealed class FakePoliceAiSlotSource : IPoliceAiSlotSource
{
    public List<PoliceAiSlotInfo> Slots { get; } = new();
    public List<IPoliceAiState> States { get; } = new();
    public byte? PreparedSessionId { get; private set; }

    public IReadOnlyList<PoliceAiSlotInfo> GetSlots() => Slots;

    public IReadOnlyList<IPoliceAiState> PrepareSingleState(byte sessionId)
    {
        PreparedSessionId = sessionId;
        return States;
    }

    public void AddFixedSlot(byte id, string model) =>
        Slots.Add(new PoliceAiSlotInfo(id, model, AiMode.Fixed));
}
