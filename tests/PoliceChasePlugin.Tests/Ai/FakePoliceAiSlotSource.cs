using AssettoServer.Server;
using PoliceChasePlugin.Ai;

namespace PoliceChasePlugin.Tests.Ai;

internal sealed class FakePoliceAiState : IPoliceAiState
{
    public bool IsInitialized { get; set; }
    public PolicePursuitTrackingResult NextTrackingResult { get; set; } =
        new(PolicePursuitTrackingStatus.WaitingForSpawn, null, 0);
    public Queue<PolicePursuitTrackingResult> TrackingResults { get; } = new();
    public List<(byte TargetSessionId, PolicePursuitTrackingOptions Options)> TrackRequests { get; } = new();
    public List<float> SpeedRequests { get; } = new();
    public int ReleaseCount { get; private set; }
    public Action? DuringTracking { get; set; }

    public FakePoliceAiState(bool isInitialized)
    {
        IsInitialized = isInitialized;
    }

    public PolicePursuitTrackingResult TrackPursuit(
        byte targetSessionId,
        PolicePursuitTrackingOptions options)
    {
        TrackRequests.Add((targetSessionId, options));
        DuringTracking?.Invoke();
        return TrackingResults.TryDequeue(out var result)
            ? result
            : NextTrackingResult;
    }

    public void Enqueue(PolicePursuitTrackingResult result) =>
        TrackingResults.Enqueue(result);

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
