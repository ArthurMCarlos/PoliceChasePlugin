# Police Chase P0.5 Basic Pursuit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the dedicated `CAR_12` physically pursuing the selected player through bounded spline branches, with individual catch-up speed and release only after the target is disconnected or more than 1,500 metres away.

**Architecture:** Add opt-in pursuit primitives to the pinned AssettoServer core: a bounded directed route planner, explicit per-state junction decisions, an immutable pursuit snapshot, individual requested speed, and a lifecycle-retention predicate. The plugin owns orchestration through testable adapters and a 200 ms controller; all core behavior remains native when a state has no pursuit snapshot.

**Tech Stack:** C# 12, .NET 8, NUnit 3, FluentValidation, Autofac, AssettoServer `0.0.54` fork.

**Spec:** `docs/superpowers/specs/2026-09-17-police-chase-p0-5-basic-pursuit-design.md`

## Global Constraints

- Keep exactly one reserved state for `PoliceCarSessionId=12` using the P0.4 minimum-overbooking mechanism.
- Do not change global `PlayerRadiusMeters`, `MaxSpeedKph`, `TrafficDensity`, `AiPerPlayerTargetCount`, or `MaxAiTargetCount`.
- Do not use reflection or access private core state from the plugin.
- Do not command a teleport; initial placement and post-release recycling remain native.
- `CAR_0..CAR_9` retain their current traffic lifecycle and speed behavior.
- `CAR_10` and `CAR_11` remain unaffected player slots.
- Default pursuit configuration is 1,500 m maximum distance, 50 m desired distance, 180 km/h maximum speed, and 200 ms update interval.
- Native curve, obstacle, collision, braking, and acceleration constraints remain authoritative.
- No siren, lights, Heat/Wanted, HUD, PIT, multiple police cars, reverse navigation, or global map routing.
- Use strict RED → GREEN → REFACTOR for every production behavior.
- Push every core commit to `ArthurMCarlos/AssettoServer` and every parent-project commit to `ArthurMCarlos/PoliceChasePlugin`.

## File Structure

### AssettoServer submodule

- Create `AssettoServer/Server/Ai/Routing/AiRouteGraph.cs`: immutable graph node and edge contracts used by the pure search.
- Create `AssettoServer/Server/Ai/Routing/AiRoutePlanner.cs`: bounded Dijkstra search and spline adapter.
- Create `AssettoServer/Server/Ai/AiPursuitControl.cs`: immutable pursuit snapshot, update status, and pure lifecycle/speed helpers.
- Modify `AssettoServer/Server/Ai/Splines/JunctionEvaluator.cs`: optional atomic explicit decisions.
- Modify `AssettoServer/Server/Ai/AiState.cs`: typed track/update/release API and requested-speed integration.
- Modify `AssettoServer/Server/Ai/AiBehavior.cs`: skip native recycling only for a retained pursuit state.
- Create corresponding tests under `AssettoServer.Tests/Server/Ai/`.

### PoliceChasePlugin parent repository

- Modify `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`: replace unused preliminary chase knobs with the four approved P0.5 settings.
- Modify `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs`: validate the four settings and their ordering.
- Create `src/PoliceChasePlugin/Pursuit/PolicePursuitSpeedPolicy.cs`: pure desired-speed calculation.
- Create `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs`: deterministic `UpdateOnce()` plus periodic runner and transition logging.
- Modify `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`: typed pursuit operations on the selected state.
- Modify `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`: bridge the plugin contract to the new core API.
- Modify `src/PoliceChasePlugin/PoliceChaseModule.cs` and `PoliceChaseService.cs`: register, run, and stop pursuit orchestration.
- Add focused tests under `tests/PoliceChasePlugin.Tests/Pursuit/` and update existing configuration, adapter, and service tests.
- Create `docs/P0.5-perseguicao-basica.md` and link it from `README.md`.

---

### Task 1: Create the bounded spline route planner

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiRouteGraph.cs`
- Create: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiRoutePlanner.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiRoutePlannerTests.cs`

**Interfaces:**
- Produces: `AiRouteGraph`, `AiRouteEdge`, `AiRoutePlan`, and `AiRoutePlanner.TryPlan(int startPointId, IReadOnlySet<int> targetPointIds, float maxDistanceMeters)`.
- `AiRouteEdge` carries `ToPointId`, `LengthMeters`, optional `JunctionId`, and optional `TakeBranch`.
- `AiRoutePlan` carries `DistanceMeters` and `IReadOnlyDictionary<int, bool> JunctionDecisions`.

