using AssettoServer.Server;
using AssettoServer.Server.Ai;

namespace PoliceChasePlugin.Ai;

internal sealed class AssettoServerPoliceAiState : IPoliceAiState
{
    internal AiState NativeState { get; }
    public bool IsInitialized => NativeState.Initialized;

    public AssettoServerPoliceAiState(AiState nativeState)
    {
        NativeState = nativeState;
    }
}

internal sealed class AssettoServerPoliceAiSlotSource : IPoliceAiSlotSource
{
    private readonly EntryCarManager _entryCarManager;

    public AssettoServerPoliceAiSlotSource(EntryCarManager entryCarManager)
    {
        _entryCarManager = entryCarManager;
    }

    public IReadOnlyList<PoliceAiSlotInfo> GetSlots() =>
        _entryCarManager.EntryCars
            .Select(car => new PoliceAiSlotInfo(car.SessionId, car.Model, car.AiMode))
            .ToArray();

    public IReadOnlyList<IPoliceAiState> PrepareSingleState(byte sessionId)
    {
        var slot = _entryCarManager.EntryCars.Single(car => car.SessionId == sessionId);
        slot.AiMaxOverbooking = 1;
        slot.SetAiControl(true);
        slot.SetAiOverbooking(1);

        var initialized = new List<AiState>();
        var uninitialized = new List<AiState>();
        slot.GetInitializedStates(initialized, uninitialized);

        return initialized
            .Concat(uninitialized)
            .Select(state => (IPoliceAiState)new AssettoServerPoliceAiState(state))
            .ToArray();
    }
}
