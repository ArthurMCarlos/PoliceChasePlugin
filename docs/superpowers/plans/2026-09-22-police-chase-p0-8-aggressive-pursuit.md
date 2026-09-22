# Police Chase P0.8 Aggressive Pursuit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dedicated longitudinal police controller that performs aggressive catch-up, controlled close pressure, physical contact, and collision recovery without changing P0.6/P0.7 navigation or normal traffic behavior.

**Architecture:** Implement a pure stateful `AiPursuitDrivingController` in the pinned AssettoServer core. `AiState.TrackPursuit()` feeds it route, physical, speed, and lane-phase inputs; `AiState.DetectObstacles()` applies its bounded request while exempting only the active target from generic player-obstacle following; `AiBehavior.OnCollision()` reports target collisions to recovery. The plugin owns configuration, option mapping, backward-compatible legacy mode, and transition-only diagnostics.

**Tech Stack:** C#/.NET 8, NUnit, FluentValidation, AssettoServer plugin API, immutable records with `Interlocked`/`Volatile`, Serilog, Git submodule.

**Spec:** `docs/superpowers/specs/2026-09-22-police-chase-p0-8-aggressive-pursuit-design.md`

## Global Constraints

- Baseline parent commit: `7edc4732d0de4296c0f5398de557df0089ac1bfa`.
- Baseline AssettoServer commit: `910f4727cc5891b85efc7f2f46c5dc8e2aae05ce`.
- Preserve P0.6 forward-only routing, lane equivalence, grace/probe, and junction decisions.
- Preserve P0.7 target-lane alignment, future-junction preparation, reciprocal adjacency, safety, physical transition, hysteresis, cooldown, and route revision.
- Do not change AI counts, traffic density, player radius, spawn distances, lane widths, route-search budgets, or production splines.
- Do not use reflection, force, damage, teleport, snap, reverse routing, respawn catch-up, or map-specific IDs.
- Only an AI with an active pursuit snapshot for the exact target may use P0.8 target treatment.
- `PursuitMaxSpeedKph` remains the absolute requested-speed cap.
- Disabled P0.8 preserves `PolicePursuitSpeedPolicy` and `PursuitDesiredDistanceMeters`.
- Do not edit the user's local server configuration or deploy to the server.
- P0.8 remains pending real-server validation after local completion.

## Review Focus

- A player other than the pursued target must retain the native 8–12 m hard stop and braking envelope; Task 3 adds an explicit obstacle-classification test.
- A reported collision from a non-target player must retain delayed `StopForCollision()` behavior; Task 4 adds a collision-routing test.
- Invalid or stale physical measurements must produce a bounded conservative request rather than NaN or unlimited speed; Task 2 adds finite-input and cap tests.
- A temporary route loss during `Contact` or `Recovery` must retain the last bounded request and must not create a new route; Task 4 adds snapshot-retention tests.
- Lane phase changes concurrent with pursuit updates must not reset route state or expose mutable controller state across threads; Tasks 2 and 3 test immutable state carry-forward and route revisions.

---

## File Structure

### AssettoServer core

- Create `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitDrivingController.cs`: pure controller types, validation, state transition, closing-speed envelope, hysteresis, and diagnostics.
- Create `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitDrivingControllerTests.cs`: controller state and math tests.
- Modify `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`: public options/diagnostics contracts and pursuit snapshot driving state.
- Modify `external/AssettoServer/AssettoServer/Server/Ai/AiState.cs`: controller invocation, physical clearance, target-only obstacle treatment, release/reset, and collision notification.
- Modify `external/AssettoServer/AssettoServer/Server/Ai/AiBehavior.cs`: distinguish active-target pursuit collisions from ordinary traffic collisions.
- Create `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitDrivingIntegrationTests.cs`: target obstacle, traffic isolation, collision recovery, lane phases, and snapshot regression tests.

### PoliceChasePlugin

