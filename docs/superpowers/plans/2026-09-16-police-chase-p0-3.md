# Police Chase P0.3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect the first ready human player and prepare exactly one configured `AI=FIXED` police slot without changing speed, spline, route, or global traffic behavior.

**Architecture:** Add event-driven player acquisition behind a testable `IPolicePlayerSource` port and exact Session ID police-slot preparation behind an `IPoliceAiSlotSource` port. AssettoServer adapters are the only classes that touch `EntryCarManager`, `ACTcpClient`, `EntryCar`, and `AiState`; orchestration remains in the existing `PoliceChaseService`.

**Tech Stack:** C# 12 / .NET 8, Autofac 7.1, FluentValidation 11.8, Serilog 3.1, NUnit 3.14, AssettoServer v0.0.54.

**Spec:** `docs/superpowers/specs/2026-09-16-police-chase-p0-3-design.md`

## Global Constraints

- Compile only against AssettoServer commit `51737e2c2ee892bc7800df479517eaaf38a828fc`.
- Do not modify AssettoServer core in P0.3.
- Do not edit server configuration files automatically.
- Do not alter global AI parameters or ordinary traffic slots.
- Do not call `AiState.Teleport`, write target speed, inspect splines, or choose junctions.
- Never fall back to a different AI slot when `PoliceCarSessionId` is invalid.
- A police slot with more than one existing state is rejected; P0.3 must never control multiple states.
- Use tests first for each production behavior and observe the expected RED failure before implementing GREEN.
- Push every completed commit to `origin/main`.

---

### Task 1: Police slot configuration

**Files:**
- Modify: `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`
- Modify: `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs`
- Modify: `src/PoliceChasePlugin/PoliceChasePlugin.csproj`

**Interfaces:**
- Produces: `int PoliceChaseConfiguration.PoliceCarSessionId`, default `-1`.
- Validation: when enabled, Session ID must be between `0` and `254`; disabled configuration accepts `-1`.
- Produces: test assembly access to internal adapters through `InternalsVisibleTo`.

- [ ] **Step 1: Write failing configuration tests**

Add this assertion to `NewConfigurationUsesP0Defaults`:

```csharp
Assert.That(configuration.PoliceCarSessionId, Is.EqualTo(-1));
```

Change `AcceptsValidP0Configuration` to use an operationally valid Session ID:

```csharp
var result = _validator.Validate(new PoliceChaseConfiguration
{
    PoliceCarSessionId = 0
});
```

Add these tests to `PoliceChaseConfigurationValidatorTests`:

```csharp
[TestCase(-1)]
[TestCase(255)]
public void RejectsInvalidPoliceSessionIdWhenEnabled(int sessionId)
{
    var configuration = new PoliceChaseConfiguration
    {
        Enabled = true,
        PoliceCarSessionId = sessionId
    };

    var result = _validator.Validate(configuration);

    Assert.That(result.Errors, Has.Some.Property("PropertyName")
        .EqualTo(nameof(PoliceChaseConfiguration.PoliceCarSessionId)));
}

[Test]
public void AcceptsUnsetPoliceSessionIdWhenDisabled()
{
    var configuration = new PoliceChaseConfiguration
    {
        Enabled = false,
        PoliceCarSessionId = -1
    };

    var result = _validator.Validate(configuration);

    Assert.That(result.IsValid, Is.True);
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter "FullyQualifiedName~PoliceChaseConfigurationTests|FullyQualifiedName~PoliceChaseConfigurationValidatorTests"
```

Expected: compile failure because `PoliceCarSessionId` does not exist.

- [ ] **Step 3: Implement configuration and validation**

Add to `PoliceChaseConfiguration` immediately after `PoliceCarModel`:

```csharp
public int PoliceCarSessionId { get; set; } = -1;
```

Add to the validator constructor:

```csharp
When(configuration => configuration.Enabled, () =>
{
    RuleFor(configuration => configuration.PoliceCarSessionId)
        .InclusiveBetween(0, 254);
});
```

Add to the plugin `.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="PoliceChasePlugin.Tests" />
</ItemGroup>
```

- [ ] **Step 4: Run GREEN**

Run the Task 1 test command again.

Expected: all configuration tests pass.

- [ ] **Step 5: Commit and push**

