# Police Chase P0.7 Real Lane Change Correction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer a Skoda policial preparar e executar uma troca física de faixa antes de uma junction necessária, mesmo quando a navegação fallback da P0.6 ainda encontra uma faixa equivalente alcançável.

**Architecture:** O locator separará uma âncora física estável do conjunto expandido de destinos da P0.6. O selector avaliará essa âncora proativamente, com junction real, lookahead e diagnóstico tipado; `AiState` alimentará o controlador físico P0.7 já existente mesmo quando o navigator fallback estiver ativo. O plugin mapeará os novos contratos, validará o lookahead e registrará apenas mudanças semânticas.

**Tech Stack:** C# 12, .NET 8, NUnit, AssettoServer 0.0.54 fork, FluentValidation e Serilog.

**Spec:** `docs/superpowers/specs/2026-09-19-police-chase-p0-7-real-lane-change-correction-design.md`

## Global Constraints

- PoliceChasePlugin base: `7c3f2f8778fa77c3c9eb3d5a870de565438d9947` em `main`.
- AssettoServer base: `539fcbe65c45f237d51fed45400121d8aae33cea`.
- Desenvolver o core em `codex/p0.7-real-lane-change-fix`.
- Preservar os testes diagnósticos já não commitados em `RealSplineTransitionIntegrationTests.cs`.
- Manter `PursuitRouteSearchMaxDistanceMeters=20000` e `PursuitRouteSearchMaxVisitedNodes=50000`.
- Não adicionar `LeftId` ou `RightId` ao `AiRouteGraph`.
- Não usar reflection, teleport, snap, reverse routing, respawn ou edição do `fast_lane.aip`.
- Não alterar parâmetros globais de tráfego, overbooking, reserva dedicada ou os CAR_0..CAR_11.
- Manter a expansão de equivalentes, grace, no-route e probe da P0.6.
- Uma manobra atravessa exatamente uma adjacência e usa o `AiLaneChangeController` existente.
- Entregar como candidata ao teste visual real; testes automatizados não concluem a P0.7.

## File Structure

### Core AssettoServer

- Modify `AssettoServer/Server/Ai/Routing/AiPursuitTargetLocator.cs`: selecionar e expor a âncora física antes de expandir equivalentes.
- Modify `AssettoServer/Server/Ai/Routing/AiPursuitNavigator.cs`: reter âncora estável e carregá-la junto da navegação fallback.
- Modify `AssettoServer/Server/Ai/Routing/AiPursuitLaneSelector.cs`: exigir junction real, aplicar lookahead e devolver evidência de cada rota.
- Create `AssettoServer/Server/Ai/Routing/AiPursuitLaneChangePipeline.cs`: coordenar avaliação proativa e preparação da request sem depender do status fallback.
- Create `AssettoServer/Server/Ai/AiPursuitLaneDiagnosticTracker.cs`: deduplicar avaliações por assinatura semântica.
- Modify `AssettoServer/Server/Ai/AiPursuitControl.cs`: opções com lookahead e diagnóstico público tipado.
- Modify `AssettoServer/Server/Ai/AiState.cs`: chamar o pipeline em toda avaliação, integrar safety/controlador e publicar diagnóstico semântico.
- Modify corresponding tests under `AssettoServer.Tests/Server/Ai/`.

### PoliceChasePlugin

- Modify `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`: adicionar default do lookahead.
- Modify `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs`: validar intervalo e relação com a distância física.
- Modify `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`: espelhar opções e diagnósticos tipados.
- Modify `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`: mapear os contratos sem reflection.
- Modify `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs`: transportar lookahead e deduplicar logs por assinatura semântica.
- Modify `src/PoliceChasePlugin/PoliceChaseService.cs`: logar configuração efetiva no startup.
- Modify corresponding tests under `tests/PoliceChasePlugin.Tests/`.
- Modify `docs/P0.7-route-aware-lane-changing.md`: causa, configuração, evidências, logs e checklist real.

---

