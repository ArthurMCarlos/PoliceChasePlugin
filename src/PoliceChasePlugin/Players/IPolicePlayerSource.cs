namespace PoliceChasePlugin.Players;

public sealed record PolicePlayerSnapshot(byte SessionId, string Name, bool IsReady);

public enum PolicePlayerChangeKind
{
    Connected,
    Ready,
    Disconnected
}

public sealed record PolicePlayerChange(
    PolicePlayerChangeKind Kind,
    PolicePlayerSnapshot Player);

public interface IPolicePlayerSource
{
    event Action<PolicePlayerChange>? Changed;
    IReadOnlyList<PolicePlayerSnapshot> GetPlayers();
    void Start();
    void Stop();
}