- [ ] **Step 1: Create a core branch and verify the pinned baseline**

Run:

```powershell
git -C external/AssettoServer switch -c codex/p0.5-basic-pursuit
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
```

Expected: branch starts at `c2d9d1e360071bd5ea54254b6bf3c21c7681bdd9`; 14 tests pass.

- [ ] **Step 2: Write failing literal graph tests**

Add tests that construct `AiRouteGraph` directly:

```csharp
[Test]
public void ChoosesBranchThatReachesTarget()
{
    var graph = new AiRouteGraph(new Dictionary<int, AiRouteEdge[]>
    {
        [0] = [new(1, 10, 4, false), new(2, 12, 4, true)],
        [1] = [new(3, 10, null, null)],
        [2] = [new(5, 8, null, null)],
        [3] = [],
        [5] = []
    });

    var plan = new AiRoutePlanner(graph).TryPlan(0, new HashSet<int> { 5 }, 100);

    Assert.Multiple(() =>
    {
        Assert.That(plan, Is.Not.Null);
        Assert.That(plan!.DistanceMeters, Is.EqualTo(20));
        Assert.That(plan.JunctionDecisions, Is.EqualTo(new Dictionary<int, bool> { [4] = true }));
    });
}

[Test]
public void RejectsPathLongerThanMaximumDistance()
{
    var graph = new AiRouteGraph(new Dictionary<int, AiRouteEdge[]>
    {
        [0] = [new(1, 800, null, null)],
        [1] = [new(2, 800, null, null)],
        [2] = []
    });

    Assert.That(new AiRoutePlanner(graph).TryPlan(0, new HashSet<int> { 2 }, 1500), Is.Null);
}
```

Also cover a direct path, unreachable target, invalid start, and an adjacent-lane target set containing more than one acceptable point.

- [ ] **Step 3: Run RED**

Run:

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore --filter FullyQualifiedName~AiRoutePlannerTests
```

Expected: compile failure because `AiRouteGraph`, `AiRouteEdge`, and `AiRoutePlanner` do not exist.

- [ ] **Step 4: Implement the pure bounded search and spline adapter**

Use these exact public shapes:

```csharp
public readonly record struct AiRouteEdge(
    int ToPointId,
    float LengthMeters,
    int? JunctionId,
    bool? TakeBranch);

public sealed record AiRoutePlan(
    float DistanceMeters,
    IReadOnlyDictionary<int, bool> JunctionDecisions);

public sealed class AiRoutePlanner
{
    public AiRoutePlanner(AiSpline spline);
    internal AiRoutePlanner(AiRouteGraph graph);
    public AiRoutePlan? TryPlan(
        int startPointId,
        IReadOnlySet<int> targetPointIds,
        float maxDistanceMeters);
}
```

Build production edges from `SplinePoint.NextId` and, when `JunctionStartId >= 0`, `SplineJunction.EndPointId`. Use segment `Length` as non-negative cost, `PriorityQueue<int,float>`, predecessor records, and stop expanding a candidate whose accumulated distance exceeds the supplied maximum. Reconstruct only the junction decisions on the winning path.

- [ ] **Step 5: Run GREEN and the complete core suite**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore --filter FullyQualifiedName~AiRoutePlannerTests
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
```

Expected: all route tests and all existing core tests pass.

- [ ] **Step 6: Commit and push the core task**

```powershell
git -C external/AssettoServer add AssettoServer/Server/Ai/Routing AssettoServer.Tests/Server/Ai/AiRoutePlannerTests.cs
git -C external/AssettoServer commit -m "feat(ai): add bounded spline route planner"
git -C external/AssettoServer push -u arthur codex/p0.5-basic-pursuit
```