### Task 1: Âncora física separada dos equivalentes P0.6

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitTargetLocator.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitNavigator.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitTargetLocatorTests.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitNavigatorTests.cs`

**Interfaces:**
- Produces: `AiPursuitTargetLocationResult.PreferredPhysicalTargetPointId` (`int?`).
- Produces: `AiPursuitTargetLocationResult.AcceptedSpatialCandidates` para a histerese sem confundir equivalentes.
- Produces: `AiPursuitNavigationResult.PreferredPhysicalTargetPointId` (`int?`).
- Produces: `AiPursuitRouteState.PreferredPhysicalTargetPointId` (`int?`) para histerese entre updates.
- Preserves: `Candidates` e `TargetPointIds` continuam contendo os equivalentes da P0.6.

- [ ] **Step 1: Escrever testes failing para distinção e estabilidade**

Adicionar ao locator um caso em que o ponto espacial `20` é mais próximo e o equivalente `10` é alcançável pelo fallback:

```csharp
[Test]
public void KeepsPhysicalAnchorSeparateFromLaneEquivalentCandidates()
{
    var locator = CreateLocator(
        spatial: [Source(20, 1)],
        equivalents: [Source(10, 9)]);

    var result = locator.LocateCandidates(Vector3.Zero, Vector3.Zero, 100, 8);

    Assert.That(result.PreferredPhysicalTargetPointId, Is.EqualTo(20));
    Assert.That(result.Candidates.Select(candidate => candidate.PointId),
        Is.EquivalentTo(new[] { 20, 10 }));
}
```

Adicionar ao navigator um teste com dois updates em que a melhor semente muda por menos de `1 m`, afirmando que a âncora anterior é retida, e outro em que ela fica inválida, afirmando seleção da nova âncora.

- [ ] **Step 2: Executar os testes e confirmar RED**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AiPursuitTargetLocatorTests|FullyQualifiedName~AiPursuitNavigatorTests"
```

Expected: falha de compilação porque `PreferredPhysicalTargetPointId` ainda não existe.

- [ ] **Step 3: Implementar a âncora no locator**

Alterar o resultado:

```csharp
public sealed record AiPursuitTargetLocationResult(
    IReadOnlyList<AiPursuitTargetCandidate> Candidates,
    IReadOnlyList<AiPursuitTargetCandidate> AcceptedSpatialCandidates,
    int? PreferredPhysicalTargetPointId,
    AiPursuitTargetLocationDiagnostics Diagnostics);
```

Durante o loop de `orderedSpatialSources`, adicionar somente os `spatialCandidate` aceitos a `AcceptedSpatialCandidates` e guardar o primeiro como âncora. Nunca promover um item retornado por `_findLaneEquivalents` a âncora física.

- [ ] **Step 4: Implementar histerese no navigator**

Adicionar `PreferredPhysicalTargetPointId` ao estado/resultado. Antes de publicar o resultado, reter a âncora anterior quando ela ainda estiver entre os candidatos espaciais aceitos e sua distância até o alvo estiver no máximo `1 m` pior que a melhor semente; caso contrário, usar a âncora atual do locator. `TargetPointIds` permanece o conjunto completo.

