# P0.8.1 High Intensity / Offensive Pursuit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve the validated P0.8 pursuit while removing fixed lane-change slowdown, strengthening bounded close approach, and adding a conservative, optional physical PIT offset for the single police AI.

**Architecture:** Keep P0.6/P0.7 route selection and lane movement unchanged. Extend the pure core driving controller, add a pure PIT state machine, and overlay a rate-limited signed offset on the dedicated police `AiState`'s spline pose. Pass options and transition events through existing plugin contracts; keep PIT off by default.

**Tech Stack:** C#/.NET 8, NUnit, FluentValidation, AssettoServer core submodule, PoliceChasePlugin.

**Spec:** `docs/superpowers/specs/2026-09-22-police-chase-p0-8-1-high-intensity-design.md`

## Global Constraints

- Parent baseline `9bdb215465573426b73dce972f12e67cd3160155`; core baseline `64968afce4dd0df3b84c9fa44a9356dbd879d381`.
- P0.8 contact distance 3 m and contact closing speed 5 km/h remain unchanged by default.
- One dedicated police AI only; normal traffic behavior unchanged.
- No force, teleport, snap, instant boost, damage, graph edge, multiple cars, or server deploy.
- `PursuitMaxClosingSpeedKph`, `PursuitMaxSpeedKph`, native acceleration, braking, traffic safety, and cornering continue to cap motion.
- Existing P0.6/P0.7 tests and semantics remain valid. If an existing test assertion intentionally changes, explain why in the commit.

## Review Focus

- A collision just after PIT start must clear the lateral command, log contact once, and preserve P0.8 Recovery.
- A blocked second player or traffic car beside the target must prevent the candidate PIT side without suppressing normal obstacle braking.
- Route loss, target switch, or lane-change request during the lateral motion must return smoothly toward center without retaining a stale side.
- A stopped, reversed, or invalid target velocity must not generate an arbitrary side or nonfinite pose.
- A 300 m gap with the police slower than the target must stay capped at configured maximum; it cannot bypass cornering or native acceleration.

---

### Task 1: Bounded high-intensity longitudinal curve

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitDrivingController.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitDrivingControllerTests.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitDrivingIntegrationTests.cs`

**Interfaces:** Existing `AiPursuitDrivingController.Update(AiPursuitDrivingRequest)` stays unchanged. No new config is needed.

- [ ] Write tests for 300 m/150 m CatchUp capped at maximum, 70 m Approach stronger than the old linear curve, 10 m ClosePressure stronger than the old quarter-maximum envelope, 2–3 m Contact still at the configured 5 km/h, excess closing speed still requests braking, and `Changing`/`WaitingForGap`/`Cooldown` all producing the same desired closing speed for equal measurements.
- [ ] Run `dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --filter "FullyQualifiedName~AiPursuitDrivingControllerTests|FullyQualifiedName~AiPursuitDrivingIntegrationTests"` and observe the new assertions fail.
- [ ] Remove `LaneChangeClosingSpeedFactor` and the automatic `LaneChangeLimited` reason. Replace linear Approach interpolation with a bounded square-root easing between the close and catch-up thresholds. Increase the ClosePressure upper envelope from one quarter to one half of maximum closing speed, while retaining the contact endpoint and configured caps. Do not change `DetectObstacles` or P0.7 lane safety.
- [ ] Re-run focused tests, then all core tests. Commit the core change with an explicit note that old lane penalty expectations changed intentionally.

### Task 2: Optional PIT configuration and contracts

**Files:**
- Modify: `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`
- Modify: `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs`
- Modify: `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`
- Test: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs`
- Test: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs`

**Interfaces:** Add optional `PolicePursuitPitOptions? Pit = null` and `AiPursuitPitOptions? Pit = null` at the end of the respective driving option records. Define mirrored `Enabled`, `MaxDistanceMeters`, `MaxClosingSpeedMetersPerSecond`, `LateralOffsetMeters`, `CommitMilliseconds`, and `CooldownMilliseconds` fields. Define mirrored PIT phase/side/event/abort-reason diagnostics with stable names for Tasks 3–5.

- [ ] Add failing tests for disabled default, suggested numeric defaults, dependency on aggressive driving and contact when PIT enabled, finite/positive distance and offset, 0–configured maximum closing speed, bounded commit/cooldown, and offset fitting a 3 m lane with 0.4 m reserve.
- [ ] Run `dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj -c Release --filter "FullyQualifiedName~PoliceChaseConfiguration"` and observe failures.
- [ ] Add the six config properties, validator rules, and mirrored optional records/enums. Keep all existing defaults intact.
- [ ] Re-run focused tests, then both full suites to prove contracts compile independently. Commit core contracts first, then parent contracts.

### Task 3: Pure PIT decision and smooth offset policy

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitPitController.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitPitControllerTests.cs`

