# P0.7 Target-Lane Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the dedicated police AI safely and continuously align one lane at a time with the pursued player's physical lane, including direct parallel-lane transitions that have no junction.

**Architecture:** Keep the forward-only route graph unchanged. Extend the P0.7 selector with an explicit lane-change motivation: `TargetLaneAlignment` accepts a valid direct route to the physical target and uses that route's distance as the maneuver horizon; `FutureJunction` retains the existing junction-only behavior. The pipeline continues to own request/cooldown/commit state, and the existing physical controller remains the only code that moves an AI laterally.

**Tech Stack:** C# / .NET, AssettoServer `0.0.54+e584789afc`, NUnit, PoliceChasePlugin adapter and Serilog diagnostics.

**Spec:** `docs/superpowers/specs/2026-09-19-police-chase-p0-7-target-lane-alignment-design.md`

## Global Constraints

- Work from PoliceChasePlugin `main` commit `0cdcdd6` and AssettoServer submodule branch `codex/p0.7-real-lane-change-fix` at `e584789afc1106afadf85acb9e5168f5960b6f12`.
- Preserve the dedicated `CAR_12` reservation, the ten traffic AIs and player slots `CAR_10`/`CAR_11` exactly as they are.
- Do not change global AI counts, overbooking, density, speed, route-search budgets, or the `fast_lane.aip` asset.
- Keep the route graph forward-only: no lateral `LeftId`/`RightId` edges, reverse routing, teleport, respawn or snap.
- Reuse `AiLaneChangeController`, `AiLaneChangeTrajectory` and `AiLaneChangeSafety`; do not create a second physical movement controller.
- Every maneuver crosses one immediate, reciprocal, same-direction physical adjacency only.
- Preserve safety, cooldown, committed-maneuver completion, grace, no-route recovery and diagnostic de-duplication.
- Do not implement collision intent, PIT, ram, siren, HUD, Heat/Wanted, multiple police cars or opportunistic normal-traffic lane changes.
- Keep the core and parent repository histories reproducible: commit the core first, push it to the existing fork branch, then update and push the submodule pointer on parent `main`.

## Review Focus

- A valid current route that reaches the player through a later merge must still permit an immediately adjacent, shorter direct alignment route; pin in Task 2.
- A target on the same physical lane but at a different point ID must not cause a lateral request; pin in Task 2.
- A target two lanes away must produce only the first reciprocal step and may not skip an intermediate lane; pin in Task 3.
- A player oscillating near a lane divider must not alternate requests before cooldown/anchor retention permit it; pin in Task 3.
- A direct candidate with no junction must be accepted only for alignment, while the identical absence of a junction remains a rejection for route preparation; pin in Task 2.

---

## File Structure

| Path | Responsibility |
|---|---|
| `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitLaneSelector.cs` | Classify immediate adjacency, score current and adjacent routes, and choose `TargetLaneAlignment` or `FutureJunction`. |
| `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitLaneChangePipeline.cs` | Feed the selector the preferred physical anchor, retain a one-step target-lane intention across evaluations, and prepare only safe controller requests. |
| `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitLaneDiagnosticTracker.cs` | Include motivation and physical relation in the semantic de-duplication key. |
| `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs` | Extend public core diagnostic types with the new motivation/relation fields and reason values. |
| `external/AssettoServer/AssettoServer/Server/Ai/AiState.cs` | Publish the enriched evaluation and controller lifecycle without changing normal AI movement. |
| `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitLaneSelectorTests.cs` | Unit-test route/motivation/adjacency decisions. |
| `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitLaneChangePipelineTests.cs` | Unit-test request preparation, multi-step alignment, route priority and cooldown. |
| `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitLaneDiagnosticsTests.cs` | Ensure enriched diagnostics remain semantic and non-spammy. |
| `external/AssettoServer/AssettoServer.Tests/Server/Ai/RealSplineTransitionIntegrationTests.cs` | Reproduce all three real `fast_lane.aip` failures and exercise the controller completion path. |
| `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs` | Mirror new core enums/properties into the plugin-facing contract. |
| `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs` | Map every new core enum/property exhaustively. |
| `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs` | Include motivation/relation in log signatures and render them once per semantic state. |
| `tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiStateTests.cs` | Verify adapter preservation of the new diagnostic evidence. |
| `tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs` | Verify new evaluation logs and de-duplication. |
| `docs/P0.7-route-aware-lane-changing.md` | Document final guarantees, expected server logs and deployment validation. |