- [ ] **Step 5: Rodar testes focados e regressão P0.6**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AiPursuitTargetLocatorTests|FullyQualifiedName~AiPursuitNavigatorTests|FullyQualifiedName~AiRoutePlannerTests"
```

Expected: todos passam; `ExpandsAcceptedSpatialSeedsToLaneEquivalentPoints` continua verde.

- [ ] **Step 6: Commitar o contrato de localização**

```powershell
git -C external/AssettoServer add -- AssettoServer/Server/Ai/Routing/AiPursuitTargetLocator.cs AssettoServer/Server/Ai/Routing/AiPursuitNavigator.cs AssettoServer.Tests/Server/Ai/AiPursuitTargetLocatorTests.cs AssettoServer.Tests/Server/Ai/AiPursuitNavigatorTests.cs
git -C external/AssettoServer commit -m "fix(ai): separate physical pursuit target anchor"
```

### Task 2: Selector antecipado, limitado por junction e lookahead

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitLaneSelector.cs`
- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitLaneSelectorTests.cs`

**Interfaces:**
- Consumes: conjunto contendo somente a âncora física preferencial.
- Produces: `Select(int currentPointId, IReadOnlySet<int> physicalTargetPointIds, AiRouteSearchLimits limits, float maneuverDistanceMeters, float lookaheadMeters)`.
- Produces: `AiPursuitLaneEvaluationReason`, `AiPursuitLaneRouteDiagnostic` e `JunctionId` na seleção.

- [ ] **Step 1: Escrever testes failing para junction, lookahead e evidência**

Adicionar casos:

```csharp
[Test]
public void SelectsAdjacentLaneWhenFallbackEquivalentWouldMaskPhysicalTarget()
{
    var selector = CreateSelector(
        currentRoute: Failure(AiRouteSearchFailure.Unreachable, 80, 0),
        leftRoute: Success(PlanWithJunction(junctionId: 2, distanceToDecision: 420)));

    var result = selector.Select(0, new HashSet<int> { 99 }, Limits, 60, 1000);

    Assert.That(result.Kind, Is.EqualTo(AiPursuitLaneSelectionKind.Change));
    Assert.That(result.Selection!.JunctionId, Is.EqualTo(2));
    Assert.That(result.Reason, Is.EqualTo(AiPursuitLaneEvaluationReason.RoutePreparation));
}
```

Cobrir também:

- plano adjacente sem junction real retorna `NoRealJunction`;
- junction a `1001 m` retorna `BeyondLookahead`;
- junction a menos de `60 m` retorna `InsufficientPreparationDistance`;
- faixa atual alcançável retorna `CurrentLaneValid`;
- nenhuma adjacente alcançável inclui failure, distância explorada e junction count no diagnóstico;
- empate entre esquerda/direita não gera mudança.

- [ ] **Step 2: Executar e confirmar RED**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AiPursuitLaneSelectorTests
```

Expected: falha de compilação nas novas assinaturas/tipos.

- [ ] **Step 3: Implementar tipos de diagnóstico e seleção**

```csharp
public enum AiPursuitLaneEvaluationReason
{
    Disabled,
    NoPhysicalTarget,
    CurrentLaneValid,
    NoAdjacentLane,
    OppositeDirection,
    NoForwardRoute,
    NoRealJunction,
    BeyondLookahead,
    InsufficientPreparationDistance,
    RoutePreparation
}

public sealed record AiPursuitLaneRouteDiagnostic(
    int PointId,
    AiLaneChangeDirection? Direction,
    AiRouteSearchFailure SearchFailure,
    float? RouteDistanceMeters,
    float MaximumExploredDistanceMeters,
    int JunctionEdgesExamined,
    int? JunctionId,
    float? DistanceToDecisionMeters,
    AiPursuitLaneEvaluationReason Reason);
```

Estender `AiPursuitLaneSelection` com `int JunctionId`. Estender o resultado com `Reason`, diagnóstico da faixa atual e diagnósticos das adjacentes.

- [ ] **Step 4: Implementar limites sem criar aresta lateral**

O selector deve chamar `_tryPlan` separadamente para current/left/right. A adjacente só é selecionável quando seu `AiRoutePlan` contém uma decisão correspondente a um `JunctionStartId` real. Rejeitar antes de `maneuverDistanceMeters` e depois de `lookaheadMeters`. Preservar a regra de uma adjacência e preferência pela faixa atual.

- [ ] **Step 5: Rodar testes e commit**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AiPursuitLaneSelectorTests|FullyQualifiedName~AiRoutePlannerTests"
git -C external/AssettoServer add -- AssettoServer/Server/Ai/Routing/AiPursuitLaneSelector.cs AssettoServer.Tests/Server/Ai/AiPursuitLaneSelectorTests.cs
git -C external/AssettoServer commit -m "fix(ai): evaluate pursuit lanes before junctions"
```

Expected: todos passam e nenhum teste cria edge por `LeftId`/`RightId`.

### Task 3: Pipeline proativo que emite request com fallback ativo

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitLaneChangePipeline.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitLaneChangePipelineTests.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`