**Interfaces:** `AiPursuitPitController.Update(AiPursuitPitRequest) -> AiPursuitPitDecision`, `ReportCollision(AiPursuitPitControllerState, long) -> AiPursuitPitDecision`, and `AiPursuitPitController.ApproachOffset(float current, float desired, float maximumStep) -> float`. Request includes options, target session ID, route validity, same-lane and behind checks, clearance, closing speed, driving state, lane phase, junction proximity, left/right safety, route revision, time, and previous state. Decision includes immutable state, desired signed offset, and optional semantic event. Side is sticky through `Armed`/`Attempting`; abort/contact enters cooldown.

- [ ] Write failing tests for disabled/aggressive-off, far target, valid close approach, excessive closing speed, each blocked side, WaitingForGap/Changing, Recovery, junction proximity, stable side, cooldown, route loss, target change reset, collision event, and rate-limited offset return.
- [ ] Run `dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --filter "FullyQualifiedName~AiPursuitPitControllerTests"` and observe failures.
- [ ] Implement immutable state transitions: `Idle -> Armed -> Attempting -> Cooldown -> Idle`; choose left only if safe, otherwise right; abort whenever the selected side becomes unsafe. Clamp the offset step and emit events only on transitions.
- [ ] Re-run focused tests and full core suite. Commit core controller.

### Task 4: Native police-only PIT integration

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiState.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitDrivingIntegrationTests.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitPitControllerTests.cs`

**Interfaces:** Extend `AiPursuitSnapshot` and `AiPursuitTrackingResult` with optional PIT state/diagnostics. `AiState.TrackPursuit` creates a request only when aggressive PIT is enabled and navigation is active; `AiState.Update` overlays the desired offset on spline pose without changing the point id or route. `ReportPursuitCollision` marks PIT contact only when an attempt was active, while preserving `Recovery` and the exact-target native collision bypass.

- [ ] Add failing tests for world-position/velocity heading checks, behind and same-lane geometry, obstacle side selection excluding the target, route/junction/lane gates, tangent-aware smooth overlay and smooth return, and collision recovery with a PIT contact event. Existing P0.7 safety tests must continue to pass unmodified.
- [ ] Run the core PIT and P0.6/P0.7 integration filters and observe failures.
- [ ] Build the PIT request from current `EntryCar.Status`, route plan, lane phase, and a side probe using existing `AiLaneChangeSafety` on each shifted pose, excluding the intended target. Use `_pursuitUpdateGate` for tracking/collision/release state changes. Gate P0.7 transition while an old PIT offset returns to center. Apply offset with a bounded per-tick step and recompute the published tangent from lateral motion; never write offset to spline anchors.
- [ ] Re-run focused and full core tests, then build core Release. Commit core integration.

### Task 5: Plugin mapping, semantic logs, operational guide, final verification

**Files:**
- Modify: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs`
- Modify: `tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiStateTests.cs`
- Modify: `tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs`
- Modify: `docs/P0.8-aggressive-pursuit.md`
- Create: `docs/P0.8.1-high-intensity-offensive-pursuit.md`

**Interfaces:** Map `PolicePursuitPitOptions` to core metres-per-second options, core PIT events to plugin diagnostics, and pass them via `PolicePursuitTrackingResult`. Log one event per revision and reset signatures on target change/release.

- [ ] Add failing adapter/service tests for all option conversions, PIT disabled default, arm/start/abort/contact event mapping and log deduplication, legacy speed path, and target change reset.
- [ ] Run plugin adapter/service filters and observe failures.
- [ ] Wire options and diagnostics, add transition-only logs, update P0.8's status to reflect the user-reported real-server validation, and write the operational document with real-server findings, YAML opt-in, accepted limits, build/deploy paths, PIT manual test matrix, and explicit pending-real-server status for P0.8.1.
- [ ] Run complete core and plugin test suites; build both Release projects; inspect warnings, hashes, parent and submodule status. Commit parent code/docs with the pinned core pointer.
- [ ] Request one fresh whole-change review, fix Critical/Important findings with red/green tests, then rerun the full verification. Push the core branch first and parent `main` second, as previously authorized; do not deploy to the server.