### Task 1: Add typed alignment and physical-adjacency evidence

**Files:**

- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitLaneSelector.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitLaneSelectorTests.cs`

**Interfaces:**

- Consumes: `AiSplinePoint.LeftId`, `AiSplinePoint.RightId`, `AiSpline.Operations.IsSameDirection(int, int)` and the existing `AiRoutePlanner.TryPlan` delegate.
- Produces: `AiPursuitLaneMotivation`, `AiPursuitLanePhysicalRelation`, and enriched `AiPursuitLaneSelection`/`AiPursuitLaneRouteDiagnostic` values consumed by the pipeline and plugin adapter.

- [ ] **Step 1: Write the failing selector tests for typed physical relations**

```csharp
[Test]
public void RejectsCandidateWhoseOppositeLinkDoesNotReturnToCurrentPoint()
{
    var selector = CreateSelector(
        adjacent: _ => new AiAdjacentLanePoints(10, -1),
        reverseAdjacent: pointId => pointId == 10
            ? new AiAdjacentLanePoints(-1, 77) : default,
        plans: new Dictionary<int, AiRoutePlan?> { [0] = null, [10] = Plan(120) });

    var result = selector.SelectForAlignment(0, Targets, Limits, 60, 1000);

    Assert.That(result.Kind, Is.EqualTo(AiPursuitLaneSelectionKind.Unreachable));
    Assert.That(result.CandidateLaneRoutes.Single().Reason,
        Is.EqualTo(AiPursuitLaneEvaluationReason.NonAdjacent));
}

[Test]
public void AcceptsOnlyImmediateReciprocalSameDirectionNeighbor()
{
    var result = CreatePhysicalSelector(leftOf0: 10, rightOf10: 0,
            sameDirection: true, currentPlan: null, leftPlan: Plan(120))
        .SelectForAlignment(0, Targets, Limits, 60, 1000);

    Assert.That(result.Selection!.PhysicalRelation,
        Is.EqualTo(AiPursuitLanePhysicalRelation.ImmediateLeft));
}
```

- [ ] **Step 2: Run the focused tests and verify they fail because the types/method do not exist**

Run:

```powershell
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj --filter "FullyQualifiedName~AiPursuitLaneSelectorTests"
```

Expected: compilation failure mentioning `SelectForAlignment`, `AiPursuitLaneMotivation`, or `AiPursuitLanePhysicalRelation`.

- [ ] **Step 3: Add the minimal typed model and physical relation resolver**

In `AiPursuitLaneSelector.cs`, add these public enums and properties rather than infer meaning from a nullable junction:

```csharp
public enum AiPursuitLaneMotivation
{
    FutureJunction,
    TargetLaneAlignment
}

public enum AiPursuitLanePhysicalRelation
{
    SameLane,
    ImmediateLeft,
    ImmediateRight,
    NonAdjacent,
    OppositeDirection,
    InvalidGeometry
}

public sealed record AiPursuitLaneSelection(/* existing fields */)
{
    public int? JunctionId { get; init; }
    public required AiPursuitLaneMotivation Motivation { get; init; }
    public required AiPursuitLanePhysicalRelation PhysicalRelation { get; init; }
}
```

Add `AiPursuitLaneEvaluationReason.NonAdjacent` and `InvalidGeometry` plus corresponding public diagnostic reasons in `AiPursuitControl.cs`. Extend the selector constructor used by production code with a relation delegate that checks both links: a left candidate must expose the current point as its right link; a right candidate must expose the current point as its left link. Keep `IsSameDirection` as a separate mandatory check. The synthetic constructor supplies that relation delegate so tests can model broken topology.

- [ ] **Step 4: Run focused tests and the complete selector suite**

Run:

```powershell
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj --filter "FullyQualifiedName~AiPursuitLaneSelectorTests"
```

Expected: PASS; all existing immediate-neighbor tests still prove that no point beyond `LeftId`/`RightId` is evaluated.

- [ ] **Step 5: Commit the typed evidence contract**

```powershell
git add AssettoServer/Server/Ai/Routing/AiPursuitLaneSelector.cs AssettoServer/Server/Ai/AiPursuitControl.cs AssettoServer.Tests/Server/Ai/AiPursuitLaneSelectorTests.cs
git commit -m "feat: classify pursuit lane adjacency"
```

### Task 2: Select direct target-lane alignment without weakening junction routing

**Files:**

- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitLaneSelector.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitLaneSelectorTests.cs`