- Modify `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`: seven P0.8 settings with approved defaults.
- Modify `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs`: cross-field distance and closing-speed validation.
- Modify `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`: plugin-side options, state, reason, and diagnostic contracts.
- Modify `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`: explicit bidirectional-free mapping from plugin types to core types.
- Modify `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs`: construct P0.8 options, retain legacy speed path when disabled, and log semantic transitions.
- Modify `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs`: defaults.
- Modify `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs`: validation boundaries.
- Modify `tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiStateTests.cs`: mapping.
- Modify `tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs`: legacy/new-mode behavior and log deduplication.
- Modify `docs/P0.5-perseguicao-basica.md`: mark the legacy distance policy and link P0.8.
- Create `docs/P0.8-aggressive-pursuit.md`: operations, configuration, logs, deployment artifacts, and real-server test matrix.

---

### Task 1: Add validated P0.8 configuration and transport contracts

**Files:**
- Modify: `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`
- Modify: `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs`
- Modify: `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`
- Test: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs`
- Test: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs`

**Interfaces:**
- Produces: `PolicePursuitDrivingOptions`, `PolicePursuitDrivingState`, `PolicePursuitDrivingReason`, and `PolicePursuitDrivingDiagnostics` for Tasks 2 and 5.
- Produces configuration properties named exactly `PursuitAggressiveDrivingEnabled`, `PursuitContactEnabled`, `PursuitCatchUpDistanceMeters`, `PursuitCloseDistanceMeters`, `PursuitContactDistanceMeters`, `PursuitMaxClosingSpeedKph`, and `PursuitContactClosingSpeedKph`.

- [ ] **Step 1: Write failing default-value tests**

Add assertions to `PoliceChaseConfigurationTests.DefaultsAreStable`:

```csharp
Assert.Multiple(() =>
{
    Assert.That(configuration.PursuitAggressiveDrivingEnabled, Is.False);
    Assert.That(configuration.PursuitContactEnabled, Is.True);
    Assert.That(configuration.PursuitCatchUpDistanceMeters, Is.EqualTo(100));
    Assert.That(configuration.PursuitCloseDistanceMeters, Is.EqualTo(15));
    Assert.That(configuration.PursuitContactDistanceMeters, Is.EqualTo(3));
    Assert.That(configuration.PursuitMaxClosingSpeedKph, Is.EqualTo(35));
    Assert.That(configuration.PursuitContactClosingSpeedKph, Is.EqualTo(5));
});
```

The code default is disabled for backward compatibility; the documented P0.8 server configuration enables it explicitly.

- [ ] **Step 2: Write failing validator tests**

Add parameterized tests proving finite positive ordered distances and non-negative ordered closing speeds:

```csharp
[TestCase(15, 15, 3, nameof(PoliceChaseConfiguration.PursuitCatchUpDistanceMeters))]
[TestCase(100, 3, 3, nameof(PoliceChaseConfiguration.PursuitCloseDistanceMeters))]
public void DrivingDistancesMustBeStrictlyDescending(
    float catchUp, float close, float contact, string expectedProperty)
{
    var result = _validator.Validate(new PoliceChaseConfiguration
    {
        PursuitCatchUpDistanceMeters = catchUp,
        PursuitCloseDistanceMeters = close,
        PursuitContactDistanceMeters = contact
    });
    Assert.That(result.Errors.Select(error => error.PropertyName), Does.Contain(expectedProperty));
}

[TestCase(-1, 5, nameof(PoliceChaseConfiguration.PursuitMaxClosingSpeedKph))]
[TestCase(35, 36, nameof(PoliceChaseConfiguration.PursuitContactClosingSpeedKph))]
public void DrivingClosingSpeedsMustBeValid(
    float maximum, float contact, string expectedProperty)
{
    var result = _validator.Validate(new PoliceChaseConfiguration
    {
        PursuitMaxClosingSpeedKph = maximum,
        PursuitContactClosingSpeedKph = contact
    });
    Assert.That(result.Errors.Select(error => error.PropertyName), Does.Contain(expectedProperty));
}
```