**Interfaces:**
- Consumes: `AiPursuitNavigationResult`, âncora física, `AiPursuitLaneSelector`, cursores e `AiLaneChangeController`.
- Produces: `AiPursuitLanePreparationResult` com navegação efetiva, avaliação e indicação de request preparada.
- Produces: `AiPursuitLaneChangeOptions(bool Enabled, float DistanceMeters, int CooldownMilliseconds, float LookaheadMeters)`.

- [ ] **Step 1: Escrever o teste end-to-end failing do pipeline de decisão**

Construir locator/navigator/selector/controlador reais sobre uma spline pequena em que:

- a faixa `0 -> 1 -> 2` alcança um equivalente fallback;
- a âncora física está na faixa `10 -> 11`;
- somente `10` alcança a âncora por uma junction real;
- o navigator retorna `Active` pela faixa atual.

```csharp
[Test]
public void ActiveFallbackStillPreparesRequestForPhysicalAnchor()
{
    var navigation = NavigateToEquivalentFallback();
    Assert.That(navigation.Status, Is.EqualTo(AiPursuitNavigationStatus.Active));

    var result = pipeline.Evaluate(CurrentPoint, navigation, Options, Limits, now: 100);

    Assert.That(result.RequestPrepared, Is.True);
    Assert.That(result.EffectiveNavigation.State!.Plan.JunctionDecisions,
        Contains.Key(ExpectedJunctionId));
    Assert.That(controller.Phase, Is.EqualTo(AiLaneChangePhase.WaitingForGap));
}
```

Adicionar os seguintes testes com assertions explícitas:

```csharp
[Test]
public void CurrentLaneValidDoesNotPrepareRequest() =>
    Assert.That(EvaluateCurrentLaneValid().RequestPrepared, Is.False);

[Test]
public void UnsafeGapKeepsPreparedRequestWaiting() =>
    Assert.That(EvaluateWithSafety(AiLaneChangeSafetyStatus.BlockedSide).Phase,
        Is.EqualTo(AiLaneChangePhase.WaitingForGap));

[Test]
public void ClearingGapStartsPreviouslyPreparedRequest() =>
    Assert.That(EvaluateBlockedThenSafe().Phase,
        Is.EqualTo(AiLaneChangePhase.Changing));

[Test]
public void CooldownDoesNotPrepareOppositeRequest() =>
    Assert.That(EvaluateDuringCooldown().RequestPrepared, Is.False);

[Test]
public void CompletedDestinationDoesNotImmediatelyRequestReturn() =>
    Assert.That(EvaluateAfterCompletion().Evaluation.Kind,
        Is.EqualTo(AiPursuitLaneSelectionKind.Stay));

[Test]
public void NoReachableAdjacentLaneDoesNotPrepareRequest() =>
    Assert.That(EvaluateWithoutAlternative().Evaluation.Reason,
        Is.EqualTo(AiPursuitLaneEvaluationReason.NoForwardRoute));

[Test]
public void RouteRevisionCancelsWaitingRequest() =>
    Assert.That(EvaluateRevisionWhileWaiting().ControllerEvent!.Kind,
        Is.EqualTo(AiPursuitLaneChangeEventKind.RouteRevised));

[Test]
public void RouteRevisionDoesNotInterruptCommittedChange() =>
    Assert.That(EvaluateRevisionWhileChanging().Phase,
        Is.EqualTo(AiLaneChangePhase.Changing));
```

Os casos de obstacle usam o `AiLaneChangeController` e `AiLaneChangeSafetyStatus` reais; não usam um boolean `safe` artificial.

- [ ] **Step 2: Executar e confirmar RED**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AiPursuitLaneChangePipelineTests
```

Expected: falha de compilação porque o pipeline e `LookaheadMeters` não existem.

- [ ] **Step 3: Implementar o pipeline mínimo**

```csharp
internal sealed record AiPursuitLanePreparationResult(
    AiPursuitNavigationResult EffectiveNavigation,
    AiPursuitLaneSelectionResult Evaluation,
    bool RequestPrepared,
    AiLaneChangePhase ControllerPhase,
    AiLaneChangeEvent? ControllerEvent);
