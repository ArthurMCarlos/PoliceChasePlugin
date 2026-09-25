using Serilog;

namespace PoliceChasePlugin.Players;

public sealed record PoliceTargetIdentity(byte SessionId, long ConnectionGeneration);

public sealed class PoliceTargetService
{
    private readonly object _sync = new();
    private readonly IPolicePlayerSource _source;
    private bool _started;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<(byte Session, long Generation), (long Since, TimeSpan Duration)> _suppressed = new();
    private long _targetGeneration;

    public byte? CurrentTargetSessionId { get; private set; }
    public PoliceTargetIdentity? CurrentTargetIdentity
    {
        get { lock (_sync) return CurrentTargetSessionId is { } id ? new(id, _targetGeneration) : null; }
    }

    public PoliceTargetService(IPolicePlayerSource source, TimeProvider? timeProvider = null)
    {
        _source = source;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Refresh()
    {
        lock (_sync) { if (_started) AcquireTargetIfNeeded(); }
    }

    public void SuppressCurrentTarget(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_sync)
        {
            if (!_started || CurrentTargetSessionId is not { } session) return;
            SuppressTarget(new(session, _targetGeneration), duration);
        }
    }

    public void SuppressTarget(PoliceTargetIdentity identity, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_sync)
        {
            if (!_started) return;
            _suppressed[(identity.SessionId, identity.ConnectionGeneration)] = (_timeProvider.GetTimestamp(), duration);
            if (CurrentTargetSessionId == identity.SessionId && _targetGeneration == identity.ConnectionGeneration)
                CurrentTargetSessionId = null;
            AcquireTargetIfNeeded();
        }
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
            _suppressed.Clear();
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
                && CurrentTargetSessionId == change.Player.SessionId
                && _targetGeneration == change.Player.ConnectionGeneration)
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
        var players = _source.GetPlayers();
        var now = _timeProvider.GetTimestamp();
        foreach (var key in _suppressed.Keys.ToArray())
            if (!players.Any(p => p.SessionId == key.Session && p.ConnectionGeneration == key.Generation)
                || _timeProvider.GetElapsedTime(_suppressed[key].Since, now) >= _suppressed[key].Duration)
                _suppressed.Remove(key);
        if (CurrentTargetSessionId is { } current)
        {
            if (players.Any(p => p.SessionId == current && p.ConnectionGeneration == _targetGeneration && p.IsReady)) return;
            CurrentTargetSessionId = null;
        }

        var target = players
            .Where(player => player.IsReady && !_suppressed.ContainsKey((player.SessionId, player.ConnectionGeneration)))
            .OrderBy(player => player.SessionId)
            .FirstOrDefault();

        if (target == null)
        {
            Log.Debug("[PoliceChase] Waiting for an eligible player");
            return;
        }

        CurrentTargetSessionId = target.SessionId;
        _targetGeneration = target.ConnectionGeneration;
        Log.Information("[PoliceChase] Target acquired: {PlayerName} ({SessionId})",
            target.Name, target.SessionId);
    }
}