- [ ] **Step 3: Run the focused tests and observe RED**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj -c Release --filter "FullyQualifiedName~PoliceChaseConfiguration"
```

Expected: compilation failures for the missing P0.8 properties.

- [ ] **Step 4: Add configuration properties and validation**

Add to `PoliceChaseConfiguration`:

```csharp
public bool PursuitAggressiveDrivingEnabled { get; set; }
public bool PursuitContactEnabled { get; set; } = true;
public float PursuitCatchUpDistanceMeters { get; set; } = 100;
public float PursuitCloseDistanceMeters { get; set; } = 15;
public float PursuitContactDistanceMeters { get; set; } = 3;
public float PursuitMaxClosingSpeedKph { get; set; } = 35;
public float PursuitContactClosingSpeedKph { get; set; } = 5;
```

Add FluentValidation rules that reject non-finite values before comparing fields, then enforce `catchUp > close > contact > 0` and `0 <= contactClosing <= maxClosing`.

- [ ] **Step 5: Add plugin transport contracts**

Add to `IPoliceAiSlotSource.cs`:

```csharp
public enum PolicePursuitDrivingState
{
    CatchUp,
    Approach,
    ClosePressure,
    Contact,
    Recovery
}

public enum PolicePursuitDrivingReason
{
    DistanceCatchUp,
    DistanceApproach,
    ClosePressure,
    ContactPressure,
    ContactDisabled,
    ExcessClosingSpeed,
    LaneChangeLimited,
    CollisionRecovery
}

public sealed record PolicePursuitDrivingOptions(
    bool Enabled,
    bool ContactEnabled,
    float CatchUpDistanceMeters,
    float CloseDistanceMeters,
    float ContactDistanceMeters,
    float MaximumSpeedMetersPerSecond,
    float MaximumClosingSpeedMetersPerSecond,
    float ContactClosingSpeedMetersPerSecond);

public sealed record PolicePursuitDrivingDiagnostics(
    long Revision,
    PolicePursuitDrivingState State,
    PolicePursuitDrivingReason Reason,
    float RouteDistanceMeters,
    float PhysicalClearanceMeters,
    float TargetSpeedMetersPerSecond,
    float PoliceSpeedMetersPerSecond,
    float ClosingSpeedMetersPerSecond,
    float DesiredClosingSpeedMetersPerSecond,
    float RequestedSpeedMetersPerSecond,
    bool CollisionReported);
```

Extend `PolicePursuitTrackingOptions` with optional `PolicePursuitDrivingOptions? Driving = null` and `PolicePursuitTrackingResult` with optional `PolicePursuitDrivingDiagnostics? DrivingDiagnostics = null`, preserving existing call sites.

- [ ] **Step 6: Run focused tests and observe GREEN**

Run the Task 1 test command. Expected: all configuration tests pass.

- [ ] **Step 7: Commit the parent contracts**

```powershell
git add src/PoliceChasePlugin/PoliceChaseConfiguration.cs src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs
git commit -m "feat: define aggressive pursuit configuration"
```

---

### Task 2: Implement the pure longitudinal controller in AssettoServer

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitDrivingController.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitDrivingControllerTests.cs`

**Interfaces:**
- Consumes core `AiLaneChangePhase` and `AiPursuitDrivingOptions`.
- Produces `AiPursuitDrivingController.Update(AiPursuitDrivingRequest)` returning `AiPursuitDrivingDecision`.
- Produces immutable `AiPursuitDrivingControllerState` carried in `AiPursuitSnapshot`.
- Produces `ReportCollision(long nowMilliseconds)` for Task 4.

- [ ] **Step 1: Write state-selection tests**

Create tests using a helper request with player speed `20 m/s`, police speed `20 m/s`, and maximum speed `50 m/s`:

```csharp
[TestCase(150, 140, AiPursuitDrivingState.CatchUp)]
[TestCase(60, 55, AiPursuitDrivingState.Approach)]
[TestCase(12, 10, AiPursuitDrivingState.ClosePressure)]
[TestCase(4, 2, AiPursuitDrivingState.Contact)]
public void SelectsStateFromRouteAndPhysicalDistance(
    float routeDistance,
    float clearance,
    AiPursuitDrivingState expected)
{
    var decision = CreateController().Update(Request(routeDistance, clearance));
    Assert.That(decision.Diagnostics.State, Is.EqualTo(expected));
}
```

