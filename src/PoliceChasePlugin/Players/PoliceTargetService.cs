using Serilog;

namespace PoliceChasePlugin.Players;

public sealed class PoliceTargetService
{
    private readonly object _sync = new();
    private readonly IPolicePlayerSource _source;
    private bool _started;

    public byte? CurrentTargetSessionId { get; private set; }

    public PoliceTargetService(IPolicePlayerSource source)
    {
        _source = source;
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_started) return;
            _started = true;
            _source.Changed += OnPlayerChanged;
            _source.Start();
            AcquireTargetIfNeeded();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_started) return;
            _started = false;
            _source.Changed -= OnPlayerChanged;
            _source.Stop();
            CurrentTargetSessionId = null;
        }
    }

    private void OnPlayerChanged(PolicePlayerChange change)
    {
        lock (_sync)
        {
            if (!_started) return;

            if (change.Kind == PolicePlayerChangeKind.Connected)
            {
                Log.Information("[PoliceChase] Player connected: {PlayerName} ({SessionId})",
                    change.Player.Name, change.Player.SessionId);
            }

            if (change.Kind == PolicePlayerChangeKind.Disconnected
                && CurrentTargetSessionId == change.Player.SessionId)
            {
                Log.Information("[PoliceChase] Target released: {PlayerName} ({SessionId})",
                    change.Player.Name, change.Player.SessionId);
                CurrentTargetSessionId = null;
            }

            AcquireTargetIfNeeded();
        }
    }

    private void AcquireTargetIfNeeded()
    {
        if (CurrentTargetSessionId.HasValue) return;

        var target = _source.GetPlayers()
            .Where(player => player.IsReady)
            .OrderBy(player => player.SessionId)
            .FirstOrDefault();

        if (target == null)
        {
            Log.Debug("[PoliceChase] Waiting for an eligible player");
            return;
        }

        CurrentTargetSessionId = target.SessionId;
        Log.Information("[PoliceChase] Target acquired: {PlayerName} ({SessionId})",
            target.Name, target.SessionId);
    }
}
