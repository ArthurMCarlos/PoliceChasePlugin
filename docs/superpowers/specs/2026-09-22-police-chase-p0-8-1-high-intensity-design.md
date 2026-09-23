# P0.8.1 — High Intensity / Offensive Pursuit: design

## Intent and evidence

The real server validated P0.8 at parent `9bdb215465573426b73dce972f12e67cd3160155` and core `64968afce4dd0df3b84c9fa44a9356dbd879d381`: catch-up, approach, close pressure, contact, collision recovery, navigation, and lane change work. The next goal is a single dedicated police AI that keeps pressure through traffic and can make a small, controlled rear-quarter contact attempt. Normal traffic, route search, and the P0.7 lane graph must retain their behavior.

Two limitations are distinct. `AiPursuitDrivingController.Update` currently halves desired closing speed in `Changing` and labels it `LaneChangeLimited`. However, at route distance >= `PursuitCatchUpDistanceMeters` the controller already requests the configured maximum closing speed. A police speed of 76.8 km/h while a player travels at 154.2 km/h cannot be explained by that factor alone: the speed request would still exceed 170 km/h with a 35 km/h maximum closing-speed setting. Native acceleration, another vehicle, and/or `SplineLookahead` cornering can limit actual speed. P0.8.1 must remove the artificial factor and cannot promise unlimited catch-up while retaining those limits.

## Selected approach

Extend the existing core controller and `AiState`, without changing the route graph. The longitudinal controller uses a continuous, front-loaded closing-speed curve between the existing close and catch-up thresholds. It still requests no more than `PursuitMaxClosingSpeedKph` above the catch-up threshold, no more than `PursuitMaxSpeedKph` total, and preserves the P0.8 contact closing speed at the contact threshold. `Changing` no longer changes the closing-speed intent; `WaitingForGap` remains governed by the existing P0.7 safety gate. `DetectObstacles`, `AiAcceleration`, `AiDeceleration`, and cornering limits remain authoritative.

A new core `AiPursuitPitController` produces a small signed lateral offset and semantic events. It is called only from the active, route-valid pursuit of the dedicated police `AiState`. It does not add lane-graph edges or alter `CurrentSplinePointId`. The controller has `Idle`, `Armed`, `Attempting`, and `Cooldown` phases, with a sticky left/right choice. The default PIT configuration is disabled; enabling it requires aggressive driving and contact. A collision with the active target during an attempt reports `PitContact`, clears the desired offset, enters cooldown, and keeps the P0.8 `Recovery` behavior.

## PIT entry and safety

An attempt may arm only when the route is active, target is in the police's current physical lane, target motion gives a finite heading, police is behind the target, physical clearance is within the configured maximum, relative closing speed is nonnegative and within the configured PIT cap, and the P0.8 controller is neither in `Recovery` nor reacting to excess closing speed. `WaitingForGap` and `Changing` disallow PIT. A planned junction within a conservative near-field window disallows PIT. Both candidate sides are checked against the existing nearby AI and non-target players; a side is rejected for front, side, or rear blocking. The target itself is excluded from this side check because it is the intended contact.

The configured offset must fit inside the native lane width with a reserved margin. The target's world position and velocity, vehicle rear length, current spline pose, and relative heading determine whether the target is in the same lane and behind/ahead relationship. There is no vehicle width in the current core contracts, so this is a conservative lane-width proxy, not a guaranteed body-clearance calculation. If neither side is valid, there is no attempt. One side is selected and held through the configured commit period; new selection is forbidden until cooldown expires.

Any route loss, invalid measurements, target change, blocked selected side, lane change request, critical junction, or recovery aborts the attempt and starts cooldown. The police then returns to the spline center progressively. A pending P0.7 lane transition must wait until this offset has returned close to zero, so PIT and a full lane change do not overlap. The P0.7 front/side/rear checks remain in force.

## Physical movement

`AiState.Update` applies the signed offset to the published spline pose, using a bounded rate derived from offset and commit duration. It includes the lateral derivative in the published tangent/velocity and rotation. Both entry and return are gradual; no position snap, teleport, velocity jump, artificial force, or player damage is used. The police retains the same route anchor. Normal AI states never receive a PIT controller or offset.

## Configuration and telemetry

Add `PursuitPitEnabled` (default `false`), `PursuitPitMaxDistanceMeters` (6), `PursuitPitMaxClosingSpeedKph` (10), `PursuitPitLateralOffsetMeters` (0.8), `PursuitPitCommitMilliseconds` (1200), and `PursuitPitCooldownMilliseconds` (3000). Validate finite positive distances/speeds/offsets, a practical offset relative to lane width, commit/cooldown ranges, and PIT's dependency on aggressive driving and contact. Keep existing P0.8 defaults and `PursuitDesiredDistanceMeters` legacy behavior unchanged.

The core returns revisioned PIT events for arm, start, abort with reason, and contact. The plugin logs each event once, with target, side, clearance, closing speed, offset, and reason. Existing driving transition logs continue. Document that a capped request does not override traffic or cornering and that actual PIT/contact is pending real-server validation.

## Verification

Use red/green tests for: no fixed `Changing` penalty, P0.7 waiting/safety regression, continuous approach curve and distance cases, P0.8 contact and recovery, PIT enablement and every gate, sticky side, cooldown, route loss, collision, lateral rate and return, no graph/anchor change, exact-target isolation, and plugin option/log mapping. Run full core and plugin suites and Release builds. Publish verified commits to the existing GitHub repositories, then give manual server test instructions. Do not deploy to the server.