Add boundary tests showing hysteresis retains the current state for small movements around 100, 15, and 3 metres.

- [ ] **Step 2: Write closing-speed controller tests**

Cover these concrete expectations:

```csharp
Assert.That(CatchUp(policeKph: 80, playerKph: 120).RequestedSpeedKph, Is.GreaterThan(120));
Assert.That(Approach(policeKph: 150, playerKph: 40, distance: 20).RequestedSpeedKph, Is.LessThan(150));
Assert.That(Close(policeKph: 125, playerKph: 120, clearance: 5).RequestedSpeedKph, Is.InRange(120, 130));
Assert.That(Contact(policeKph: 125, playerKph: 120).RequestedSpeedKph, Is.LessThanOrEqualTo(125));
Assert.That(ContactEnabled().DesiredClosingSpeedKph, Is.EqualTo(5).Within(0.01));
Assert.That(ContactDisabled().DesiredClosingSpeedKph, Is.Zero);
```

Add tests for zero speed, a stopped target, negative closing speed, maximum-speed cap, finite output, and the closing-speed deadband preserving the previous request.

Add a diagnostic revision test: two updates with the same state/reason and only changed distances retain the revision; a state transition or collision recovery increments it exactly once.

- [ ] **Step 3: Write lane-phase and route-revision tests**

Assert `Changing` produces a lower desired closing speed than `None`, `WaitingForGap` does not bypass safety, `Cooldown` restores the normal envelope, and a route revision change preserves longitudinal state when the measurements still select the same state.

- [ ] **Step 4: Run controller tests and observe RED**

Run:

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --filter "FullyQualifiedName~AiPursuitDrivingControllerTests"
```

Expected: compilation failure because the controller contracts do not exist.

- [ ] **Step 5: Define core contracts**

In `AiPursuitControl.cs`, add core equivalents of the plugin state/reason/options/diagnostics records. Extend `AiPursuitTrackingOptions` and `AiPursuitTrackingResult` with optional driving fields. Extend the internal snapshot:

```csharp
internal sealed record AiPursuitSnapshot(
    byte TargetSessionId,
    Vector3 TargetPosition,
    AiPursuitTrackingOptions Options,
    AiPursuitRouteState NavigationState,
    float? DesiredSpeedMetersPerSecond,
    AiPursuitDrivingControllerState? DrivingState,
    AiPursuitDrivingDiagnostics? DrivingDiagnostics);
```

Keep optional/default parameters at public boundaries so existing P0.6/P0.7 tests continue compiling.

Define `AiPursuitDrivingControllerState` as a public immutable record because it appears in the public controller request/decision signatures. It stores the current state, last requested speed, diagnostic revision, collision/recovery flag, and the last semantic reason. The diagnostic revision increments only when state, semantic reason, collision/recovery flag, or target changes; continuous measurement changes do not increment it.

- [ ] **Step 6: Implement the pure controller**

Use these exact public entry types:

```csharp
public sealed record AiPursuitDrivingRequest(
    AiPursuitDrivingOptions Options,
    float RouteDistanceMeters,
    float PhysicalClearanceMeters,
    float TargetSpeedMetersPerSecond,
    float PoliceSpeedMetersPerSecond,
    AiLaneChangePhase LaneChangePhase,
    long RouteRevision,
    long NowMilliseconds,
    AiPursuitDrivingControllerState? PreviousState);

public sealed record AiPursuitDrivingDecision(
    float RequestedSpeedMetersPerSecond,
    AiPursuitDrivingControllerState State,
    AiPursuitDrivingDiagnostics Diagnostics);