```powershell
git add src/PoliceChasePlugin/PoliceChaseConfiguration.cs src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs src/PoliceChasePlugin/PoliceChasePlugin.csproj tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs
git commit -m "feat: configure exact police AI slot"
git push origin main
```

---

### Task 2: Event-driven player target acquisition

**Files:**
- Create: `src/PoliceChasePlugin/Players/IPolicePlayerSource.cs`
- Create: `src/PoliceChasePlugin/Players/AssettoServerPlayerSource.cs`
- Create: `src/PoliceChasePlugin/Players/PoliceTargetService.cs`
- Create: `tests/PoliceChasePlugin.Tests/Players/FakePolicePlayerSource.cs`
- Create: `tests/PoliceChasePlugin.Tests/Players/PoliceTargetServiceTests.cs`

**Interfaces:**
- Produces: `PolicePlayerSnapshot(byte SessionId, string Name, bool IsReady)`.
- Produces: `PolicePlayerChange(PolicePlayerChangeKind Kind, PolicePlayerSnapshot Player)`.
- Produces: `IPolicePlayerSource.GetPlayers()`, `Changed`, `Start()`, and `Stop()`.
- Produces: `PoliceTargetService.CurrentTargetSessionId`, `Start()`, and `Stop()`.

- [ ] **Step 1: Define the port types used by tests and production**

Create `IPolicePlayerSource.cs`:

```csharp
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
```

This is a contract-only step; do not create `PoliceTargetService` yet.

- [ ] **Step 2: Add the fake source**

Create `FakePolicePlayerSource.cs`:

```csharp
using PoliceChasePlugin.Players;

namespace PoliceChasePlugin.Tests.Players;

internal sealed class FakePolicePlayerSource : IPolicePlayerSource
{
    private readonly List<PolicePlayerSnapshot> _players = new();

    public event Action<PolicePlayerChange>? Changed;
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }

    public IReadOnlyList<PolicePlayerSnapshot> GetPlayers() => _players.ToArray();

    public void Start() => StartCount++;
    public void Stop() => StopCount++;

    public void Connect(byte id, string name, bool ready = false)
    {
        var player = new PolicePlayerSnapshot(id, name, ready);
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

    public void Disconnect(byte id)
    {
        var player = _players.Single(player => player.SessionId == id);
        _players.Remove(player);
        Changed?.Invoke(new PolicePlayerChange(PolicePlayerChangeKind.Disconnected, player));
    }
}
```

- [ ] **Step 3: Write failing target-service tests**

Create `PoliceTargetServiceTests.cs` with these tests:

```csharp
using PoliceChasePlugin.Players;

namespace PoliceChasePlugin.Tests.Players;

[TestFixture]
public class PoliceTargetServiceTests
{
    [Test]
    public void SelectsLowestReadySessionIdOnStart()
    {
        var source = new FakePolicePlayerSource();
        source.Connect(9, "Nine", ready: true);
        source.Connect(3, "Three", ready: true);
        var service = new PoliceTargetService(source);

        service.Start();

        Assert.That(service.CurrentTargetSessionId, Is.EqualTo(3));
    }

    [Test]
    public void WaitsForFirstUpdateBeforeAcquiringTarget()
    {
        var source = new FakePolicePlayerSource();
        var service = new PoliceTargetService(source);
        service.Start();

        source.Connect(4, "Loading");
        Assert.That(service.CurrentTargetSessionId, Is.Null);

        source.MarkReady(4);
        Assert.That(service.CurrentTargetSessionId, Is.EqualTo(4));
    }

    [Test]
    public void KeepsCurrentTargetWhenAnotherPlayerBecomesReady()
    {
        var source = new FakePolicePlayerSource();
        var service = new PoliceTargetService(source);
        service.Start();
        source.Connect(7, "First", ready: true);
        source.MarkReady(7);

        source.Connect(2, "Later", ready: true);
        source.MarkReady(2);

        Assert.That(service.CurrentTargetSessionId, Is.EqualTo(7));
    }

    [Test]
    public void ReacquiresNextReadyPlayerWhenTargetDisconnects()
    {
        var source = new FakePolicePlayerSource();
        source.Connect(8, "First", ready: true);
        source.Connect(10, "Second", ready: true);
        var service = new PoliceTargetService(source);
        service.Start();

        source.Disconnect(8);

        Assert.That(service.CurrentTargetSessionId, Is.EqualTo(10));
    }

    [Test]
    public void ReturnsToWaitingWhenLastTargetDisconnects()
    {
        var source = new FakePolicePlayerSource();
        source.Connect(1, "Only", ready: true);
        var service = new PoliceTargetService(source);
        service.Start();

        source.Disconnect(1);

        Assert.That(service.CurrentTargetSessionId, Is.Null);
    }

    [Test]
    public void StartAndStopAreIdempotent()
    {
        var source = new FakePolicePlayerSource();
        var service = new PoliceTargetService(source);

        service.Start();
        service.Start();
        service.Stop();
        service.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(source.StartCount, Is.EqualTo(1));
            Assert.That(source.StopCount, Is.EqualTo(1));
        });
    }
}
```