**Interfaces:**

- Consumes: `AiPursuitLanePhysicalRelation` from Task 1 and `AiRouteSearchResult` for current/left/right forward routes.
- Produces: `SelectForAlignment(...)` and `SelectForRoutePreparation(...)`, each returning an `AiPursuitLaneSelectionResult` with a motivation and route evidence.

- [ ] **Step 1: Write failing tests that distinguish alignment from route preparation**

```csharp
[Test]
public void AlignmentAcceptsDirectNeighborRouteWithoutJunction()
{
    var result = CreatePhysicalSelector(leftOf0: 10, rightOf10: 0,
            sameDirection: true, currentPlan: null, leftPlan: Plan(94.6f))
        .SelectForAlignment(0, Targets, Limits, 60, 1000);

    Assert.Multiple(() =>
    {
        Assert.That(result.Kind, Is.EqualTo(AiPursuitLaneSelectionKind.Change));
        Assert.That(result.Selection!.Motivation,
            Is.EqualTo(AiPursuitLaneMotivation.TargetLaneAlignment));
        Assert.That(result.Selection.JunctionId, Is.Null);
        Assert.That(result.Selection.DistanceToDecisionMeters, Is.EqualTo(94.6f));
    });
}

[Test]
public void RoutePreparationStillRejectsSamePlanWhenNoJunctionExists()
{
    var result = CreatePhysicalSelector(leftOf0: 10, rightOf10: 0,
            sameDirection: true, currentPlan: null, leftPlan: Plan(94.6f))
        .SelectForRoutePreparation(0, Targets, Limits, 60, 1000);

    Assert.That(result.Reason, Is.EqualTo(AiPursuitLaneEvaluationReason.NoRealJunction));
}

[Test]
public void AlignmentChoosesShorterAdjacentRouteEvenWhenCurrentRouteIsValid()
{
    var result = CreatePhysicalSelector(leftOf0: 10, rightOf10: 0,
            sameDirection: true, currentPlan: Plan(850), leftPlan: Plan(94.6f))
        .SelectForAlignment(0, Targets, Limits, 60, 1000);

    Assert.That(result.Selection!.ToPointId, Is.EqualTo(10));
}

[Test]
public void AlignmentStaysWhenCurrentRouteIsNoLongerThanAdjacentRoute()
{
    var result = CreatePhysicalSelector(leftOf0: 10, rightOf10: 0,
            sameDirection: true, currentPlan: Plan(90), leftPlan: Plan(94.6f))
        .SelectForAlignment(0, Targets, Limits, 60, 1000);

    Assert.That(result.Kind, Is.EqualTo(AiPursuitLaneSelectionKind.Stay));
}
```

- [ ] **Step 2: Run the four named tests and verify they fail**

Run:

```powershell
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj --filter "FullyQualifiedName~AlignmentAcceptsDirectNeighborRouteWithoutJunction|FullyQualifiedName~RoutePreparationStillRejectsSamePlanWhenNoJunctionExists|FullyQualifiedName~AlignmentChoosesShorterAdjacentRouteEvenWhenCurrentRouteIsValid|FullyQualifiedName~AlignmentStaysWhenCurrentRouteIsNoLongerThanAdjacentRoute"
```

Expected: FAIL because the current selector treats every valid current route as `Stay` and every junction-free candidate as `NoRealJunction`.

- [ ] **Step 3: Implement separate selection entry points**

Refactor the existing `Select` body into a shared evaluator and expose these semantics:

```csharp
public AiPursuitLaneSelectionResult SelectForRoutePreparation(
    int currentPointId, IReadOnlySet<int> targetPointIds,
    AiRouteSearchLimits limits, float maneuverDistanceMeters, float lookaheadMeters);

public AiPursuitLaneSelectionResult SelectForAlignment(
    int currentPointId, IReadOnlySet<int> physicalTargetPointIds,
    AiRouteSearchLimits limits, float maneuverDistanceMeters, float lookaheadMeters);
```

For `TargetLaneAlignment`, always calculate current, left and right routes. Accept only a reciprocal same-direction neighbor whose route is valid, whose route distance is at least the physical maneuver distance and no more than lookahead, and whose route is strictly shorter than the current valid route (or valid when current has no plan). Use `route.Plan.DistanceMeters` as `DistanceToDecisionMeters`, set `JunctionId = null`, and set `Motivation = TargetLaneAlignment`.

