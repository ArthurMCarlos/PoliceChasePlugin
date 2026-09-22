# P0.7 Short-Anchor Alignment Correction Plan

**Goal:** Allow a dedicated police AI to begin a safe physical lane transition when the pursued player's physical anchor is already on an immediate adjacent lane, even when the route distance to that anchor is shorter than the configured transition length.

**Architecture:** Keep `AiPursuitNavigator` routing and the forward-only graph unchanged. Separate route distance to the physical target from physical transition capacity. `FutureJunction` retains its decision-distance gate; `TargetLaneAlignment` uses the target route only for reachability/ranking/lookahead and validates transition capacity on the source and destination spline cursors. Preserve the existing controller, safety checks, hysteresis, cooldown, and single-adjacent-lane constraint.

**Spec:** Approved in the 2026-09-21 conversation after analysis of the real-server log. This plan supersedes only the incorrect short-distance assumption in `2026-09-19-police-chase-p0-7-target-lane-alignment-design.md`.

## Global constraints

- No graph edges, reverse routing, teleport, respawn, hardcoded Shutoko IDs, reflection, or global traffic configuration changes.
- `FutureJunction` behavior remains unchanged.
- The controller remains the only component that moves the AI laterally.
- Write and observe failing tests before each production change.
- Update the AssettoServer submodule first, then the plugin adapter/logging, documentation, and parent pointer.

## Task 1: Correct direct-alignment semantics in the selector

- Add selector regression tests for a valid adjacent target route at `0 m` and below `60 m`.
- Confirm they fail with `InsufficientPreparationDistance`.
- Stop treating route distance to the physical target as transition capacity.
- Represent the absence of a topological decision explicitly for `TargetLaneAlignment`.
- Keep the lookahead upper bound and candidate ranking behavior.

## Task 2: Validate actual transition capacity in the pipeline

- Add pipeline tests proving short-anchor alignment prepares when both cursors can advance the transition distance.
- Add tests proving insufficient source or destination forward capacity still rejects preparation.
- Apply the decision-distance gate only to `FutureJunction`.
- Publish an explicit pipeline rejection reason instead of silently returning a selector result that appears valid.

## Task 3: Make rejection diagnostics truthful

- Add tests for motivation and exact rejecting candidate on failed evaluations.
- Separate target route distance, junction decision distance, required transition distance, and available source/destination distance.
- Prevent the aggregate reason from being paired with unrelated first-candidate evidence.
- Preserve semantic deduplication to avoid per-frame log spam.

## Task 4: Propagate diagnostics through the plugin

- Add adapter and service logging tests first.
- Map every new diagnostic field explicitly.
- Log enough evidence to distinguish route failure from physical-space failure without changing pursuit behavior.

## Task 5: Integration, documentation, and verification

- Add an integrated locator/navigator/pipeline regression matching the `57707 -> 171051` shape without hardcoding production behavior to those IDs.
- Preserve real-spline tests as explicit tests when `POLICE_CHASE_FAST_LANE_AIP` is available.
- Correct the P0.7 design and operational documentation.
- Run all AssettoServer tests, all plugin tests, and Release builds.
- Perform one fresh whole-change review, address Important/Critical findings with RED-GREEN tests, commit the core, update the parent pointer, commit the parent, and push both approved branches.

## Review focus

- Short route-to-target distance must never substitute for source/destination spline capacity.
- `FutureJunction` must retain the pre-junction 60 m requirement.
- A valid selector result must not be silently discarded by the pipeline.
- Diagnostics must identify the evidence responsible for the published reason.
- No behavior change for ordinary traffic AI or non-police slots.