- [ ] **Step 4: Run RED for the missing service**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter PoliceTargetServiceTests
```

Expected: compile failure because `PoliceTargetService` does not exist.

- [ ] **Step 5: Implement the target service**

Create `PoliceTargetService.cs`:

```csharp
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
```

- [ ] **Step 6: Run GREEN for target policy**

Run the Task 2 test command again.

Expected: 6 passing tests.

- [ ] **Step 7: Implement the AssettoServer player adapter**

Create `AssettoServerPlayerSource.cs`:

```csharp
using AssettoServer.Network.Tcp;
using AssettoServer.Server;

namespace PoliceChasePlugin.Players;

internal sealed class AssettoServerPlayerSource : IPolicePlayerSource
{
    private readonly EntryCarManager _entryCarManager;
    private readonly Dictionary<byte, ACTcpClient> _subscribedClients = new();
    private bool _started;

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
        if (!_subscribedClients.TryAdd(client.SessionId, client)) return;
        client.FirstUpdateSent += OnFirstUpdateSent;
    }

    private void Unsubscribe(ACTcpClient client)
    {
        if (!_subscribedClients.Remove(client.SessionId)) return;
        client.FirstUpdateSent -= OnFirstUpdateSent;
    }

    private static PolicePlayerSnapshot Snapshot(ACTcpClient client) =>
        new(client.SessionId, client.Name ?? $"Session {client.SessionId}", client.HasSentFirstUpdate);
}
```

The `ACTcpClient` namespace above is the one declared by the pinned AssettoServer source: `AssettoServer.Network.Tcp`.

- [ ] **Step 8: Build and run all target tests**

```powershell
dotnet build PoliceChasePlugin.sln --no-restore
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter PoliceTargetServiceTests
```

Expected: build succeeds and all target tests pass.

- [ ] **Step 9: Commit and push**

```powershell
git add src/PoliceChasePlugin/Players tests/PoliceChasePlugin.Tests/Players
git commit -m "feat: acquire first ready player target"
git push origin main
```

---

### Task 3: Exact police AI slot preparation

**Files:**
- Create: `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`
- Create: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`
- Create: `src/PoliceChasePlugin/Ai/PoliceAiService.cs`
- Create: `tests/PoliceChasePlugin.Tests/Ai/FakePoliceAiSlotSource.cs`
- Create: `tests/PoliceChasePlugin.Tests/Ai/PoliceAiServiceTests.cs`

**Interfaces:**
- Produces: `PoliceAiSlotInfo(byte SessionId, string Model, AiMode Mode)`.
- Produces: `IPoliceAiState.IsInitialized`.
- Produces: `IPoliceAiSlotSource.GetSlots()` and `PrepareSingleState(byte)`.
- Produces: `PoliceAiService.SelectedSlot`, `SelectedState`, and `Prepare()`.

- [ ] **Step 1: Define the AI port**

Create `IPoliceAiSlotSource.cs`:

```csharp
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
```

- [ ] **Step 2: Add the fake AI source**

Create `FakePoliceAiSlotSource.cs`:

