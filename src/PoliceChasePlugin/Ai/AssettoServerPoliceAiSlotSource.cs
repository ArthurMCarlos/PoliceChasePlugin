using AssettoServer.Server;
using AssettoServer.Server.Ai;
using CoreTrackingStatus = AssettoServer.Server.Ai.AiPursuitTrackingStatus;

namespace PoliceChasePlugin.Ai;

internal interface INativePolicePursuitState
{
    bool IsInitialized { get; }
    PolicePursuitTrackingResult TrackPursuit(
        byte targetSessionId,
        float maxDistanceMeters);
    void SetDesiredSpeed(float metersPerSecond);
    void ReleasePursuit();
}

internal sealed class AssettoServerNativePolicePursuitState : INativePolicePursuitState
{
    private readonly AiState _state;
    private readonly Func<byte, EntryCar?> _findTarget;

    public bool IsInitialized => _state.Initialized;

    public AssettoServerNativePolicePursuitState(
        AiState state,
        Func<byte, EntryCar?> findTarget)
    {
        _state = state;
        _findTarget = findTarget;
    }

    public PolicePursuitTrackingResult TrackPursuit(
        byte targetSessionId,
        float maxDistanceMeters)
    {
        var target = _findTarget(targetSessionId);
        if (target == null || target.Client == null)
        {
            return new PolicePursuitTrackingResult(
                PolicePursuitTrackingStatus.TargetUnavailable,
                null,
                0);
        }

        var result = _state.TrackPursuit(target, maxDistanceMeters);
        return new PolicePursuitTrackingResult(
            MapStatus(result.Status),
            result.RouteDistanceMeters,
            result.TargetSpeedMetersPerSecond);
    }

    public void SetDesiredSpeed(float metersPerSecond) =>
        _state.SetPursuitDesiredSpeed(metersPerSecond);

    public void ReleasePursuit() => _state.ReleasePursuit();

    internal static PolicePursuitTrackingStatus MapStatus(CoreTrackingStatus status) =>
        status switch
        {
            CoreTrackingStatus.Active => PolicePursuitTrackingStatus.Active,
            CoreTrackingStatus.WaitingForSpawn => PolicePursuitTrackingStatus.WaitingForSpawn,
            CoreTrackingStatus.RouteTemporarilyUnavailable =>
                PolicePursuitTrackingStatus.RouteTemporarilyUnavailable,
            CoreTrackingStatus.MaxDistanceExceeded =>
                PolicePursuitTrackingStatus.MaxDistanceExceeded,
            CoreTrackingStatus.NoRoute => PolicePursuitTrackingStatus.NoRoute,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}

internal sealed class AssettoServerPoliceAiState : IPoliceAiState
{
    private readonly INativePolicePursuitState _native;

    public bool IsInitialized => _native.IsInitialized;

    public AssettoServerPoliceAiState(
        AiState nativeState,
        Func<byte, EntryCar?> findTarget)
        : this(new AssettoServerNativePolicePursuitState(nativeState, findTarget))
    {
    }

    internal AssettoServerPoliceAiState(INativePolicePursuitState native)
    {
        _native = native;
    }

    public PolicePursuitTrackingResult TrackPursuit(
        byte targetSessionId,
        float maxDistanceMeters) =>
        _native.TrackPursuit(targetSessionId, maxDistanceMeters);

    public void SetDesiredSpeed(float metersPerSecond) =>
        _native.SetDesiredSpeed(metersPerSecond);

    public void ReleasePursuit() => _native.ReleasePursuit();
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
    private readonly Func<byte, EntryCar?> _findTarget;

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

    public AssettoServerNativePoliceAiSlot(
        EntryCar slot,
        Func<byte, EntryCar?> findTarget)
    {
        _slot = slot;
        _findTarget = findTarget;
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
            .Select(state => (IPoliceAiState)new AssettoServerPoliceAiState(
                state,
                _findTarget))
            .ToArray();
    }
}

internal sealed class AssettoServerPoliceAiSlotSource : IPoliceAiSlotSource
{
    private readonly Func<IReadOnlyList<INativePoliceAiSlot>> _getSlots;

    public AssettoServerPoliceAiSlotSource(EntryCarManager entryCarManager)
    {
        _getSlots = () =>
        {
            EntryCar? FindTarget(byte sessionId) =>
                entryCarManager.EntryCars.SingleOrDefault(car => car.SessionId == sessionId);

            return entryCarManager.EntryCars
                .Select(slot => (INativePoliceAiSlot)new AssettoServerNativePoliceAiSlot(
                    slot,
                    FindTarget))
                .ToArray();
        };
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