### Task 2: Add explicit per-state junction decisions

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Splines/JunctionEvaluator.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/JunctionEvaluatorTests.cs`

**Interfaces:**
- Produces: `JunctionEvaluator.SetExplicitDecisions(IReadOnlyDictionary<int, bool>? decisions)`.
- Consumes: `AiRoutePlan.JunctionDecisions` from Task 1.

- [ ] **Step 1: Write failing evaluator tests**

Add an internal probability-function constructor so tests need no memory-mapped spline, then write:

```csharp
[Test]
public void ExplicitDecisionWinsProbabilityAndClearingRestoresNativeChoice()
{
    var evaluator = new JunctionEvaluator(_ => 0.0f, savesState: true);
    evaluator.SetExplicitDecisions(new Dictionary<int, bool> { [7] = true });

    Assert.That(evaluator.WillTakeJunction(7), Is.True);

    evaluator.SetExplicitDecisions(null);
    Assert.That(evaluator.WillTakeJunction(7), Is.False);
}
```

Also verify an override for junction 7 does not affect junction 8.

- [ ] **Step 2: Run RED**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore --filter FullyQualifiedName~JunctionEvaluatorTests
```

Expected: compile failure for the missing constructor and `SetExplicitDecisions`.

- [ ] **Step 3: Implement atomic decision snapshots**

Add:

```csharp
private IReadOnlyDictionary<int, bool>? _explicitDecisions;
private readonly Func<int, float> _getProbability;

public void SetExplicitDecisions(IReadOnlyDictionary<int, bool>? decisions) =>
    Volatile.Write(ref _explicitDecisions,
        decisions == null ? null : new Dictionary<int, bool>(decisions));
```

`WillTakeJunction()` must first read the snapshot and return its value when present; otherwise it uses the existing saved/random behavior. The public `AiSpline` constructor supplies `junctionId => _spline.Junctions[junctionId].Probability` to the same implementation.

- [ ] **Step 4: Run GREEN, full core tests, commit, and push**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
git -C external/AssettoServer add AssettoServer/Server/Ai/Splines/JunctionEvaluator.cs AssettoServer.Tests/Server/Ai/JunctionEvaluatorTests.cs
git -C external/AssettoServer commit -m "feat(ai): allow explicit junction decisions"
git -C external/AssettoServer push
```

### Task 3: Add typed pursuit state, tracking, and individual speed

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiState.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitControlTests.cs`

**Interfaces:**
- Produces: `AiPursuitTrackingStatus`, `AiPursuitTrackingResult`, `AiState.TrackPursuit(...)`, `AiState.SetPursuitDesiredSpeed(float)`, `AiState.ReleasePursuit()`, and `AiState.ShouldRetainPursuit`.
- Consumes: route planner and junction decisions from Tasks 1–2.

- [ ] **Step 1: Write failing pure policy tests**

Create tests for exact retention and speed behavior:

```csharp
[TestCase(1499, 1500, true)]
[TestCase(1500, 1500, true)]
[TestCase(1501, 1500, false)]
public void RetentionUsesPursuitMaximumDistance(float distance, float maximum, bool expected) =>
    Assert.That(AiPursuitControl.ShouldRetain(distance * distance, maximum), Is.EqualTo(expected));

[Test]
public void RequestedSpeedOverridesTrafficSpeedButSafetyLimitsCanReduceIt()
{
    Assert.That(AiPursuitControl.ResolveRequestedSpeed(80 / 3.6f, 180 / 3.6f), Is.EqualTo(180 / 3.6f).Within(0.001));
    Assert.That(AiPursuitControl.ApplySafetyLimit(180 / 3.6f, 90 / 3.6f), Is.EqualTo(90 / 3.6f).Within(0.001));
}
```

Also test null requested speed returns native initial speed, negative/NaN values are rejected, and release clears retention.

- [ ] **Step 2: Run RED**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore --filter FullyQualifiedName~AiPursuitControlTests
```

Expected: compile failure because the pursuit API does not exist.

- [ ] **Step 3: Implement immutable snapshots and tracking API**

Use:

```csharp
public enum AiPursuitTrackingStatus
{
    Active,
    WaitingForSpawn,
    RouteTemporarilyUnavailable,
    MaxDistanceExceeded,
    NoRoute
}

public sealed record AiPursuitTrackingResult(
    AiPursuitTrackingStatus Status,
    float? RouteDistanceMeters,
    float TargetSpeedMetersPerSecond);

internal sealed record AiPursuitSnapshot(
    byte TargetSessionId,
    Vector3 TargetPosition,
    float MaxDistanceMeters,
    AiRoutePlan Route,
    float? DesiredSpeedMetersPerSecond);