```csharp
using AssettoServer.Server;
using PoliceChasePlugin.Ai;

namespace PoliceChasePlugin.Tests.Ai;

internal sealed record FakePoliceAiState(bool IsInitialized) : IPoliceAiState;

internal sealed class FakePoliceAiSlotSource : IPoliceAiSlotSource
{
    public List<PoliceAiSlotInfo> Slots { get; } = new();
    public List<IPoliceAiState> States { get; } = new();
    public byte? PreparedSessionId { get; private set; }

    public IReadOnlyList<PoliceAiSlotInfo> GetSlots() => Slots;

    public IReadOnlyList<IPoliceAiState> PrepareSingleState(byte sessionId)
    {
        PreparedSessionId = sessionId;
        return States;
    }

    public void AddFixedSlot(byte id, string model) =>
        Slots.Add(new PoliceAiSlotInfo(id, model, AiMode.Fixed));
}
```

- [ ] **Step 3: Write failing AI service tests**

Create `PoliceAiServiceTests.cs`:

```csharp
using AssettoServer.Server;
using PoliceChasePlugin.Ai;

namespace PoliceChasePlugin.Tests.Ai;

[TestFixture]
public class PoliceAiServiceTests
{
    [Test]
    public void PreparesOnlyConfiguredFixedSlot()
    {
        var source = new FakePoliceAiSlotSource();
        source.AddFixedSlot(2, "traffic");
        source.AddFixedSlot(7, "police");
        source.States.Add(new FakePoliceAiState(false));
        var service = Create(source, 7, "police");

        service.Prepare();

        Assert.Multiple(() =>
        {
            Assert.That(source.PreparedSessionId, Is.EqualTo(7));
            Assert.That(service.SelectedSlot?.SessionId, Is.EqualTo(7));
            Assert.That(service.SelectedState?.IsInitialized, Is.False);
        });
    }

    [Test]
    public void RejectsMissingSlotWithoutFallback()
    {
        var source = new FakePoliceAiSlotSource();
        source.AddFixedSlot(2, "traffic");
        var service = Create(source, 7, "police");

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("slot 7 was not found"));
            Assert.That(source.PreparedSessionId, Is.Null);
        });
    }

    [Test]
    public void RejectsSlotThatIsNotFixed()
    {
        var source = new FakePoliceAiSlotSource();
        source.Slots.Add(new PoliceAiSlotInfo(7, "police", AiMode.Auto));
        var service = Create(source, 7, "police");

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare());

        Assert.That(exception!.Message, Does.Contain("not configured as AI=FIXED"));
    }

    [Test]
    public void RejectsConfiguredModelMismatch()
    {
        var source = new FakePoliceAiSlotSource();
        source.AddFixedSlot(7, "actual");
        var service = Create(source, 7, "expected");

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare());

        Assert.That(exception!.Message, Does.Contain("model mismatch"));
    }

    [Test]
    public void EmptyConfiguredModelDoesNotRejectSlot()
    {
        var source = new FakePoliceAiSlotSource();
        source.AddFixedSlot(7, "police");
        source.States.Add(new FakePoliceAiState(true));
        var service = Create(source, 7, "");

        service.Prepare();

        Assert.That(service.SelectedState?.IsInitialized, Is.True);
    }

    [TestCase(0)]
    [TestCase(2)]
    public void RejectsStateCountOtherThanExactlyOne(int stateCount)
    {
        var source = new FakePoliceAiSlotSource();
        source.AddFixedSlot(7, "police");
        for (var index = 0; index < stateCount; index++)
            source.States.Add(new FakePoliceAiState(false));
        var service = Create(source, 7, "police");

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare());

        Assert.That(exception!.Message, Does.Contain("exactly one state"));
    }

    private static PoliceAiService Create(
        FakePoliceAiSlotSource source,
        int sessionId,
        string model) => new(source, new PoliceChaseConfiguration
    {
        PoliceCarSessionId = sessionId,
        PoliceCarModel = model
    });
}
```

