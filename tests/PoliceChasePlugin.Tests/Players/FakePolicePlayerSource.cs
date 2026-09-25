using PoliceChasePlugin.Players;

namespace PoliceChasePlugin.Tests.Players;

internal sealed class FakePolicePlayerSource : IPolicePlayerSource
{
    private readonly List<PolicePlayerSnapshot> _players = new();
    private long _generation;

    public event Action<PolicePlayerChange>? Changed;
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }

    public IReadOnlyList<PolicePlayerSnapshot> GetPlayers() => _players.ToArray();

    public void Start() => StartCount++;
    public void Stop() => StopCount++;

    public void Connect(byte id, string name, bool ready = false)
    {
        var player = new PolicePlayerSnapshot(id, name, ready) { ConnectionGeneration = ++_generation };
        _players.Add(player);
        Changed?.Invoke(new PolicePlayerChange(PolicePlayerChangeKind.Connected, player));
    }

    public void MarkReady(byte id)
    {
        var index = _players.FindIndex(player => player.SessionId == id);
        var ready = _players[index] with { IsReady = true };
        _players[index] = ready;
        Changed?.Invoke(new PolicePlayerChange(PolicePlayerChangeKind.Ready, ready));
    }

    public PolicePlayerSnapshot ReplaceBeforeDisconnect(byte id)
    {
        var previous = _players.Single(p => p.SessionId == id);
        _players.Remove(previous);
        Connect(id, "Replacement", true);
        return previous;
    }

    public void DeliverLateDisconnect(PolicePlayerSnapshot previous) =>
        Changed?.Invoke(new(PolicePlayerChangeKind.Disconnected, previous));

    public void Disconnect(byte id)
    {
        var player = _players.Single(player => player.SessionId == id);
        _players.Remove(player);
        Changed?.Invoke(new PolicePlayerChange(PolicePlayerChangeKind.Disconnected, player));
    }
}