For `FutureJunction`, preserve current behavior: a valid current route returns `Stay`; a candidate must contain the first real junction, and that junction remains the decision distance. Keep deterministic ordering: shortest candidate route wins; distances equal within `0.001f` return `Stay`.

Never call route planning for any non-immediate point. Keep the existing invalid-range validation for maneuver distance and lookahead.

- [ ] **Step 4: Run selector tests**

Run:

```powershell
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj --filter "FullyQualifiedName~AiPursuitLaneSelectorTests"
```

Expected: PASS, including the new same-lane proxy test, direct no-junction test, and legacy junction-only tests.

- [ ] **Step 5: Commit selector behavior**

```powershell
git add AssettoServer/Server/Ai/Routing/AiPursuitLaneSelector.cs AssettoServer.Tests/Server/Ai/AiPursuitLaneSelectorTests.cs
git commit -m "feat: select direct target lane alignment"
```

### Task 3: Make alignment persistent, one-step and route-safe in the pipeline

**Files:**

- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitLaneChangePipeline.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitNavigator.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitLaneChangePipelineTests.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitNavigatorTests.cs`

**Interfaces:**

- Consumes: `SelectForAlignment` and `SelectForRoutePreparation` from Task 2; `AiLaneChangeController.Phase`, `Prepare`, `TryGetCommittedRoute` and `ReconcileCurrentRoute`.
- Produces: `AiPursuitLanePreparationResult.Evaluation` whose selection represents one safe step toward the retained physical anchor.

- [ ] **Step 1: Write failing stateful pipeline tests**

```csharp
[Test]
public void ActiveFallbackPreparesDirectAlignmentForPhysicalAnchor()
{
    var result = CreatePipelineForDirectAlignment().Evaluate(
        0, 0, 100, ActiveFallback(), null, Options, Limits, 100);

    Assert.Multiple(() =>
    {
        Assert.That(result.RequestPrepared, Is.True);
        Assert.That(result.Evaluation.Selection!.Motivation,
            Is.EqualTo(AiPursuitLaneMotivation.TargetLaneAlignment));
        Assert.That(result.Evaluation.Selection.JunctionId, Is.Null);
    });
}

[Test]
public void PendingAlignmentDoesNotSkipPastImmediateNeighbor()
{
    var starts = new List<int>();
    var result = CreatePipelineWithTwoLeftSteps(starts).Evaluate(
        0, 0, 100, ActiveFallback(), null, Options, Limits, 100);

    Assert.That(result.Evaluation.Selection!.ToPointId, Is.EqualTo(10));
    Assert.That(starts, Does.Not.Contain(20));
}

[Test]
public void NearJunctionRoutePreparationWinsOverAlignment()
{
    var result = CreatePipelineWithRequiredJunctionAndOppositeAlignment().Evaluate(
        0, 0, 100, ActiveFallback(), null, Options, Limits, 100);

    Assert.That(result.Evaluation.Selection!.Motivation,
        Is.EqualTo(AiPursuitLaneMotivation.FutureJunction));
}