```

`Evaluate(...)` deve:

1. retornar `Disabled` ou `NoPhysicalTarget` sem tocar no controlador;
2. chamar o selector com um conjunto de um único ID físico mesmo quando `navigation.Status == Active`;
3. manter a navegação fallback quando a avaliação não pede mudança;
4. preparar os cursores e o controlador quando recebe `Change`;
5. substituir a navegação efetiva pelo `DestinationPlan` somente após uma request aceita ou enquanto uma mudança committed estiver ativa;
6. reconciliar revisão em waiting/changing com as regras atuais.

- [ ] **Step 4: Validar opções do core**

Em `AiPursuitControl.ValidateTrackingOptions`, exigir `LookaheadMeters` finito, entre `100` e `5000`, e maior ou igual a `DistanceMeters` quando enabled.

- [ ] **Step 5: Rodar testes do pipeline/controlador e commit**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AiPursuitLaneChangePipelineTests|FullyQualifiedName~AiLaneChangeControllerTests|FullyQualifiedName~AiPursuitControlTests"
git -C external/AssettoServer add -- AssettoServer/Server/Ai/Routing/AiPursuitLaneChangePipeline.cs AssettoServer/Server/Ai/AiPursuitControl.cs AssettoServer.Tests/Server/Ai/AiPursuitLaneChangePipelineTests.cs AssettoServer.Tests/Server/Ai/AiPursuitControlTests.cs
git -C external/AssettoServer commit -m "fix(ai): prepare lane changes with active fallback"
```

### Task 4: Integrar pipeline e diagnóstico semântico no `AiState`

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiState.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`
- Create: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitLaneDiagnosticTracker.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitLaneDiagnosticsTests.cs`
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiLaneChangeSafetyTests.cs`

**Interfaces:**
- Produces: `AiPursuitLaneChangeDiagnosticReason` e `AiPursuitLaneChangeDiagnostics` com evidência de current/left/right.
- Preserves: eventos físicos `Required`, `Waiting`, `Started`, `Completed`, `Cancelled`, `RouteRevised`.

- [ ] **Step 1: Escrever testes failing de assinatura semântica**

```csharp
[Test]
public void MovingDistanceDoesNotRepublishSameSemanticEvaluation()
{
    var tracker = new AiPursuitLaneDiagnosticTracker();
    var first = tracker.Publish(Evaluation(distanceToDecision: 500));
    var duplicate = tracker.Publish(Evaluation(distanceToDecision: 498));

    Assert.That(first, Is.Not.Null);
    Assert.That(duplicate, Is.Null);
}
```

Adicionar testes que exigem nova revisão quando mudam reason, âncora, candidata, junction, safety ou fase, e que eventos físicos sempre aparecem uma vez.

- [ ] **Step 2: Executar e confirmar RED**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AiPursuitLaneDiagnosticsTests|FullyQualifiedName~AiLaneChangeSafetyTests"
```

Expected: falha de compilação nos novos tipos/tracker.

- [ ] **Step 3: Implementar contrato público tipado**

Substituir `string? BlockingReason` por reason/safety tipados e incluir:

```csharp
public enum AiPursuitLaneChangeDiagnosticReason
{
    Disabled,
    NoPhysicalTarget,
    CurrentLaneValid,
    NoAdjacentLane,
    OppositeDirection,
    NoForwardRoute,
    NoRealJunction,
    BeyondLookahead,
    InsufficientPreparationDistance,
    Cooldown,
    ObstacleAhead,
    ObstacleAlongside,
    ObstacleBehind,
    RouteRevisionChanged,
    Requested,
    Started,
    Completed,
    Cancelled
}

// Add Evaluated to the existing lifecycle event enum; lifecycle values stay unchanged.
public enum AiPursuitLaneChangeEventKind
{
    Evaluated,
    Required,
    Waiting,
    Started,
    Completed,
    Cancelled,
    RouteRevised
}

public sealed record AiPursuitLaneChangeDiagnostics(
    long Revision,
    AiPursuitLaneChangeEventKind EventKind,
    AiPursuitLaneChangeDiagnosticReason Reason,
    int PolicePointId,
    int? PreferredPhysicalTargetPointId,
    int? FromPointId,
    int? ToPointId,
    AiLaneChangeDirection? Direction,
    int? JunctionId,
    float? DistanceToDecisionMeters,
    AiPursuitLaneRouteDiagnostic? CurrentLaneRoute,
    IReadOnlyList<AiPursuitLaneRouteDiagnostic> CandidateLaneRoutes,
    AiLaneChangeSafetyStatus? SafetyStatus,
    long RouteRevision);
```

