using AssettoServer.Server;
using Serilog;

namespace PoliceChasePlugin.Ai;

public sealed class PoliceAiService
{
    private readonly IPoliceAiSlotSource _source;
    private readonly PoliceChaseConfiguration _configuration;

    public PoliceAiSlotInfo? SelectedSlot { get; private set; }
    public IPoliceAiState? SelectedState { get; private set; }

    public PoliceAiService(
        IPoliceAiSlotSource source,
        PoliceChaseConfiguration configuration)
    {
        _source = source;
        _configuration = configuration;
    }

    public void Prepare()
    {
        var slot = _source.GetSlots().SingleOrDefault(candidate =>
            candidate.SessionId == _configuration.PoliceCarSessionId);

        if (slot == null)
            throw new InvalidOperationException(
                $"[PoliceChase] Police slot {_configuration.PoliceCarSessionId} was not found");

        if (slot.Mode != AiMode.Fixed)
            throw new InvalidOperationException(
                $"[PoliceChase] Police slot {slot.SessionId} is not configured as AI=FIXED");

        if (!string.IsNullOrEmpty(_configuration.PoliceCarModel)
            && !string.Equals(slot.Model, _configuration.PoliceCarModel, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"[PoliceChase] Police slot {slot.SessionId} model mismatch: expected {_configuration.PoliceCarModel}, actual {slot.Model}");
        }

        var states = _source.PrepareSingleState(slot.SessionId);
        if (states.Count != 1)
            throw new InvalidOperationException(
                $"[PoliceChase] Police slot {slot.SessionId} must have exactly one state, actual {states.Count}");

        SelectedSlot = slot;
        SelectedState = states[0];
        Log.Information("[PoliceChase] Police slot prepared: {SessionId} ({Model})",
            slot.SessionId, slot.Model);
        Log.Information(SelectedState.IsInitialized
            ? "[PoliceChase] Police AI initialized"
            : "[PoliceChase] Police AI waiting for native spawn");
    }
}