[Test]
public void CooldownPreventsImmediateAlignmentReversal()
{
    var controller = new AiLaneChangeController(60, 3000);
    var pipeline = CreatePipelineForDirectAlignment(controller);
    CompleteFirstChange(controller);

    var result = pipeline.Evaluate(10, 0, 100, ActiveFallback(), null, Options, Limits, 1000);

    Assert.That(result.Evaluation.Reason, Is.EqualTo(AiPursuitLaneEvaluationReason.Cooldown));
}
```

- [ ] **Step 2: Run focused pipeline tests and verify they fail**

Run:

```powershell
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj --filter "FullyQualifiedName~AiPursuitLaneChangePipelineTests"
```

Expected: the existing pipeline only calls the legacy selector and cannot report `TargetLaneAlignment`.

- [ ] **Step 3: Implement selection priority and retained-anchor behavior**

Keep `AiPursuitNavigator.SelectPreferredPhysicalTarget` as the only anchor-retention policy. Preserve its previous accepted spatial anchor while within its existing one-meter tolerance; add tests that assert that exact policy rather than a second competing hysteresis state.

In `AiPursuitLaneChangePipeline.Evaluate`, after the committed route branch and before `Prepare`:

```csharp
var routePreparation = _selector.SelectForRoutePreparation(/* physical target */);
var alignment = _selector.SelectForAlignment(/* same physical target */);
var evaluation = SelectPriority(routePreparation, alignment);
```

Implement `SelectPriority` with this exact order:

1. Return a `Change` from `routePreparation` when its junction is within the maneuver/lookahead window.
2. Otherwise return a `Change` from `alignment`.
3. Otherwise return `alignment` if it contains richer current/candidate evidence; preserve the route-preparation rejection in its candidate diagnostics when it is the only failure.

Do not prepare a new request while the controller has a committed or changing route. Let the existing controller finish; after completion it adopts the destination cursor, normal movement updates `CurrentSplinePointId`, and the next evaluation may choose the next immediate alignment step. Retain existing cooldown handling exactly: when `Prepare` returns false with `Cooldown`, overwrite only `evaluation.Reason`.

- [ ] **Step 4: Run pipeline and navigator tests**

Run:

```powershell
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj --filter "FullyQualifiedName~AiPursuitLaneChangePipelineTests|FullyQualifiedName~AiPursuitNavigatorTests"
```

Expected: PASS; completion, committed route retention, grace and the original P0.6 equivalent target behavior remain green.

- [ ] **Step 5: Commit stateful pipeline behavior**

```powershell
git add AssettoServer/Server/Ai/Routing/AiPursuitLaneChangePipeline.cs AssettoServer/Server/Ai/Routing/AiPursuitNavigator.cs AssettoServer.Tests/Server/Ai/AiPursuitLaneChangePipelineTests.cs AssettoServer.Tests/Server/Ai/AiPursuitNavigatorTests.cs
git commit -m "feat: retain route-safe target lane alignment"
```

### Task 4: Publish motivation and relation in core diagnostics

**Files:**

- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitLaneDiagnosticTracker.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiState.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitLaneDiagnosticsTests.cs`

**Interfaces:**

- Consumes: `AiPursuitLaneSelection.Motivation`, `PhysicalRelation`, enriched route diagnostics and controller events.
- Produces: `AiPursuitLaneChangeDiagnostics.Motivation` and `.PhysicalRelation` for the plugin adapter.

- [ ] **Step 1: Write failing diagnostics tests**

```csharp
[Test]
public void MotivationChangeRepublishesEvaluationWithoutDistanceSpam()
{
    var tracker = new AiPursuitLaneDiagnosticTracker();
    tracker.PublishEvaluation(0, 99, 4, Evaluation(500,
        motivation: AiPursuitLaneMotivation.TargetLaneAlignment));

    var duplicate = tracker.PublishEvaluation(0, 99, 4, Evaluation(498,
        motivation: AiPursuitLaneMotivation.TargetLaneAlignment));
    var changed = tracker.PublishEvaluation(0, 99, 4, Evaluation(498,
        motivation: AiPursuitLaneMotivation.FutureJunction));

    Assert.That(duplicate, Is.Null);
    Assert.That(changed, Is.Not.Null);
}

[Test]
public void DirectAlignmentDiagnosticHasNoJunctionButKeepsRouteEvidence()
{
    var diagnostic = new AiPursuitLaneDiagnosticTracker().PublishEvaluation(
        171761, 283943, 1, DirectAlignmentEvaluation());

    Assert.Multiple(() =>
    {
        Assert.That(diagnostic!.JunctionId, Is.Null);
        Assert.That(diagnostic.Motivation,
            Is.EqualTo(AiPursuitLaneMotivation.TargetLaneAlignment));
        Assert.That(diagnostic.CandidateLaneRoutes.Single().RouteDistanceMeters,
            Is.EqualTo(94.6f));
    });
}
```

- [ ] **Step 2: Run focused diagnostics tests and verify they fail**

Run:

```powershell
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj --filter "FullyQualifiedName~AiPursuitLaneDiagnosticsTests"
```

Expected: compilation failure because the public diagnostic has no motivation/relation fields.

- [ ] **Step 3: Extend diagnostic data and semantic signature**

Add nullable properties to `AiPursuitLaneChangeDiagnostics`:

```csharp
public AiPursuitLaneMotivation? Motivation { get; init; }
public AiPursuitLanePhysicalRelation? PhysicalRelation { get; init; }
```

Set both in `PublishEvaluation`; leave them null for disabled/no-target gates and pure controller lifecycle events that have no retained evaluation. Include both in `SemanticKey` and include motivation/relation in `CreateCandidateEvidence`. Keep distances out of the key so 500 m to 498 m remains one log state. In `AiState.CreateLaneChangeDiagnostics`, preserve the latest evaluation context for a controller event so `Requested`, `Started`, `Completed`, `Cancelled` can still identify an alignment request without inventing a junction.