```

Add these `AiState` methods:

```csharp
public AiPursuitTrackingResult TrackPursuit(EntryCar target, float maxDistanceMeters);
public void SetPursuitDesiredSpeed(float metersPerSecond);
public void ReleasePursuit();
public bool ShouldRetainPursuit { get; }
```

`TrackPursuit` must:

1. return `WaitingForSpawn` when `Initialized == false`;
2. compare spatial distance with the maximum and release/return `MaxDistanceExceeded` when over it;
3. call `WorldToSpline(target.Status.Position)` and preserve the existing snapshot with updated target position when the target is farther from the spline than `MaxPlayerDistanceToAiSplineSquared`, returning `RouteTemporarilyUnavailable`;
4. build acceptable target IDs from the target point plus `AiSpline.GetLanes(targetPoint)` in the same direction;
5. call `AiRoutePlanner.TryPlan(CurrentSplinePointId, targets, maxDistanceMeters)`;
6. release/return `NoRoute` when no bounded path exists;
7. atomically publish the new snapshot, set explicit junction decisions, and return `Active` with literal route distance and target velocity length.

`ReleasePursuit()` atomically clears the snapshot and calls `SetExplicitDecisions(null)`.

- [ ] **Step 4: Integrate the speed request without bypassing safety**

In `DetectObstacles()`, replace the initial `InitialMaxSpeed` values with:

```csharp
var pursuit = Volatile.Read(ref _pursuit);
float requestedSpeed = AiPursuitControl.ResolveRequestedSpeed(
    InitialMaxSpeed,
    pursuit?.DesiredSpeedMetersPerSecond);
float targetSpeed = requestedSpeed;
float maxSpeed = requestedSpeed;
```

Keep every existing obstacle and curve branch. Continue applying `Math.Min(splineLookahead.MaxSpeed, targetSpeed)`. For engine RPM, use the pursuit request as the reference only while pursuit is present so normal traffic math is unchanged.

- [ ] **Step 5: Run GREEN and full core suite**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore --filter FullyQualifiedName~AiPursuitControlTests
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
```

- [ ] **Step 6: Commit and push**

```powershell
git -C external/AssettoServer add AssettoServer/Server/Ai/AiPursuitControl.cs AssettoServer/Server/Ai/AiState.cs AssettoServer.Tests/Server/Ai/AiPursuitControlTests.cs
git -C external/AssettoServer commit -m "feat(ai): add per-state pursuit control"
git -C external/AssettoServer push
```

### Task 4: Prevent native recycling only for a retained pursuit

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiBehavior.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiSpatialLifecycleTests.cs`

**Interfaces:**
- Consumes: `AiState.ShouldRetainPursuit` from Task 3.
- Produces: internal pure `AiSpatialLifecycle.ShouldQueueForReposition(...)` used by `AiBehavior.Update()`.

- [ ] **Step 1: Write failing lifecycle matrix tests**

```csharp
[TestCase(40_001, 40_000, 9_000, 8_000, false, true)]
[TestCase(40_001, 40_000, 9_000, 8_000, true, false)]
[TestCase(39_999, 40_000, 9_000, 8_000, false, false)]
[TestCase(40_001, 40_000, 7_999, 8_000, false, false)]
public void QueueDecisionPreservesNativeRulesExceptRetainedPursuit(
    float distanceSquared, float radiusSquared, long now, long protectionEnds,
    bool retainPursuit, bool expected)
{
    Assert.That(AiSpatialLifecycle.ShouldQueueForReposition(
        distanceSquared, radiusSquared, now, protectionEnds, retainPursuit), Is.EqualTo(expected));
}
```

- [ ] **Step 2: Run RED**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore --filter FullyQualifiedName~AiSpatialLifecycleTests
```

Expected: compile failure for missing `AiSpatialLifecycle`.

- [ ] **Step 3: Implement and wire the single lifecycle predicate**

Create the pure helper in `AiPursuitControl.cs` or a focused `AiSpatialLifecycle.cs`, then replace lines 249–255 of `AiBehavior.Update()` with a call using `dist.Key.ShouldRetainPursuit`. Do not modify list ordering, player selection, spawn safety, or traffic calculations.