public sealed class AiPursuitDrivingController
{
    public AiPursuitDrivingDecision Update(AiPursuitDrivingRequest request);
    public AiPursuitDrivingControllerState ReportCollision(
        AiPursuitDrivingControllerState state,
        long nowMilliseconds);
}
```

Implement a piecewise-linear desired-closing envelope, clamp requested speed to `[0, MaximumSpeedMetersPerSecond]`, use a `0.5 m/s` closing-error deadband, and use 10% threshold hysteresis. During `Changing`, multiply desired closing speed by `0.5`; do not change route or lateral state.

Recovery exits when either clearance exceeds `ContactDistanceMeters + 2` or the target opens distance with closing speed at or below zero. It immediately yields to CatchUp if route distance exceeds `CatchUpDistanceMeters`.

- [ ] **Step 7: Run focused tests and observe GREEN**

Run the Task 2 test command. Expected: every controller test passes.

- [ ] **Step 8: Commit the core controller inside the submodule**

```powershell
git -C external/AssettoServer add AssettoServer/Server/Ai/AiPursuitControl.cs AssettoServer/Server/Ai/AiPursuitDrivingController.cs AssettoServer.Tests/Server/Ai/AiPursuitDrivingControllerTests.cs
git -C external/AssettoServer commit -m "feat: add pursuit driving controller"
```

---

### Task 3: Integrate controller output with AiState and target-only obstacle handling

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiState.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitDrivingIntegrationTests.cs`

**Interfaces:**
- Consumes `AiPursuitDrivingController.Update` from Task 2.
- Produces `AiState.IsPursuing(byte targetSessionId)` and `AiState.ReportPursuitCollision(byte targetSessionId)` for Task 4.
- Produces internal target classification used before native player-obstacle braking.

- [ ] **Step 1: Write physical-clearance and target-classification tests**

Extract testable static helpers on `AiPursuitControl`:

```csharp
Assert.That(
    AiPursuitControl.CalculatePhysicalClearance(
        centerDistance: 8, policeFrontLength: 2, targetRearLength: 2),
    Is.EqualTo(4));

Assert.That(
    AiPursuitControl.IsControlledTargetObstacle(
        pursuitTargetSessionId: 10,
        obstacleSessionId: 10,
        aggressiveDrivingEnabled: true),
    Is.True);

Assert.That(
    AiPursuitControl.IsControlledTargetObstacle(10, 11, true),
    Is.False);
Assert.That(
    AiPursuitControl.IsControlledTargetObstacle(10, 10, false),
    Is.False);
```

- [ ] **Step 2: Write AiState pursuit integration tests**

Add tests proving:

- active tracking stores the controller request and returns diagnostics;
- same target carries controller state across updates;
- route revision carries state rather than resetting it;
- target change creates new controller state;
- temporary route loss preserves the previous bounded request;
- `ReleasePursuit()` clears controller and lane state together;
- `MaximumSpatialDistanceMeters` still ends pursuit.

- [ ] **Step 3: Write obstacle regression tests**

Use the extracted decision helper to pin the branch ordering:

- active target with P0.8 bypasses only generic player hard-stop/following;
- another player still produces the native safety limit;
- the closest traffic AI still produces the native safety limit;
- unsafe `Changing` still forces zero;
- spline corner speed remains a final upper bound;
- an AI with no pursuit uses the old branch unchanged.

- [ ] **Step 4: Run focused integration tests and observe RED**

Run:

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --filter "FullyQualifiedName~AiPursuitDrivingIntegrationTests"
```

Expected: failures for missing integration and helpers.

- [ ] **Step 5: Invoke the controller from TrackPursuit**

After navigation has produced an active retained state:

```csharp
var physicalClearance = AiPursuitControl.CalculatePhysicalClearance(
    Vector3.Distance(Status.Position, targetPosition),
    EntryCar.VehicleLengthPreMeters,
    target.VehicleLengthPostMeters);

var drivingDecision = options.Driving is { Enabled: true } driving
    ? _pursuitDrivingController.Update(new AiPursuitDrivingRequest(
        driving,
        navigationState.Plan.DistanceMeters,
        physicalClearance,
        targetSpeed,
        CurrentSpeed,
        _laneChangeController?.Phase ?? AiLaneChangePhase.None,
        navigationState.Revision,
        _sessionManager.ServerTimeMilliseconds,
        previous?.TargetSessionId == target.SessionId ? previous.DrivingState : null))
    : null;