- [ ] **Step 4: Run RED for missing AI service**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter PoliceAiServiceTests
```

Expected: compile failure because `PoliceAiService` does not exist.

- [ ] **Step 5: Implement AI slot policy**

Create `PoliceAiService.cs`:

```csharp
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
```

- [ ] **Step 6: Run GREEN for AI policy**

Run the Task 3 test command again.

Expected: 7 passing cases.

- [ ] **Step 7: Implement the real AssettoServer AI adapter**

Create `AssettoServerPoliceAiSlotSource.cs`:

```csharp
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
```

This adapter deliberately calls no other `EntryCar` or `AiState` mutation method.

- [ ] **Step 8: Build and verify forbidden API boundary**

```powershell
dotnet build PoliceChasePlugin.sln --no-restore
rg -n "Teleport|TargetSpeed|MaxSpeedKph|WorldToSpline|Junction" src/PoliceChasePlugin/Ai
```

Expected: build succeeds; grep emits no matches other than configuration/type names outside the adapter. Inspect every match and remove any behavioral scope leak.

- [ ] **Step 9: Commit and push**

```powershell
git add src/PoliceChasePlugin/Ai tests/PoliceChasePlugin.Tests/Ai
git commit -m "feat: prepare exact police AI slot"
git push origin main
```

---

### Task 4: Lifecycle and Autofac wiring

**Files:**
- Modify: `src/PoliceChasePlugin/PoliceChaseModule.cs`
- Modify: `src/PoliceChasePlugin/PoliceChaseService.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseServiceTests.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseModuleTests.cs`

**Interfaces:**
- Consumes: `PoliceAiService.Prepare()` and `PoliceTargetService.Start()/Stop()`.
- Produces: one autostart service whose enabled lifecycle prepares AI, starts target observation, and cleans up observation at shutdown.

Add these imports to both modified test files as needed:

```csharp
using PoliceChasePlugin.Ai;
using PoliceChasePlugin.Players;
using PoliceChasePlugin.Tests.Ai;
using PoliceChasePlugin.Tests.Players;
```

- [ ] **Step 1: Update service tests before production**

Add helpers in `PoliceChaseServiceTests`:

```csharp
private static PoliceAiService CreateAiService()
{
    var source = new FakePoliceAiSlotSource();
    source.AddFixedSlot(7, "police");
    source.States.Add(new FakePoliceAiState(false));
    return new PoliceAiService(source, new PoliceChaseConfiguration
    {
        PoliceCarSessionId = 7,
        PoliceCarModel = "police"
    });
}

private static (PoliceTargetService Service, FakePolicePlayerSource Source) CreateTargetService()
{
    var source = new FakePolicePlayerSource();
    return (new PoliceTargetService(source), source);
}
```

In `EnabledServiceLogsInitializationAndShutdown`, replace construction with:

```csharp
var (targetService, _) = CreateTargetService();
using var service = new PoliceChaseService(
    new PoliceChaseConfiguration { Enabled = true, PoliceCarSessionId = 7 },
    CreateAiService(),
    targetService,
    _lifetime);
```

In `DisabledServiceLogsDisabledStateWithoutInitialization`, replace construction with:

```csharp
var (targetService, _) = CreateTargetService();
using var service = new PoliceChaseService(
    new PoliceChaseConfiguration { Enabled = false },
    CreateAiService(),
    targetService,
    _lifetime);
```

Then add these tests:

```csharp
[Test]
public async Task EnabledServiceStartsAndStopsTargetObservation()
{
    var (targetService, source) = CreateTargetService();
    using var service = new PoliceChaseService(
        new PoliceChaseConfiguration { Enabled = true, PoliceCarSessionId = 7 },
        CreateAiService(),
        targetService,
        _lifetime);

    await service.StartAsync(CancellationToken.None);
    await service.StopAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
        Assert.That(source.StartCount, Is.EqualTo(1));
        Assert.That(source.StopCount, Is.EqualTo(1));
    });
}

[Test]
public async Task DisabledServiceDoesNotPrepareAiOrObservePlayers()
{
    var aiSource = new FakePoliceAiSlotSource();
    var aiService = new PoliceAiService(aiSource, new PoliceChaseConfiguration());
    var (targetService, playerSource) = CreateTargetService();
    using var service = new PoliceChaseService(
        new PoliceChaseConfiguration { Enabled = false },
        aiService,
        targetService,
        _lifetime);

    await service.StartAsync(CancellationToken.None);
    await service.StopAsync(CancellationToken.None);

    Assert.Multiple(() =>
    {
        Assert.That(aiSource.PreparedSessionId, Is.Null);
        Assert.That(playerSource.StartCount, Is.Zero);
    });
}
```

- [ ] **Step 2: Run RED for constructor/lifecycle changes**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter PoliceChaseServiceTests
```

Expected: compile failures because the service constructor does not accept the two new services.

- [ ] **Step 3: Update `PoliceChaseService`**