- [ ] **Step 4: Run full tests and Release build**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
dotnet build external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release --no-restore
```

Expected: all tests pass; zero build errors.

- [ ] **Step 5: Commit and push**

```powershell
git -C external/AssettoServer add AssettoServer/Server/Ai/AiBehavior.cs AssettoServer/Server/Ai/AiPursuitControl.cs AssettoServer.Tests/Server/Ai/AiSpatialLifecycleTests.cs
git -C external/AssettoServer commit -m "feat(ai): retain active pursuit state"
git -C external/AssettoServer push
```

### Task 5: Replace preliminary chase configuration and add speed policy

**Files:**
- Modify: `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`
- Modify: `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs`
- Create: `src/PoliceChasePlugin/Pursuit/PolicePursuitSpeedPolicy.cs`
- Create: `tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitSpeedPolicyTests.cs`

**Interfaces:**
- Produces: the four approved configuration properties and `PolicePursuitSpeedPolicy.Calculate(float targetSpeedMs, float routeDistanceMeters, float desiredDistanceMeters, float maximumSpeedKph)`.

- [ ] **Step 1: Write failing default and validation tests**

Assert exact defaults `1500`, `50`, `180`, `200`. Add invalid cases for zero/negative values and `PursuitDesiredDistanceMeters >= PursuitMaxDistanceMeters`.

```csharp
Assert.Multiple(() =>
{
    Assert.That(configuration.PursuitMaxDistanceMeters, Is.EqualTo(1500));
    Assert.That(configuration.PursuitDesiredDistanceMeters, Is.EqualTo(50));
    Assert.That(configuration.PursuitMaxSpeedKph, Is.EqualTo(180));
    Assert.That(configuration.PursuitUpdateIntervalMilliseconds, Is.EqualTo(200));
});
```

- [ ] **Step 2: Write failing speed-policy tests**

```csharp
[TestCase(50, 20, 50, 180, 20)]
[TestCase(150, 20, 50, 180, 45)]
[TestCase(1000, 60, 50, 180, 180)]
public void CalculatesProgressiveCappedSpeed(
    float distance, float targetKph, float desiredDistance, float maximumKph, float expectedKph)
{
    var actual = PolicePursuitSpeedPolicy.Calculate(
        targetKph / 3.6f, distance, desiredDistance, maximumKph);
    Assert.That(actual * 3.6f, Is.EqualTo(expectedKph).Within(0.01));
}
```

Use the exact formula `targetSpeedKph + max(0, routeDistance - desiredDistance) * 0.25`, capped at `maximumSpeedKph`. This makes the expected 150 m case `20 + 100 * 0.25 = 45`.

- [ ] **Step 3: Run RED**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter "FullyQualifiedName~PoliceChaseConfiguration|FullyQualifiedName~PolicePursuitSpeedPolicy"
```

Expected: failures for missing properties and type.

- [ ] **Step 4: Implement minimal configuration and policy**

Remove the unused preliminary fields `MaxPoliceSpeedKph`, distance bands, speed bonuses, `LostDistanceMeters`, `Debug`, and `DebugIntervalMs`. Add only:

```csharp
public float PursuitMaxDistanceMeters { get; set; } = 1500;
public float PursuitDesiredDistanceMeters { get; set; } = 50;
public float PursuitMaxSpeedKph { get; set; } = 180;
public int PursuitUpdateIntervalMilliseconds { get; set; } = 200;
```

Implement the literal formula above and reject non-finite arguments with `ArgumentOutOfRangeException`.

- [ ] **Step 5: Run GREEN, full plugin suite, commit, and push**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore
git add src/PoliceChasePlugin/PoliceChaseConfiguration.cs src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs src/PoliceChasePlugin/Pursuit tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitSpeedPolicyTests.cs
git commit -m "feat: define P0.5 pursuit policy"
git push origin main
```

### Task 6: Bridge the selected police state to the core pursuit API

**Files:**
- Modify: `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`
- Modify: `tests/PoliceChasePlugin.Tests/Ai/FakePoliceAiSlotSource.cs`
- Modify: `tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiSlotSourceTests.cs`

**Interfaces:**
- Produces: plugin `PolicePursuitTrackingStatus`, `PolicePursuitTrackingResult`, and methods on `IPoliceAiState` named `TrackPursuit`, `SetDesiredSpeed`, and `ReleasePursuit`.
- Consumes: core `AiState.TrackPursuit`, `SetPursuitDesiredSpeed`, and `ReleasePursuit` from Task 3.
- Adds an internal `INativePolicePursuitState` seam so the production mapping is exercised without constructing AssettoServer's full session graph.

- [ ] **Step 1: Write failing adapter contract tests**

Add a fake `INativePolicePursuitState` with recorded calls and verify the public wrapper delegates only the requested target session:

```csharp
var result = state.TrackPursuit(targetSessionId: 10, maxDistanceMeters: 1500);
state.SetDesiredSpeed(50);
state.ReleasePursuit();

