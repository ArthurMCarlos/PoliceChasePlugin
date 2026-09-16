using AssettoServer.Server;
using PoliceChasePlugin.Ai;

namespace PoliceChasePlugin.Tests.Ai;

internal sealed record FakePoliceAiState(bool IsInitialized) : IPoliceAiState;

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