- [ ] **Step 4: Run diagnostics and controller suites**

Run:

```powershell
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj --filter "FullyQualifiedName~AiPursuitLaneDiagnosticsTests|FullyQualifiedName~AiLaneChangeControllerTests|FullyQualifiedName~AiLaneChangeSafetyTests"
```

Expected: PASS; one log-worthy diagnostic for static meaning, a new one when motivation/relation changes, and unchanged safety semantics.

- [ ] **Step 5: Commit core diagnostics**

```powershell
git add AssettoServer/Server/Ai/AiPursuitControl.cs AssettoServer/Server/Ai/AiPursuitLaneDiagnosticTracker.cs AssettoServer/Server/Ai/AiState.cs AssettoServer.Tests/Server/Ai/AiPursuitLaneDiagnosticsTests.cs
git commit -m "feat: diagnose target lane alignment"
```

### Task 5: Lock the exact SRP regressions and physical completion

**Files:**

- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/RealSplineTransitionIntegrationTests.cs`

**Interfaces:**

- Consumes: the production `AiSpline`, `AiRoutePlanner`, `AiPursuitLaneSelector`, `AiPursuitLaneChangePipeline`, `AiLaneChangeController` and the developer-supplied `C:\Users\arthur.carlos\Downloads\fast_lane.aip` fixture path already used by the real-spline tests.
- Produces: a stable test proving the three server-log pairs request and complete direct target-lane alignment without a junction.

- [ ] **Step 1: Write failing parameterized real-spline regression tests**

```csharp
[TestCase(171761, 283881, 283943)]
[TestCase(171763, 283883, 283945)]
[TestCase(171765, 283885, 283947)]
public void DirectPhysicalTargetUsesReciprocalAdjacentLaneWithoutJunction(
    int policePointId, int adjacentPointId, int targetPointId)
{
    var spline = LoadRealFastLaneSpline();
    var planner = new AiRoutePlanner(spline);
    var selector = new AiPursuitLaneSelector(spline, planner);

    var result = selector.SelectForAlignment(
        policePointId, new HashSet<int> { targetPointId },
        new AiRouteSearchLimits(20_000, 50_000), 60, 1000);

    Assert.Multiple(() =>
    {
        Assert.That(result.Selection!.ToPointId, Is.EqualTo(adjacentPointId));
        Assert.That(result.Selection.Motivation,
            Is.EqualTo(AiPursuitLaneMotivation.TargetLaneAlignment));
        Assert.That(result.Selection.JunctionId, Is.Null);
        Assert.That(result.Selection.DistanceToDecisionMeters, Is.InRange(90, 100));
    });
}
```

Add a second test that prepares the first case, updates waiting safety with `Safe`, calls `TryMove` until `Completed`, and asserts the completion destination is `283881` without a teleport or a route-graph lateral edge.

- [ ] **Step 2: Run only the real direct-target tests and verify current code fails**

Run:

```powershell
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj --filter "FullyQualifiedName~DirectPhysicalTargetUsesReciprocalAdjacentLaneWithoutJunction|FullyQualifiedName~DirectPhysicalTargetCompletes"
```

Expected: FAIL on `NoRealJunction` before Task 2 is implemented; PASS only after Tasks 1–4 are complete.

- [ ] **Step 3: Add topology assertions, not ID-specific production behavior**

In each test assert `LeftId`/`RightId` reciprocity and `IsSameDirection`; calculate route evidence with `AiRoutePlanner`. Keep all IDs in tests only. Do not branch production code by spline name, point ID, lane count, map name or route distance.

- [ ] **Step 4: Run the entire real-spline integration fixture**

Run:

```powershell
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj --filter "FullyQualifiedName~RealSplineTransitionIntegrationTests"
```

Expected: PASS; retain the previous `312936 -> 311797` junction-preparation regression and the `171751` topology diagnostic.

- [ ] **Step 5: Commit real-world regression coverage**

```powershell
git add AssettoServer.Tests/Server/Ai/RealSplineTransitionIntegrationTests.cs
git commit -m "test: cover direct SRP police lane alignment"
```

### Task 6: Map core diagnostics into the plugin and keep logs readable

**Files:**

- Modify: `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs`
- Test: `tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiStateTests.cs`
- Test: `tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs`

**Interfaces:**

- Consumes: core `AiPursuitLaneMotivation`, `AiPursuitLanePhysicalRelation`, and enriched `AiPursuitLaneChangeDiagnostics` from Task 4.
- Produces: `PolicePursuitLaneChangeDiagnostics.Motivation`/`.PhysicalRelation` and exactly-once semantically distinct log lines.

- [ ] **Step 1: Write failing adapter and logging tests**

```csharp
[Test]
public void MapsDirectAlignmentDiagnosticWithoutInventingJunction()
{
    var mapped = AssettoServerPoliceAiState.MapLaneChangeDiagnostics(
        CoreDirectAlignmentDiagnostic());

    Assert.Multiple(() =>
    {
        Assert.That(mapped.Motivation,
            Is.EqualTo(PolicePursuitLaneMotivation.TargetLaneAlignment));
        Assert.That(mapped.PhysicalRelation,
            Is.EqualTo(PolicePursuitLanePhysicalRelation.ImmediateLeft));
        Assert.That(mapped.JunctionId, Is.Null);
    });
}