O tracker calcula uma chave com event kind, reason, âncora, from/to, direção, junction e safety; não inclui distância contínua.

- [ ] **Step 4: Substituir o gate tardio em `TrackPursuit()`**

Remover a condição que chama `TryPrepareLaneChange` somente quando `navigation.Status != Active`. Chamar o pipeline logo após `_pursuitNavigator.Update(...)` sempre que lane changing estiver enabled. Usar sua `EffectiveNavigation` para route state e junction decisions. Manter `TryRetainCommittedLaneChange` dentro do pipeline, não duplicado em `AiState`.

- [ ] **Step 5: Preservar safety e lifecycle físico**

Continuar usando `UpdateLaneChangeSafety()`, `AiLaneChangeController.TryMove(...)`, cursores e blend existentes. `ReleasePursuit()` e `Despawn()` devem limpar pipeline/controlador/tracker. Nenhum `AiState` sem pursuit deve avaliar ou mover lateralmente.

- [ ] **Step 6: Rodar suite core e commit**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore
git -C external/AssettoServer add -- AssettoServer/Server/Ai/AiState.cs AssettoServer/Server/Ai/AiPursuitControl.cs AssettoServer/Server/Ai/AiPursuitLaneDiagnosticTracker.cs AssettoServer.Tests/Server/Ai/AiPursuitLaneDiagnosticsTests.cs AssettoServer.Tests/Server/Ai/AiLaneChangeSafetyTests.cs
git -C external/AssettoServer commit -m "feat(ai): diagnose proactive pursuit lane changes"
```

Expected: suite completa passa; nenhum novo warning.

### Task 5: Regressão com `fast_lane.aip` real

**Files:**
- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/RealSplineTransitionIntegrationTests.cs`

**Interfaces:**
- Consumes: parser, locator, navigator, selector e pipeline reais.
- Produces: regressão reproduzível do cenário `312936 -> 311797` pela junction `2`.

- [ ] **Step 1: Converter a prova exploratória em teste failing do fluxo corrigido**

Manter a caracterização que mostra fallback em `313071`, mas acrescentar a expectativa ainda ausente:

```csharp
Assert.Multiple(() =>
{
    Assert.That(navigation.Status, Is.EqualTo(AiPursuitNavigationStatus.Active));
    Assert.That(navigation.State!.TargetPointId, Is.EqualTo(313071));
    Assert.That(navigation.PreferredPhysicalTargetPointId, Is.EqualTo(311797));
    Assert.That(preparation.RequestPrepared, Is.True);
    Assert.That(preparation.Evaluation.Selection!.JunctionId, Is.EqualTo(2));
});
```

Remover `Assert.Pass` puramente diagnóstico. Manter o teste `171751/171772/171780` como inspeção explícita de topologia, sem afirmar destination desconhecida.

- [ ] **Step 2: Executar e observar RED antes da integração final**

```powershell
$env:POLICE_CHASE_FAST_LANE_AIP='C:\Users\arthur.carlos\Downloads\fast_lane.aip'
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RealShutokoPackageReproducesLateLaneChangeEvaluationGap
```

Expected antes das Tasks 1–4: FAIL porque fallback ativo mascara a request. Após as Tasks 1–4: PASS.

- [ ] **Step 3: Executar todas as regressões reais**

```powershell
$env:POLICE_CHASE_FAST_LANE_AIP='C:\Users\arthur.carlos\Downloads\fast_lane.aip'
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RealSplineTransitionIntegrationTests
```

Expected: todos os testes reais selecionados passam; nenhum teste edita o arquivo de origem.

- [ ] **Step 4: Commitar a evidência real e publicar o core**

```powershell
git -C external/AssettoServer add -- AssettoServer.Tests/Server/Ai/RealSplineTransitionIntegrationTests.cs
git -C external/AssettoServer commit -m "test(ai): cover masked Shutoko lane preparation"
git -C external/AssettoServer push -u arthur codex/p0.7-real-lane-change-fix
```