Assert.Multiple(() =>
{
    Assert.That(result.Status, Is.EqualTo(PolicePursuitTrackingStatus.Active));
    Assert.That(native.TargetRequests, Is.EqualTo(new[] { (byte)10 }));
    Assert.That(native.SpeedRequests, Is.EqualTo(new[] { 50f }));
    Assert.That(native.ReleaseCount, Is.EqualTo(1));
});
```

Add a test returning `TargetUnavailable` without calling core when session 10 has disconnected.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter FullyQualifiedName~AssettoServerPoliceAiSlotSourceTests
```

- [ ] **Step 3: Implement the typed adapter**

Use this plugin contract:

```csharp
public enum PolicePursuitTrackingStatus
{
    Active,
    WaitingForSpawn,
    RouteTemporarilyUnavailable,
    MaxDistanceExceeded,
    NoRoute,
    TargetUnavailable
}

public sealed record PolicePursuitTrackingResult(
    PolicePursuitTrackingStatus Status,
    float? RouteDistanceMeters,
    float TargetSpeedMetersPerSecond);

public interface IPoliceAiState
{
    bool IsInitialized { get; }
    PolicePursuitTrackingResult TrackPursuit(byte targetSessionId, float maxDistanceMeters);
    void SetDesiredSpeed(float metersPerSecond);
    void ReleasePursuit();
}
```

`AssettoServerPoliceAiState` receives `Func<byte, EntryCar?>`; map the core enum exhaustively and do not catch core exceptions. Preserve the lazy `EntryCarManager.EntryCars` access introduced in P0.4.

Use this internal seam and production adapter:

```csharp
internal interface INativePolicePursuitState
{
    bool IsInitialized { get; }
    PolicePursuitTrackingResult TrackPursuit(byte targetSessionId, float maxDistanceMeters);
    void SetDesiredSpeed(float metersPerSecond);
    void ReleasePursuit();
}

internal sealed class AssettoServerNativePolicePursuitState : INativePolicePursuitState
{
    // Holds AiState plus a lazy Func<byte, EntryCar?> resolver and maps the core result exhaustively.
}
```

`AssettoServerPoliceAiState` delegates the public plugin contract to `INativePolicePursuitState`. The production slot wrapper creates `AssettoServerNativePolicePursuitState` for each real state.

- [ ] **Step 4: Run GREEN, full plugin suite, commit, and push**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore
git add src/PoliceChasePlugin/Ai tests/PoliceChasePlugin.Tests/Ai
git commit -m "feat: bridge police pursuit state"
git push origin main
```

### Task 7: Implement and wire the periodic pursuit service

**Files:**
- Create: `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs`
- Create: `tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs`
- Modify: `src/PoliceChasePlugin/PoliceChaseModule.cs`
- Modify: `src/PoliceChasePlugin/PoliceChaseService.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseServiceTests.cs`

**Interfaces:**
- Consumes: target session from `PoliceTargetService`, selected state from `PoliceAiService`, adapter result from Task 6, and speed policy from Task 5.
- Produces: `PolicePursuitService.UpdateOnce()`, `RunAsync(CancellationToken)`, and `Release()`.

- [ ] **Step 1: Write failing deterministic controller tests**

Cover these separate behaviors with literal fake results:

```csharp
[Test]
public void ActiveRouteRequestsCalculatedSpeed()
{
    var state = new FakePoliceAiState(true)
    {
        NextTrackingResult = new(PolicePursuitTrackingStatus.Active, 150, 20 / 3.6f)
    };
    var service = CreateService(state, targetSessionId: 10);

    service.UpdateOnce();

    Assert.That(state.SpeedRequests.Single() * 3.6f, Is.EqualTo(45).Within(0.01));
}
```

Add individual tests for waiting on uninitialized spawn, start transition, temporary route loss without release, route recovery, target change releasing the old pursuit, target disconnect, maximum-distance release, no-route release, idempotent `Release()`, and no repeated transition logs on identical updates.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter FullyQualifiedName~PolicePursuitServiceTests
```

Expected: compile failure because `PolicePursuitService` does not exist.

- [ ] **Step 3: Implement the deterministic state machine**

Use:

```csharp
public sealed class PolicePursuitService
{
    public void UpdateOnce();
    public Task RunAsync(CancellationToken stoppingToken);
    public void Release();
}
```