```

Store `drivingDecision.RequestedSpeedMetersPerSecond` as the pursuit desired speed when enabled. Preserve the preceding desired speed during `RouteTemporarilyUnavailable`.

- [ ] **Step 6: Special-case only the active target in DetectObstacles**

Compute `controlledTargetObstacle` from the pursuit target, obstacle session, and enabled option. Exclude that single obstacle from the `_minObstacleDistance` hard stop and the generic player-following branch. Do not exclude it from route, curve, traffic-AI, or unsafe-lane-change limits.

Keep native player logic textually intact for all non-target players to make the isolation reviewable.

- [ ] **Step 7: Add collision query/report methods and release reset**

Implement:

```csharp
public bool IsPursuing(byte targetSessionId)
{
    var pursuit = Volatile.Read(ref _pursuit);
    return pursuit?.TargetSessionId == targetSessionId
           && pursuit.Options.Driving is { Enabled: true };
}

public bool ReportPursuitCollision(byte targetSessionId)
```

`ReportPursuitCollision` uses a compare-exchange loop. It returns false for a missing/different/disabled pursuit; otherwise it replaces only `DrivingState` and driving diagnostics, preserving navigation and desired-speed fields.

- [ ] **Step 8: Run focused integration and existing P0.6/P0.7 tests**

Run:

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --filter "FullyQualifiedName~AiPursuitDrivingIntegrationTests|FullyQualifiedName~AiPursuitNavigatorTests|FullyQualifiedName~AiPursuitLane|FullyQualifiedName~RealSplineTransitionIntegrationTests"
```

Expected: all selected tests pass.

- [ ] **Step 9: Commit AiState integration inside the submodule**

```powershell
git -C external/AssettoServer add AssettoServer/Server/Ai/AiState.cs AssettoServer/Server/Ai/AiPursuitControl.cs AssettoServer.Tests/Server/Ai/AiPursuitDrivingIntegrationTests.cs
git -C external/AssettoServer commit -m "feat: integrate aggressive pursuit driving"
```

---

### Task 4: Route collision events into pursuit recovery

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiBehavior.cs`
- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitDrivingIntegrationTests.cs`

**Interfaces:**
- Consumes `AiState.ReportPursuitCollision(byte)` from Task 3.
- Preserves `AiState.StopForCollision()` for every non-target collision.

- [ ] **Step 1: Extract and test collision disposition**

Add an internal helper returning:

```csharp
internal enum AiCollisionDisposition
{
    PursuitRecovery,
    NativeStop
}
```

Tests must assert:

- pursuing AI plus sender session equal to target → `PursuitRecovery`;
- pursuing AI plus another sender → `NativeStop`;
- normal traffic AI → `NativeStop`;
- released pursuit → `NativeStop`.

- [ ] **Step 2: Run collision tests and observe RED**

Run the Task 3 focused test command. Expected: missing disposition/helper failures.

- [ ] **Step 3: Modify OnCollision without changing normal traffic timing**

In `AiBehavior.OnCollision()`:

```csharp
if (!targetAiState.AiState.ReportPursuitCollision(sender.EntryCar.SessionId))
{
    Task.Delay(Random.Shared.Next(100, 500))
        .ContinueWith(_ => targetAiState.AiState.StopForCollision());
}
```

Do not schedule the native stop after a successful pursuit collision report. Keep the 25 metre validation and AI-controlled target check unchanged.

- [ ] **Step 4: Add recovery behavior tests**

Assert collision reporting:

- leaves `ShouldRetainPursuit` true;
- leaves route revision and junction decisions unchanged;
- returns `Recovery` diagnostics on the next active track update;
- exits recovery when clearance reopens;
- switches directly to CatchUp when the target has opened beyond the catch-up threshold;
- does not create a respawn or release call.

- [ ] **Step 5: Run collision and full controller tests**