### Task 6: Configuração, adapter e logs do plugin

**Files:**
- Modify: `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`
- Modify: `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs`
- Modify: `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs`
- Modify: `src/PoliceChasePlugin/PoliceChaseService.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs`
- Modify: `tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiStateTests.cs`
- Modify: `tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseServiceTests.cs`

**Interfaces:**
- Produces: `PursuitLaneChangeLookaheadMeters` com default `1000`.
- Consumes: contratos tipados do core.
- Produces: logs de startup, avaliação e lifecycle deduplicados.

- [ ] **Step 1: Escrever testes failing de default e validação**

```csharp
Assert.That(configuration.PursuitLaneChangeLookaheadMeters, Is.EqualTo(1000));
```

Adicionar validator tests para `99`, `5001`, `NaN`, infinidade e lookahead menor que `PursuitLaneChangeDistanceMeters`.

- [ ] **Step 2: Escrever testes failing de mapping e logs**

Cobrir todos os novos reasons/safety statuses no adapter. Em `PolicePursuitServiceTests`, enviar duas avaliações semanticamente idênticas com distâncias diferentes e afirmar um único log; alterar reason/junction e afirmar novo log. Em `PoliceChaseServiceTests`, afirmar uma linha de startup enabled com `lookahead 1000`, `distance 60`, `cooldown 3000`, e uma linha disabled quando o recurso está desligado.

- [ ] **Step 3: Executar e confirmar RED**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PoliceChaseConfiguration|FullyQualifiedName~AssettoServerPoliceAiStateTests|FullyQualifiedName~PolicePursuitServiceTests|FullyQualifiedName~PoliceChaseServiceTests"
```

Expected: falha de compilação pela propriedade e contratos ausentes.

- [ ] **Step 4: Implementar configuração e mapping**

```csharp
public float PursuitLaneChangeLookaheadMeters { get; set; } = 1000;
```

Estender `PolicePursuitLaneChangeOptions` com `LookaheadMeters`; mapear enums e objetos de rota explicitamente em `AssettoServerPoliceAiSlotSource`; não converter reasons em strings.

- [ ] **Step 5: Implementar logs por assinatura semântica**

Transportar o `Revision` produzido pelo core e manter `_lastLoggedLaneChangeRevision`. Logar avaliação com police point, âncora, current route, candidatas, junction e reason; logar request/start/completion/cancel uma vez. Distância sozinha não deve criar revisão.

- [ ] **Step 6: Implementar observabilidade de startup**

Antes de `Plugin initialized`, registrar:

```text
[PoliceChase] Route-aware lane changing enabled: lookahead 1000.0 m, transition 60.0 m, cooldown 3000 ms
```

ou `disabled by configuration` quando `PursuitLaneChangeEnabled=false`.

- [ ] **Step 7: Rodar plugin completo e commit parent com ponteiro do core**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj -c Release --no-restore
git add -- src tests external/AssettoServer
git commit -m "fix: prepare and diagnose P0.7 lane changes"
```

Expected: todos os testes passam e o submodule aponta para o commit publicado da Task 5.

### Task 7: Documentação e checklist real

**Files:**
- Modify: `docs/P0.7-route-aware-lane-changing.md`
- Modify: `README.md` somente na seção de configuração existente.

**Interfaces:**
- Produces: explicação reproduzível e procedimento não destrutivo de validação.

- [ ] **Step 1: Atualizar a causa raiz e corrigir a hipótese nativa**

Documentar que o core não possui lane-change nativo dos bots; explicar o gate `navigation.Status != Active`, a máscara causada por equivalentes e por que os testes isolados anteriores passaram.

- [ ] **Step 2: Documentar configuração e logs**

Registrar os quatro campos P0.7, defaults, unidades e faixas:

```yaml
PursuitLaneChangeEnabled: true
PursuitLaneChangeDistanceMeters: 60
PursuitLaneChangeCooldownMilliseconds: 3000
PursuitLaneChangeLookaheadMeters: 1000
```