`RunAsync` owns a `PeriodicTimer` using `PursuitUpdateIntervalMilliseconds` and calls `UpdateOnce`. `UpdateOnce` must never overlap: the single timer loop is its only production caller. Track `_activeTargetSessionId` and `_routeTemporarilyLost` so logs occur only on transitions. On `Active`, calculate and set desired speed. On temporary loss, keep core control and do not issue a higher speed. On `MaxDistanceExceeded`, `NoRoute`, `TargetUnavailable`, target change, or no target, release once and log the reason once.

- [ ] **Step 4: Wire host lifecycle and DI**

Register `PolicePursuitService` as a singleton. Inject it into `PoliceChaseService`; after AI preparation and target start, await `RunAsync(stoppingToken)`. In `finally`, call `Release()` before `_targetService.Stop()`.

Update service tests with a fake or real deterministic pursuit service so disabled mode never starts it and enabled shutdown releases it.

- [ ] **Step 5: Run GREEN, full plugin suite, Release build, commit, and push**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore
dotnet build src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release --no-restore
git add src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs src/PoliceChasePlugin/PoliceChaseModule.cs src/PoliceChasePlugin/PoliceChaseService.cs tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs tests/PoliceChasePlugin.Tests/PoliceChaseServiceTests.cs
git commit -m "feat: run basic police pursuit"
git push origin main
```

### Task 8: Pin the core, document deployment, and perform final verification

**Files:**
- Modify: `external/AssettoServer` gitlink
- Modify: `README.md`
- Create: `docs/P0.5-perseguicao-basica.md`

**Interfaces:**
- Consumes: final pushed core commit from Tasks 1–4 and final plugin behavior from Tasks 5–7.
- Produces: reproducible build/deployment and real-server validation instructions.

- [ ] **Step 1: Verify the final core remote and update the parent gitlink**

```powershell
git -C external/AssettoServer status --short --branch
git -C external/AssettoServer rev-parse HEAD
git -C external/AssettoServer ls-remote arthur refs/heads/codex/p0.5-basic-pursuit
git add external/AssettoServer
```

Expected: clean core branch and identical local/remote hashes.

- [ ] **Step 2: Write the deployment and validation guide**

Document the four YAML properties, requirement to install both the modified AssettoServer and plugin DLL, expected transition logs, the 1,500 m release rule, branch test, no-teleport assertion, and regression checks for traffic/player slots/weather/collisions.

Add this exact README entry:

```markdown
- [P0.5 — Perseguição básica com roteamento limitado](docs/P0.5-perseguicao-basica.md)
```

- [ ] **Step 3: Run fresh complete verification**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore
dotnet build external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release --no-restore
dotnet build src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release --no-restore
git diff --check
```

Confirm both artifacts exist:

```powershell
Test-Path external/AssettoServer/AssettoServer/bin/Release/net8.0/win-x64/AssettoServer.dll
Test-Path artifacts/PoliceChasePlugin/PoliceChasePlugin.dll
```

Search forbidden global/scope changes:

```powershell
git -C external/AssettoServer diff c2d9d1e -- AssettoServer | rg -i "TrafficDensity|AiPerPlayerTargetCount|MaxAiTargetCount|PlayerRadiusMeters"
rg -n -i "siren|wanted|heat|pit|roadblock" src external/AssettoServer/AssettoServer/Server/Ai
```

Expected: no changed global parameter assignments and no out-of-scope feature implementation.

- [ ] **Step 4: Request code review and address only verified findings**

Review the complete parent diff from `88e8c92` and core diff from `c2d9d1e`. Require explicit checks for route-search bounds, atomic snapshot safety, native fallback, obstacle/curve preservation, target disconnect, and no effect on dynamic traffic.

- [ ] **Step 5: Commit docs/gitlink and push parent repository**

```powershell
git add README.md docs/P0.5-perseguicao-basica.md external/AssettoServer
git commit -m "docs: add P0.5 server validation guide"
git push origin main
```

- [ ] **Step 6: Confirm clean synchronized repositories**

```powershell
git status --short --branch
git -C external/AssettoServer status --short --branch
git rev-parse HEAD
git ls-remote origin refs/heads/main
git -C external/AssettoServer rev-parse HEAD
git -C external/AssettoServer ls-remote arthur refs/heads/codex/p0.5-basic-pursuit
```

Expected: both worktrees clean and both local hashes equal their remote refs.
