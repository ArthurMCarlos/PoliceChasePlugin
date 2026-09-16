using AssettoServer.Server;

namespace PoliceChasePlugin.Ai;

public sealed record PoliceAiSlotInfo(byte SessionId, string Model, AiMode Mode);

public interface IPoliceAiState
{
    bool IsInitialized { get; }
}

public interface IPoliceAiSlotSource
{
    IReadOnlyList<PoliceAiSlotInfo> GetSlots();
    IReadOnlyList<IPoliceAiState> PrepareSingleState(byte sessionId);
}