Incluir exemplos de `CurrentLaneValid`, `NoForwardRoute`, `BeyondLookahead`, obstacle, requested, started, completed e cancelled.

- [ ] **Step 3: Documentar teste real**

Incluir três roteiros: trecho comum, saída/rampa e safety bloqueada/liberada. Em cada um, listar logs esperados e proibir despawn, respawn, teleport, snap e reverse.

- [ ] **Step 4: Verificar links e commit**

```powershell
rg -n "PursuitLaneChange|P0.7|312936|311797|junction 2" docs README.md
git add -- docs README.md
git commit -m "docs: explain P0.7 lane change correction"
```

### Task 8: Verificação final, artefatos e publicação

**Files:**
- Verify: entire core and plugin trees.
- Produce: `artifacts/publish/AssettoServer/`
- Produce: `artifacts/publish/PoliceChasePlugin/`

**Interfaces:**
- Produces: resultados frescos, hashes reproduzíveis e artefatos para inspeção.

- [ ] **Step 1: Executar testes e builds Release completos**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore
dotnet test PoliceChasePlugin.sln -c Release --no-restore
dotnet build external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release --no-restore
dotnet build src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release --no-restore
```

Expected: zero testes falhos e zero erros; apenas o warning CS0162 preexistente em `OnlineEventGenerator.cs` é aceitável.

- [ ] **Step 2: Executar testes reais com variável explícita**

```powershell
$env:POLICE_CHASE_FAST_LANE_AIP='C:\Users\arthur.carlos\Downloads\fast_lane.aip'
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RealSplineTransitionIntegrationTests
```

Expected: suite real verde, incluindo o cenário mascarado.

- [ ] **Step 3: Auditar escopo e grafo**

```powershell
git -C external/AssettoServer diff 539fcbe65c45f237d51fed45400121d8aae33cea..HEAD --stat
git diff 7c3f2f8778fa77c3c9eb3d5a870de565438d9947..HEAD --stat
rg -n "LeftId|RightId" external/AssettoServer/AssettoServer/Server/Ai/Routing/AiRouteGraph.cs external/AssettoServer/AssettoServer/Server/Ai/Routing/AiRoutePlanner.cs
```

Expected: nenhuma alteração global de tráfego e nenhuma edge lateral no grafo.

- [ ] **Step 4: Publicar artefatos locais para inspeção**

```powershell
dotnet publish external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release --no-restore -o artifacts/publish/AssettoServer
dotnet publish src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release --no-restore -o artifacts/publish/PoliceChasePlugin
```

- [ ] **Step 5: Solicitar revisão do código e corrigir achados válidos**

Usar `superpowers:requesting-code-review` sobre os diffs do core e parent. Reexecutar os testes afetados após qualquer ajuste.

- [ ] **Step 6: Verificar estado e publicar `main`**

```powershell
git -C external/AssettoServer status --short
git status --short
git submodule status
git -C external/AssettoServer rev-parse HEAD
git rev-parse HEAD
git push origin main
```

Expected: árvores limpas, submodule no hash exato do core e `origin/main` atualizado.

- [ ] **Step 7: Entregar handoff sem instalação destrutiva**

Informar causa comprovada, fluxo antigo/novo, diferença do tráfego nativo, arquivos, diagnostics, configuração, contagem completa de testes, builds, hashes, branch, submodule status, comandos de pull/build e checklist real. Não fornecer comandos destrutivos de instalação e não declarar a P0.7 concluída antes do teste visual.

## Self-Review

- Spec coverage: âncora física, fallback P0.6, avaliação proativa, junction real, lookahead, safety, request/controlador, diagnóstico tipado, deduplicação, startup, testes A–J, SRP real, documentação, builds, artefatos e Git possuem tasks explícitas.
- Placeholder scan: nenhum marcador provisório ou teste genérico permaneceu.
- Type consistency: `PreferredPhysicalTargetPointId` flui locator → navigator → pipeline → core diagnostics → adapter/plugin; `LookaheadMeters` flui configuração → opções plugin → opções core → selector.
- Scope: a correção usa o controlador P0.7 existente, não cria edge lateral e não altera P0.4–P0.6 nem tráfego global.