[Test]
public void AlignmentEvaluationLogsOnceUntilItsMotivationChanges()
{
    var context = CreateContext();
    context.State.Enqueue(ActiveResult(AlignmentDiagnostics(distance: 95)));
    context.State.Enqueue(ActiveResult(AlignmentDiagnostics(distance: 93)));
    context.State.Enqueue(ActiveResult(JunctionDiagnostics(distance: 93)));

    context.Service.UpdateOnce();
    context.Service.UpdateOnce();
    context.Service.UpdateOnce();

    Assert.That(LogCount("Lane change evaluation"), Is.EqualTo(2));
}
```

- [ ] **Step 2: Run focused plugin tests and verify they fail**

Run:

```powershell
dotnet test .\PoliceChasePlugin.sln --filter "FullyQualifiedName~AssettoServerPoliceAiStateTests|FullyQualifiedName~PolicePursuitServiceTests"
```

Expected: compilation failure because plugin contract types do not contain motivation/relation.

- [ ] **Step 3: Mirror the contract exhaustively and update log signature**

Add matching plugin enums:

```csharp
public enum PolicePursuitLaneMotivation { FutureJunction, TargetLaneAlignment }
public enum PolicePursuitLanePhysicalRelation
{
    SameLane, ImmediateLeft, ImmediateRight, NonAdjacent, OppositeDirection, InvalidGeometry
}
```

Add nullable properties to `PolicePursuitLaneChangeDiagnostics`. Extend `MapLaneChangeDiagnostics`, `MapLaneChangeReason` and `MapLaneEvaluationReason` with exhaustive switch arms for every new core value. Extend `LaneChangeLogSignature` with motivation and relation. Render them in the existing evaluation line, for example:

```text
[PoliceChase] Lane change evaluation: TargetLaneAlignment, relation ImmediateLeft, from 171761 to 283881, target 283943, route 94.6 m, junction none
```

Do not log distances in the signature; a distance-only change must remain silent. Keep all existing `Required`, `Waiting`, `Started`, `Completed`, `Cancelled` wording and append the new fields only when present.

- [ ] **Step 4: Run all plugin tests**

Run:

```powershell
dotnet test .\PoliceChasePlugin.sln
```

Expected: PASS; the adapter has no unmapped enum value and logs keep their current one-event semantics.

- [ ] **Step 5: Commit plugin contract and logs**

```powershell
git add src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiStateTests.cs tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs
git commit -m "feat: log police target lane alignment"
```

### Task 7: Document, validate, publish and prepare server test

**Files:**

- Modify: `docs/P0.7-route-aware-lane-changing.md`
- Modify: `docs/superpowers/specs/2026-09-19-police-chase-p0-7-target-lane-alignment-design.md` only if the final names differ from this plan; otherwise leave the approved spec immutable.

**Interfaces:**

- Consumes: final public core/plugin diagnostics and test results.
- Produces: deployment instructions that reference the exact commits and an expected log checklist for the real server.

- [ ] **Step 1: Update P0.7 operational documentation**

Document these final assertions:

```markdown
- `TargetLaneAlignment` means an immediate physical neighbor has a shorter valid forward route to the player's physical anchor; it does not require a junction.
- `FutureJunction` still requires and reports a real junction.
- The police AI changes exactly one reciprocal neighbor per maneuver and never adds lateral graph edges.
- `JunctionId=none` is expected for a direct alignment request.
- `Waiting` with an obstacle reason is safe behavior, not a failed request.
```

Add the three real IDs as regression examples only, clearly stating that production behavior is topology-based and has no map-specific branches.

- [ ] **Step 2: Run formatting, focused suites and full test suites**

Run from the submodule:

```powershell
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj --filter "FullyQualifiedName~AiPursuitLane|FullyQualifiedName~AiLaneChange|FullyQualifiedName~RealSplineTransitionIntegrationTests"
dotnet test .\AssettoServer.Tests\AssettoServer.Tests.csproj
dotnet build .\AssettoServer.sln -c Release --no-restore
```

Run from the parent repository:

```powershell
dotnet test .\PoliceChasePlugin.sln
dotnet build .\PoliceChasePlugin.sln -c Release --no-restore
git diff --check
git status --short
git -C external/AssettoServer status --short
```

Expected: all tests pass, both Release builds finish with zero errors, `git diff --check` is clean, and only intended documentation/submodule changes remain before their commits.

- [ ] **Step 3: Commit documentation in the parent repository**

```powershell
git add docs/P0.7-route-aware-lane-changing.md
git commit -m "docs: explain police target lane alignment"
```

- [ ] **Step 4: Publish the core before changing the parent pointer**

From `external/AssettoServer`, capture `git rev-parse HEAD`, push `codex/p0.7-real-lane-change-fix` to the existing `arthur` remote, then record the exact returned hash in the delivery note. Do not force-push.

- [ ] **Step 5: Update and publish the parent submodule pointer**

From the parent repository:

```powershell
git add external/AssettoServer
git commit -m "chore: update AssettoServer target lane alignment core"
git push origin main
```

Then verify:

```powershell
git status --short
git submodule status
git log -1 --oneline
git -C external/AssettoServer log -1 --oneline
```

Expected: both working trees are clean and `main` points to the published core commit.

- [ ] **Step 6: Provide the server validation checklist without modifying the server**

Ask the server operator to pull the published `main`, rebuild/install the artifact using the established backup process, then capture these lines:

```text
TargetLaneAlignment ... relation ImmediateLeft|ImmediateRight ... junction none
Lane change required
Lane change started
Lane change completed
```

The report must also state whether the Skoda changes one lane at a time toward the player, whether it stays active when safely blocked, whether it avoids divider ping-pong, and whether the ten normal traffic AIs remain present. Do not claim P0.7 complete until that real-server result is supplied.

## Self-Review

### Spec coverage

- Direct `NoRealJunction` root cause: Tasks 1, 2 and 5.
- Persistent target-lane preference while preserving routing: Tasks 2 and 3.
- Immediate reciprocal physical-only transition: Tasks 1, 2, 3 and 5.
- Safety, cooldown and physical controller reuse: Task 3 plus existing controller/safety suites in Tasks 4 and 7.
- No global traffic changes, no graph lateral edges and no teleport: Global Constraints plus Task 5 topology/controller assertions.
- Typed diagnostics and non-spam logs: Tasks 4 and 6.
- Three exact real pairs: Task 5.
- Plugin compatibility and server evidence: Tasks 6 and 7.
- Future collision/PIT explicitly excluded: Global Constraints and Task 7 documentation.

### Completeness scan

Each implementation task lists concrete methods, types, test names and commands; no task defers a requirement or refers to an unspecified test action.

### Type consistency

`AiPursuitLaneMotivation` and `AiPursuitLanePhysicalRelation` originate in Task 1, are selected in Task 2, consumed by Task 3, published by Task 4, mapped to `PolicePursuitLaneMotivation`/`PolicePursuitLanePhysicalRelation` in Task 6, and documented in Task 7. `SelectForAlignment` and `SelectForRoutePreparation` are created in Task 2 before Task 3 calls them.

### Review Focus coverage

- Valid but longer current route: Task 2 `AlignmentChoosesShorterAdjacentRouteEvenWhenCurrentRouteIsValid`.
- Same physical lane: Task 2 `AlignmentStaysWhenCurrentRouteIsNoLongerThanAdjacentRoute`.
- Multi-lane target: Task 3 `PendingAlignmentDoesNotSkipPastImmediateNeighbor`.
- Divider oscillation: Task 3 navigator retention coverage and cooldown reversal test.
- Junction-free direct route: Task 2 alignment/route-preparation contrast and Task 5 real-spline cases.
