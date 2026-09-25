using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using System.Runtime.CompilerServices;

namespace PoliceChasePlugin.Players;

internal sealed class AssettoServerPlayerSource : IPolicePlayerSource
{
    private readonly EntryCarManager _entryCarManager;
    private readonly Dictionary<byte, ACTcpClient> _subscribedClients = new();
    private bool _started;
    private static long _nextGeneration;
    private sealed record ConnectionIdentity(long Generation);
    private static readonly ConditionalWeakTable<ACTcpClient, ConnectionIdentity> Identities = new();

    public event Action<PolicePlayerChange>? Changed;

    public AssettoServerPlayerSource(EntryCarManager entryCarManager)
    {
        _entryCarManager = entryCarManager;
    }

    public IReadOnlyList<PolicePlayerSnapshot> GetPlayers() =>
        _entryCarManager.EntryCars
            .Where(car => !car.AiControlled && car.Client != null)
            .Select(car => Snapshot(car.Client!))
            .ToArray();

    public void Start()
    {
        if (_started) return;
        _started = true;
        _entryCarManager.ClientConnected += OnClientConnected;
        _entryCarManager.ClientDisconnected += OnClientDisconnected;

        foreach (var client in _entryCarManager.EntryCars
                     .Where(car => !car.AiControlled && car.Client != null)
                     .Select(car => car.Client!))
        {
            Subscribe(client);
        }
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _entryCarManager.ClientConnected -= OnClientConnected;
        _entryCarManager.ClientDisconnected -= OnClientDisconnected;

        foreach (var client in _subscribedClients.Values)
        {
            client.FirstUpdateSent -= OnFirstUpdateSent;
        }

        _subscribedClients.Clear();
    }

    private void OnClientConnected(ACTcpClient client, EventArgs args)
    {
        Subscribe(client);
        Changed?.Invoke(new PolicePlayerChange(
            PolicePlayerChangeKind.Connected, Snapshot(client)));
    }

    private void OnFirstUpdateSent(ACTcpClient client, EventArgs args)
    {
        Changed?.Invoke(new PolicePlayerChange(
            PolicePlayerChangeKind.Ready, Snapshot(client)));
    }

    private void OnClientDisconnected(ACTcpClient client, EventArgs args)
    {
        Changed?.Invoke(new PolicePlayerChange(
            PolicePlayerChangeKind.Disconnected, Snapshot(client)));
        Unsubscribe(client);
    }

    private void Subscribe(ACTcpClient client)
    {
        lock (_subscribedClients)
        {
            if (_subscribedClients.TryGetValue(client.SessionId, out var previous))
            {
                if (ReferenceEquals(previous, client)) return;
                previous.FirstUpdateSent -= OnFirstUpdateSent;
            }
            _subscribedClients[client.SessionId] = client;
            client.FirstUpdateSent += OnFirstUpdateSent;
        }
    }

    private void Unsubscribe(ACTcpClient client)
    {
        lock (_subscribedClients)
        {
            if (_subscribedClients.TryGetValue(client.SessionId, out var current) && ReferenceEquals(current, client))
                _subscribedClients.Remove(client.SessionId);
            client.FirstUpdateSent -= OnFirstUpdateSent;
        }
    }

    private static PolicePlayerSnapshot Snapshot(ACTcpClient client) =>
        new(client.SessionId, client.Name ?? $"Session {client.SessionId}", client.HasSentFirstUpdate)
        {
            ConnectionGeneration = Identities.GetValue(client,
                _ => new(Interlocked.Increment(ref _nextGeneration))).Generation
        };
}