Add fields and constructor dependencies:

```csharp
private readonly PoliceAiService _policeAiService;
private readonly PoliceTargetService _targetService;

public PoliceChaseService(
    PoliceChaseConfiguration configuration,
    PoliceAiService policeAiService,
    PoliceTargetService targetService,
    IHostApplicationLifetime applicationLifetime) : base(applicationLifetime)
{
    _configuration = configuration;
    _policeAiService = policeAiService;
    _targetService = targetService;
}
```

Replace the enabled section of `ExecuteAsync` with:

```csharp
_policeAiService.Prepare();
_targetService.Start();
Log.Information("[PoliceChase] Plugin initialized");

try
{
    await Task.Delay(Timeout.Infinite, stoppingToken);
}
catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
{
    // Normal host shutdown.
}
finally
{
    _targetService.Stop();
    Log.Information("[PoliceChase] Plugin stopping");
}
```

Keep the disabled early return unchanged.

- [ ] **Step 4: Run GREEN for lifecycle tests**

Run the Task 4 test command again.

Expected: all lifecycle tests pass.

- [ ] **Step 5: Write failing module registration assertions**

Update the module test to register fake ports after `RegisterModule`:

```csharp
var playerSource = new FakePolicePlayerSource();
var aiSource = new FakePoliceAiSlotSource();
aiSource.AddFixedSlot(7, "police");
aiSource.States.Add(new FakePoliceAiState(false));

builder.RegisterModule(new PoliceChaseModule());
builder.RegisterInstance<IPolicePlayerSource>(playerSource);
builder.RegisterInstance<IPoliceAiSlotSource>(aiSource);
```

Use a configuration with `PoliceCarSessionId = 7`, then add:

```csharp
Assert.That(container.Resolve<PoliceTargetService>(), Is.Not.Null);
Assert.That(container.Resolve<PoliceAiService>(), Is.Not.Null);
```

- [ ] **Step 6: Run RED for missing registrations**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter PoliceChaseModuleTests
```

Expected: resolution failure because the new adapters/services are not registered.

- [ ] **Step 7: Register the P0.3 services**

Add these namespaces to `PoliceChaseModule.cs`:

```csharp
using PoliceChasePlugin.Ai;
using PoliceChasePlugin.Players;
```

Add these registrations in `PoliceChaseModule.Load` before the autostart registration:

```csharp
builder.RegisterType<AssettoServerPlayerSource>()
    .As<IPolicePlayerSource>()
    .SingleInstance();
builder.RegisterType<PoliceTargetService>().SingleInstance();
builder.RegisterType<AssettoServerPoliceAiSlotSource>()
    .As<IPoliceAiSlotSource>()
    .SingleInstance();
builder.RegisterType<PoliceAiService>().SingleInstance();
```

- [ ] **Step 8: Run GREEN for module and complete suite**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter "FullyQualifiedName~PoliceChaseServiceTests|FullyQualifiedName~PoliceChaseModuleTests"
dotnet test PoliceChasePlugin.sln --no-restore
```

Expected: lifecycle/module tests pass, then the entire suite passes.

- [ ] **Step 9: Commit and push**

```powershell
git add src/PoliceChasePlugin/PoliceChaseModule.cs src/PoliceChasePlugin/PoliceChaseService.cs tests/PoliceChasePlugin.Tests/PoliceChaseServiceTests.cs tests/PoliceChasePlugin.Tests/PoliceChaseModuleTests.cs
git commit -m "feat: wire P0.3 police acquisition lifecycle"
git push origin main
```

---

### Task 5: Operator guide and P0.3 gate

**Files:**
- Create: `docs/P0.3-deteccao-e-viatura.md`
- Modify: `README.md`

**Interfaces:**
- Produces: exact manual `entry_list.ini`/YAML instructions and observable logs.
- Produces: final evidence that P0.3 stays outside speed/spline/routing scope.

- [ ] **Step 1: Write the operator guide**

Create `docs/P0.3-deteccao-e-viatura.md` with:

````markdown
# Police Chase P0.3 — detecção e viatura

## Slot policial

Escolha um índice livre em `entry_list.ini`. O índice de `[CAR_N]` é o `PoliceCarSessionId`.

Adapte manualmente este exemplo, preservando todos os slots existentes:

