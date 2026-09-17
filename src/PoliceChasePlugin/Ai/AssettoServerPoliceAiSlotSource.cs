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

internal interface INativePoliceAiSlot
{
    byte SessionId { get; }
    string Model { get; }
    AiMode Mode { get; }
    int AiMinOverbooking { get; set; }
    int? AiMaxOverbooking { get; set; }
    void SetAiControl(bool aiControlled);
    void SetAiOverbooking(int count);
    IReadOnlyList<IPoliceAiState> GetStates();
}

internal sealed class AssettoServerNativePoliceAiSlot : INativePoliceAiSlot
{
    private readonly EntryCar _slot;

    public byte SessionId => _slot.SessionId;
    public string Model => _slot.Model;
    public AiMode Mode => _slot.AiMode;

    public int AiMinOverbooking
    {
        get => _slot.AiMinOverbooking;
        set => _slot.AiMinOverbooking = value;
    }

    public int? AiMaxOverbooking
    {
        get => _slot.AiMaxOverbooking;
        set => _slot.AiMaxOverbooking = value;
    }

    public AssettoServerNativePoliceAiSlot(EntryCar slot)
    {
        _slot = slot;
    }

    public void SetAiControl(bool aiControlled) =>
        _slot.SetAiControl(aiControlled);

    public void SetAiOverbooking(int count) =>
        _slot.SetAiOverbooking(count);

    public IReadOnlyList<IPoliceAiState> GetStates()
    {
        var initialized = new List<AiState>();
        var uninitialized = new List<AiState>();
        _slot.GetInitializedStates(initialized, uninitialized);

        return initialized
            .Concat(uninitialized)
            .Select(state => (IPoliceAiState)new AssettoServerPoliceAiState(state))
            .ToArray();
    }
}

internal sealed class AssettoServerPoliceAiSlotSource : IPoliceAiSlotSource
{
    private readonly Func<IReadOnlyList<INativePoliceAiSlot>> _getSlots;

    public AssettoServerPoliceAiSlotSource(EntryCarManager entryCarManager)
    {
        _getSlots = () => entryCarManager.EntryCars
            .Select(slot => (INativePoliceAiSlot)new AssettoServerNativePoliceAiSlot(slot))
            .ToArray();
    }

    internal AssettoServerPoliceAiSlotSource(IReadOnlyList<INativePoliceAiSlot> slots)
    {
        _getSlots = () => slots;
    }

    public IReadOnlyList<PoliceAiSlotInfo> GetSlots() =>
        _getSlots()
            .Select(slot => new PoliceAiSlotInfo(slot.SessionId, slot.Model, slot.Mode))
            .ToArray();

    public IReadOnlyList<IPoliceAiState> PrepareSingleState(byte sessionId)
    {
        var slot = _getSlots().Single(candidate => candidate.SessionId == sessionId);
        slot.AiMinOverbooking = 1;
        slot.AiMaxOverbooking = 1;
        slot.SetAiControl(true);
        slot.SetAiOverbooking(1);
        return slot.GetStates();
    }
}