Run:

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --filter "FullyQualifiedName~AiPursuitDriving"
```

Expected: all P0.8 core tests pass.

- [ ] **Step 6: Commit recovery inside the submodule**

```powershell
git -C external/AssettoServer add AssettoServer/Server/Ai/AiBehavior.cs AssettoServer.Tests/Server/Ai/AiPursuitDrivingIntegrationTests.cs
git -C external/AssettoServer commit -m "feat: recover police pursuit after collision"
```

---

### Task 5: Map P0.8 through the plugin and preserve legacy mode

**Files:**
- Modify: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs`
- Test: `tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiStateTests.cs`
- Test: `tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs`

**Interfaces:**
- Consumes core options and diagnostics from Tasks 2–4.
- Produces transition-only Serilog events and the disabled-mode call to `SetDesiredSpeed`.

- [ ] **Step 1: Write adapter mapping tests**

Create a plugin tracking request with every P0.8 option set to a distinct value. Assert the native fake receives metres-per-second values and every boolean/distance unchanged. Return core diagnostics for each state/reason and assert the plugin record maps every field explicitly.

- [ ] **Step 2: Write service behavior tests**

Add tests proving:

```csharp
[Test]
public void AggressiveModePassesDrivingOptionsAndDoesNotApplyLegacySpeedPolicy()
```

Expected: `TrackPursuit` receives `Driving.Enabled == true`; `SpeedRequests` remains empty because the core owns the request.

```csharp
[Test]
public void DisabledAggressiveModeUsesLegacySpeedPolicy()
```

Expected: the existing 150 m / 20 km/h scenario still requests 45 km/h.

Also test maximum speed conversion, contact toggle, temporary route loss, definitive release, and target change.

- [ ] **Step 3: Write diagnostic logging tests**

Feed repeated diagnostics with the same state/reason but changing continuous values. Assert one log. Then change state from `CatchUp` to `Approach`, `ClosePressure`, `Contact`, `Recovery`, and back; assert one log per semantic transition containing distance, player speed, police speed, closing speed, requested speed, and reason.

- [ ] **Step 4: Run focused plugin tests and observe RED**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj -c Release --filter "FullyQualifiedName~AssettoServerPoliceAiStateTests|FullyQualifiedName~PolicePursuitServiceTests"
```

Expected: failures for missing mappings and service behavior.

- [ ] **Step 5: Map options and diagnostics explicitly**

Extend `AssettoServerNativePolicePursuitState.TrackPursuit()` to construct core `AiPursuitDrivingOptions` and map core state/reason enums with exhaustive switch expressions. Do not cast enum integers.

- [ ] **Step 6: Switch service ownership by feature flag**

Build `_trackingOptions.Driving` in the constructor using `/ 3.6f` for speed conversion. In `HandleActive()`:

```csharp
if (!_configuration.PursuitAggressiveDrivingEnabled)
{
    var desiredSpeed = PolicePursuitSpeedPolicy.Calculate(
        result.TargetSpeedMetersPerSecond,
        result.RouteDistanceMeters.Value,
        _configuration.PursuitDesiredDistanceMeters,
        _configuration.PursuitMaxSpeedKph);
    state.SetDesiredSpeed(desiredSpeed);
}
```

When enabled, the core has already stored the bounded request and the plugin must not overwrite it.

- [ ] **Step 7: Add semantic driving logs**

Track the last `(Revision, State, Reason, CollisionReported)` signature. Log only a changed signature. Reset it on pursuit release or target change. Use one structured event such as:

```csharp
Log.Information(
    "[PoliceChase] Pursuit driving state: {State}; target {TargetSessionId}; " +
    "routeDistance {RouteDistance:F1}m; clearance {Clearance:F1}m; " +
    "playerSpeed {PlayerSpeed:F1}km/h; policeSpeed {PoliceSpeed:F1}km/h; " +
    "closingSpeed {ClosingSpeed:F1}km/h; targetSpeed {RequestedSpeed:F1}km/h; reason {Reason}",
    diagnostics.State,
    targetSessionId,
    diagnostics.RouteDistanceMeters,
    diagnostics.PhysicalClearanceMeters,
    diagnostics.TargetSpeedMetersPerSecond * 3.6f,
    diagnostics.PoliceSpeedMetersPerSecond * 3.6f,
    diagnostics.ClosingSpeedMetersPerSecond * 3.6f,
    diagnostics.RequestedSpeedMetersPerSecond * 3.6f,
    diagnostics.Reason);