```ini
[CAR_N]
MODEL=nome_do_carro_policial
SKIN=nome_da_skin
AI=FIXED
```

Não remova nem reutilize um slot do tráfego comum. Reinicie o servidor após alterar `entry_list.ini`.

Para impedir que o lifecycle nativo crie mais de uma instância antes do plugin carregar, acrescente ao `AiParams.CarSpecificOverrides` já existente em `extra_cfg.yml` uma entrada para esse modelo. Preserve todas as entradas atuais:

```yaml
AiParams:
  CarSpecificOverrides:
    - Model: nome_do_carro_policial
      MaxOverbooking: 1
```

## Configuração do plugin

No documento `!PoliceChaseConfiguration`, configure:

```yaml
Enabled: true
PoliceCarSessionId: N
PoliceCarModel: "nome_do_carro_policial"
```

`PoliceCarModel` pode ficar vazio, mas preenchê-lo protege contra um Session ID configurado incorretamente.

## Logs esperados

Na inicialização:

```text
[PoliceChase] Police slot prepared: N (nome_do_carro_policial)
[PoliceChase] Police AI initialized
```

ou, antes do spawn nativo:

```text
[PoliceChase] Police AI waiting for native spawn
```

Quando um jogador terminar de carregar:

```text
[PoliceChase] Player connected: <nome> (<id>)
[PoliceChase] Target acquired: <nome> (<id>)
```

Ao desconectar o alvo:

```text
[PoliceChase] Target released: <nome> (<id>)
```

## Critérios de validação

1. O servidor inicia sem exceções contínuas.
2. RandomWeatherPlugin e AI Traffic continuam funcionando.
3. Exatamente o slot configurado é preparado.
4. O primeiro jogador pronto vira alvo e permanece até desconectar.
5. Um segundo jogador vira alvo somente depois da saída do primeiro.
6. Nenhuma velocidade, spline ou rota é alterada pela P0.3.
7. Com `Enabled: false`, o plugin não prepara slot nem observa jogadores.

O `AiBehavior` nativo ainda pode reduzir o overbooking do slot depois da inicialização. A reserva permanente será tratada na P0.4 por uma extensão mínima do core; não tente compensar alterando parâmetros globais.
````

- [ ] **Step 2: Link the guide in README**

Add under the P0 links:

```markdown
- [P0.3 — Detecção de jogador e seleção da viatura](docs/P0.3-deteccao-e-viatura.md)
```

- [ ] **Step 3: Run fresh full verification**

```powershell
dotnet restore PoliceChasePlugin.sln
dotnet test PoliceChasePlugin.sln --no-restore
dotnet build PoliceChasePlugin.sln -c Release --no-restore
git diff --check
```

Expected: restore succeeds; every test passes; Release build has 0 errors; diff check has no errors.

- [ ] **Step 4: Verify exact dependency, package, and scope**

```powershell
$expectedCommit = '51737e2c2ee892bc7800df479517eaaf38a828fc'
$actualCommit = git -C external/AssettoServer rev-parse HEAD
if ($actualCommit -ne $expectedCommit) { throw "AssettoServer commit mismatch: $actualCommit" }

$packageFiles = @(Get-ChildItem artifacts/PoliceChasePlugin -File)
if ($packageFiles.Count -ne 1 -or $packageFiles[0].Name -ne 'PoliceChasePlugin.dll') {
    throw "Unexpected plugin package contents: $($packageFiles.Name -join ', ')"
}

$forbidden = @(rg -n "Teleport|TargetSpeed|WorldToSpline|JunctionEvaluator|Wanted|Heat" src/PoliceChasePlugin)
if ($forbidden.Count -gt 0) { throw "P0.4+ scope detected: $($forbidden -join '; ')" }
```

Expected: exact hash, one DLL, and no forbidden behavior.

- [ ] **Step 5: Commit and push documentation**

```powershell
git add README.md docs/P0.3-deteccao-e-viatura.md
git commit -m "docs: add P0.3 server validation guide"
git push origin main
```

- [ ] **Step 6: Verify local/remote synchronization**

```powershell
git status --short --branch
git rev-parse HEAD
git ls-remote origin refs/heads/main
```

Expected: clean `main...origin/main`; local and remote hashes match. Stop and request operator validation before P0.4.