```

- [ ] **Step 8: Run focused plugin tests and observe GREEN**

Run the Task 5 test command. Expected: all selected tests pass.

- [ ] **Step 9: Update the parent submodule pointer and commit integration**

```powershell
git add external/AssettoServer src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiStateTests.cs tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs
git commit -m "feat: enable aggressive police pursuit"
```

---

### Task 6: Document operation and perform complete verification

**Files:**
- Modify: `docs/P0.5-perseguicao-basica.md`
- Create: `docs/P0.8-aggressive-pursuit.md`
- Modify only when an old expectation intentionally changes: affected existing test file with a written explanation in the commit.

**Interfaces:**
- Consumes the final option names, diagnostics, commits, test counts, and artifact hashes.
- Produces the manual deployment and eight-scenario real-server validation guide.

- [ ] **Step 1: Write operational documentation**

Document:

- cause of the old 50 m behavior;
- controller states and closing-speed calculation;
- target-only obstacle exception;
- collision recovery and kinematic limitation;
- preservation of P0.6/P0.7 and normal traffic;
- the approved YAML block with aggressive driving explicitly enabled;
- expected transition-only logs;
- manual deployment using `dotnet .\AssettoServer.dll`;
- server tests A–H from the specification;
- explicit status: locally complete, pending real-server validation.

- [ ] **Step 2: Run the complete AssettoServer test suite**

Run:

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore
```

Expected: all tests pass, zero failed, zero skipped unless a pre-existing environment-gated real-spline test reports its documented skip.

- [ ] **Step 3: Run the complete plugin test suite**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj -c Release --no-restore
```

Expected: all tests pass, zero failed.

- [ ] **Step 4: Build AssettoServer Release**

Run:

```powershell
dotnet build external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release --no-restore
```

Expected: zero errors; report inherited and new warnings separately.

- [ ] **Step 5: Build PoliceChasePlugin Release**

Run:

```powershell
dotnet build src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release --no-restore
```

Expected: zero errors and zero new warnings.

- [ ] **Step 6: Compute deployable hashes and inspect repository state**

Run:

```powershell
Get-FileHash external/AssettoServer/AssettoServer/bin/Release/net8.0/AssettoServer.dll -Algorithm SHA256
Get-FileHash src/PoliceChasePlugin/bin/Release/net8.0/PoliceChasePlugin.dll -Algorithm SHA256
git -C external/AssettoServer status --short
git -C external/AssettoServer rev-parse HEAD
git status --short
git rev-parse HEAD
```

Expected: only intended documentation is uncommitted before the final docs commit; capture both hashes and core commit.

- [ ] **Step 7: Commit documentation**

```powershell
git add docs/P0.5-perseguicao-basica.md docs/P0.8-aggressive-pursuit.md
git commit -m "docs: document aggressive pursuit operation"
```

- [ ] **Step 8: Perform verification-before-completion review**

Use `superpowers:verification-before-completion`. Re-run any command whose evidence is stale after a fix. Inspect the complete parent diff and each core commit. Fix every Critical or Important regression with a failing test before changing production code.

- [ ] **Step 9: Publish both repositories only after verification**

Push the AssettoServer branch containing the pinned commits first. Then push the PoliceChasePlugin `main` commit that references the reachable core commit. Confirm the remote parent and submodule hashes match the locally verified hashes.

- [ ] **Step 10: Deliver the local-completion report**

Report:

- parent and core commits;
- changed files;
- tests passed/failed/skipped by project;
- build warnings/errors by project;
- DLL SHA256 hashes;
- YAML additions without editing the server;
- expected state-transition logs;
- manual deployment files and commands;
- test matrix A–H;
- explicit statement that P0.8 awaits real-server validation.
