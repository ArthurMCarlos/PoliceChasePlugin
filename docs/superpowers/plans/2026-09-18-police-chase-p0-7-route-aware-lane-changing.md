# Police Chase P0.7 Route-Aware Lane Changing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer a instância policial mudar fisicamente uma faixa adjacente quando essa faixa for necessária para alcançar o corredor confirmado do alvo antes de uma junction real.

**Architecture:** O core ganhará um seletor puro que tenta a faixa atual e depois somente vizinhos imediatos `LeftId`/`RightId`, mantendo o grafo limitado a `NextId` e `SplineJunction`. Um controlador stateful avançará cursores de origem e destino e combinará suas poses numa trajetória contínua; `AiState` ativará isso apenas durante pursuit, enquanto o plugin fornecerá configuração e logs tipados.

**Tech Stack:** C# 12, .NET 8, NUnit, AssettoServer 0.0.54 fork, FluentValidation e Serilog.

**Spec:** `docs/superpowers/specs/2026-09-18-police-chase-p0-7-route-aware-lane-changing-design.md`

## Global Constraints

- Core inicial: `793abe4810f5860b3f9b63f93dc9bce7240ba57b` no fork `ArthurMCarlos/AssettoServer`.
- Desenvolver o core na branch `codex/p0.7-route-aware-lane-changing`.
- Não adicionar `LeftId` ou `RightId` ao `AiRouteGraph`.
- Não editar `fast_lane.aip`, fabricar junction lateral, usar reflection, teleport, snap ou reverse routing.
- Ativar lane change somente quando existir pursuit; tráfego normal permanece inalterado.
- Uma manobra atravessa exatamente uma adjacência.
- Preservar P0.4–P0.6, dez bots, slot dedicado, grace, no-route e probe.
- Não alterar parâmetros globais do tráfego.
- Entregar como pronta para validação real, nunca concluída antes do teste visual.

## File Structure

### Core

- Create `AssettoServer/Server/Ai/Routing/AiPursuitLaneSelector.cs`: decisão lateral pura.
- Create `AssettoServer/Server/Ai/AiSplineCursor.cs`: cursor longitudinal independente.
- Create `AssettoServer/Server/Ai/AiLaneChangeTrajectory.cs`: blend de posição/tangente.
- Create `AssettoServer/Server/Ai/AiLaneChangeSafety.cs`: avaliação do gap destino.
- Create `AssettoServer/Server/Ai/AiLaneChangeController.cs`: lifecycle da manobra.
- Modify `AssettoServer/Server/Ai/AiPursuitControl.cs`: opções e diagnóstico público.
- Modify `AssettoServer/Server/Ai/AiState.cs`: integração física e lookahead duplo.
- Modify `AssettoServer/Server/Ai/Routing/AiPursuitNavigator.cs`: candidatos aceitos para decisão lateral.

### Plugin

- Modify configuração, validator, adapter e serviço de pursuit.
- Modify testes correspondentes em `tests/PoliceChasePlugin.Tests/`.
- Create `docs/P0.7-route-aware-lane-changing.md`: instalação e validação real.

---

### Task 1: Branch e regressão forward-only

**Files:**
- Modify: submodule Git metadata
- Test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiRoutePlannerTests.cs`

**Interfaces:**
- Consumes: `AiRoutePlanner.TryPlan(...)`.
- Produces: proteção de que equivalentes laterais não são route edges.

- [ ] **Step 1: Criar a branch do core**

```powershell
git -C external/AssettoServer switch -c codex/p0.7-route-aware-lane-changing 793abe4810f5860b3f9b63f93dc9bce7240ba57b
```

- [ ] **Step 2: Escrever o teste de ausência de aresta lateral**

Adicionar um grafo `0 -> 1` paralelo a `10 -> 11`, sem junction:

```csharp
[Test]
public void DoesNotTreatAdjacentLaneAsRouteEdge()
{
    var planner = CreatePlanner(
        (0, new AiRouteEdge(1, 10, null, null)),
        (1, null),
        (10, new AiRouteEdge(11, 10, null, null)),
        (11, null));
    var result = planner.TryPlan(0, new HashSet<int> { 11 },
        new AiRouteSearchLimits(100, 100));
    Assert.That(result.Plan, Is.Null);
    Assert.That(result.Failure, Is.EqualTo(AiRouteSearchFailure.Unreachable));
}
```

- [ ] **Step 3: Executar e commit**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AiRoutePlannerTests
git -C external/AssettoServer add -- AssettoServer.Tests/Server/Ai/AiRoutePlannerTests.cs
git -C external/AssettoServer commit -m "test(ai): preserve forward-only pursuit graph"
```

Expected: PASS.

### Task 2: Seletor route-aware de faixa adjacente

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitLaneSelector.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitLaneSelectorTests.cs`

**Interfaces:**
- Produces: `Select(int, IReadOnlySet<int>, AiRouteSearchLimits, float)` e tipos abaixo.

- [ ] **Step 1: Escrever testes failing**

Cobrir current lane alcançável, left-only, right-only, direção oposta, decisão próxima demais e proibição de salto de duas faixas:

```csharp
[Test]
public void KeepsCurrentLaneWhenItStillReachesTarget()
{
    var result = CreateSelector(current: Plan(0, 20), left: Plan(10, 30))
        .Select(0, Targets, Limits, 60);
    Assert.That(result.Kind, Is.EqualTo(AiPursuitLaneSelectionKind.Stay));
}

[Test]
public void SelectsOnlyImmediateReachableNeighbor()
{
    var result = CreateSelector(current: null, left: PlanWithJunction(10, 180), right: null)
        .Select(0, Targets, Limits, 60);
    Assert.That(result.Selection!.ToPointId, Is.EqualTo(10));
}
```

- [ ] **Step 2: Confirmar falha**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AiPursuitLaneSelectorTests
```

Expected: FAIL de compilação.

- [ ] **Step 3: Implementar contratos e seleção mínima**

```csharp
public enum AiLaneChangeDirection { Left, Right }
public enum AiPursuitLaneSelectionKind { Stay, Change, Unreachable }
public enum AiPursuitLaneRejectionReason
{
    MissingNeighbor, OppositeDirection, InvalidGeometry,
    NoForwardRoute, InsufficientPreparationDistance
}
public sealed record AiPursuitLaneSelection(
    int FromPointId, int ToPointId, AiLaneChangeDirection Direction,
    AiRoutePlan DestinationPlan, float DistanceToDecisionMeters);
public sealed record AiPursuitLaneCandidateDiagnostic(
    int? PointId, AiLaneChangeDirection Direction,
    AiPursuitLaneRejectionReason? Rejection);
public sealed record AiPursuitLaneSelectionResult(
    AiPursuitLaneSelectionKind Kind,
    AiPursuitLaneSelection? Selection,
    IReadOnlyList<AiPursuitLaneCandidateDiagnostic> Candidates);
```

O algoritmo tenta `currentPointId`; somente em falha avalia `LeftId` e `RightId` imediatos de mesma direção e geometria válida. Aceita somente plano forward real com espaço para `maneuverDistanceMeters`; desempata por custo e ID.

- [ ] **Step 4: Testar e commit**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AiPursuitLaneSelectorTests|FullyQualifiedName~AiRoutePlannerTests"
git -C external/AssettoServer add -- AssettoServer/Server/Ai/Routing/AiPursuitLaneSelector.cs AssettoServer.Tests/Server/Ai/AiPursuitLaneSelectorTests.cs
git -C external/AssettoServer commit -m "feat(ai): select route-aware pursuit lane"
```

Expected: PASS.

### Task 3: Cursor e trajetória contínua

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/AiSplineCursor.cs`
- Create: `external/AssettoServer/AssettoServer/Server/Ai/AiLaneChangeTrajectory.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiSplineCursorTests.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiLaneChangeTrajectoryTests.cs`

**Interfaces:**
- Produces: `AiSplineCursor.TryAdvance(float)`, `Evaluate()` e `AiLaneChangeTrajectory.Blend(...)`.

- [ ] **Step 1: Escrever testes failing**

```csharp
[Test]
public void CursorAdvancesAcrossSegments()
{
    var cursor = CreateCursor(segmentLengths: [10, 20], pointId: 0, progress: 8);
    Assert.That(cursor.TryAdvance(7), Is.True);
    Assert.That(cursor.PointId, Is.EqualTo(1));
    Assert.That(cursor.SegmentProgressMeters, Is.EqualTo(5).Within(0.001));
}

[TestCase(0.0f)]
[TestCase(1.0f)]
public void BlendMatchesLanePoseAtEndpoints(float progress)
{
    var a = new AiSplinePose(Vector3.Zero, Vector3.UnitX);
    var b = new AiSplinePose(new Vector3(0, 0, 3), Vector3.UnitX);
    var pose = AiLaneChangeTrajectory.Blend(a, b, progress, 60);
    Assert.That(pose.Position, Is.EqualTo(progress == 0 ? a.Position : b.Position));
}
```

Adicionar continuidade de tangente, monotonicidade lateral e valores finitos.

- [ ] **Step 2: Confirmar falha**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AiSplineCursorTests|FullyQualifiedName~AiLaneChangeTrajectoryTests"
```

- [ ] **Step 3: Implementar cursor e smoothstep quintic**

```csharp
internal readonly record struct AiSplinePose(Vector3 Position, Vector3 Tangent);
var t = Math.Clamp(progress, 0, 1);
var w = t * t * t * (t * (t * 6 - 15) + 10);
var dw = 30 * t * t * (t * (t - 2) + 1) / distanceMeters;
var position = Vector3.Lerp(source.Position, destination.Position, w);
var tangent = Vector3.Lerp(source.Tangent, destination.Tangent, w)
              + (destination.Position - source.Position) * dw;
```

O cursor usa um delegate `TryGetNext(int, out int)`, mantém ponto/progresso e avalia Catmull–Rom com os pontos reais.

- [ ] **Step 4: Testar e commit**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AiSplineCursorTests|FullyQualifiedName~AiLaneChangeTrajectoryTests"
git -C external/AssettoServer add -- AssettoServer/Server/Ai/AiSplineCursor.cs AssettoServer/Server/Ai/AiLaneChangeTrajectory.cs AssettoServer.Tests/Server/Ai/AiSplineCursorTests.cs AssettoServer.Tests/Server/Ai/AiLaneChangeTrajectoryTests.cs
git -C external/AssettoServer commit -m "feat(ai): add continuous lane transition trajectory"
```

Expected: PASS.

### Task 4: Segurança da faixa destino

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/AiLaneChangeSafety.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiLaneChangeSafetyTests.cs`

**Interfaces:**
- Produces: `AiLaneChangeSafety.Evaluate(AiLaneChangeSafetyRequest)`.

- [ ] **Step 1: Escrever testes failing**

```csharp
[Test]
public void RejectsFastVehicleClosingFromBehind()
{
    var result = AiLaneChangeSafety.Evaluate(Request(
        policeSpeed: 20,
        obstacles: [Obstacle(longitudinal: -18, lateral: 0, speed: 40)]));
    Assert.That(result.Status, Is.EqualTo(AiLaneChangeSafetyStatus.BlockedRearClosing));
}

[Test]
public void AcceptsClearDestinationCorridor() =>
    Assert.That(AiLaneChangeSafety.Evaluate(Request(obstacles: [])).Status,
        Is.EqualTo(AiLaneChangeSafetyStatus.Safe));
```

Adicionar frente, lado, traseira e obstáculo fora do corredor.

- [ ] **Step 2: Implementar cálculo local após confirmar RED**

```csharp
internal enum AiLaneChangeSafetyStatus
{
    Safe, BlockedFront, BlockedSide, BlockedRear, BlockedRearClosing
}
internal readonly record struct AiLaneChangeObstacle(
    Vector3 Position, Vector3 Velocity, float LengthMeters);
```

Projetar posição relativa em forward/right; considerar comprimentos, largura da faixa e gap traseiro adicional de `max(0, closingSpeed) * 2s`.

- [ ] **Step 3: Testar e commit**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AiLaneChangeSafetyTests
git -C external/AssettoServer add -- AssettoServer/Server/Ai/AiLaneChangeSafety.cs AssettoServer.Tests/Server/Ai/AiLaneChangeSafetyTests.cs
git -C external/AssettoServer commit -m "feat(ai): validate pursuit lane change gaps"
```

Expected: PASS.

### Task 5: Controlador stateful

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/AiLaneChangeController.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiLaneChangeControllerTests.cs`

**Interfaces:**
- Consumes: seleção, cursores, trajetória e safety.
- Produces: `Request`, `UpdateWaiting`, `TryMove`, `CancelWaiting` e `Reset`.

- [ ] **Step 1: Escrever testes failing**

```csharp
[Test]
public void BlockedRequestWaitsAndStartsWhenGapClears()
{
    var controller = CreateController();
    controller.Request(Selection, 0, routeRevision: 4);
    controller.UpdateWaiting(AiLaneChangeSafetyStatus.BlockedSide, 100);
    Assert.That(controller.Phase, Is.EqualTo(AiLaneChangePhase.WaitingForGap));
    controller.UpdateWaiting(AiLaneChangeSafetyStatus.Safe, 200);
    Assert.That(controller.Phase, Is.EqualTo(AiLaneChangePhase.Changing));
}
```

Adicionar deduplicação, cancelamento só em waiting, revisão durante changing, conclusão/cooldown e mudanças sequenciais.

- [ ] **Step 2: Implementar lifecycle após RED**

```csharp
internal enum AiLaneChangePhase { None, WaitingForGap, Changing, Cooldown }
public enum AiPursuitLaneChangeEventKind
{
    Required, Waiting, Started, Completed, Cancelled, RouteRevised
}
internal sealed record AiLaneChangeEvent(
    long Revision, AiPursuitLaneChangeEventKind Kind,
    int FromPointId, int ToPointId, AiLaneChangeDirection Direction,
    long RouteRevision, float? DistanceToDecisionMeters,
    AiLaneChangeSafetyStatus? SafetyStatus);
```

`TryMove()` avança ambos os cursores, retorna pose e um resultado de conclusão; somente o consumidor aplica o novo ponto físico.

- [ ] **Step 3: Testar e commit**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AiLaneChangeControllerTests
git -C external/AssettoServer add -- AssettoServer/Server/Ai/AiLaneChangeController.cs AssettoServer.Tests/Server/Ai/AiLaneChangeControllerTests.cs
git -C external/AssettoServer commit -m "feat(ai): control pursuit lane change lifecycle"
```

Expected: PASS.

### Task 6: Integrar navigator e `AiState`

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitNavigator.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiState.cs`
- Modify: testes existentes de navigator/control
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiStateLaneChangeIntegrationTests.cs`

**Interfaces:**
- Produces: opções e `AiPursuitLaneChangeDiagnostics?` públicos.

- [ ] **Step 1: Escrever testes de contrato e integração**

```csharp
public sealed record AiPursuitLaneChangeOptions(
    bool Enabled, float DistanceMeters, int CooldownMilliseconds);
```

Testar validação e o fluxo: current unreachable → adjacent reachable → tracking ativo → waiting → changing → posições contínuas → commit do ponto destino → junction real. Cobrir também disabled, release, despawn e tráfego sem pursuit.

- [ ] **Step 2: Confirmar RED**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AiPursuitNavigatorTests|FullyQualifiedName~AiPursuitControlTests|FullyQualifiedName~AiStateLaneChangeIntegrationTests"
```

- [ ] **Step 3: Expor somente candidatos aceitos**

Adicionar `IReadOnlyList<int> TargetPointIds` a `AiPursuitNavigationResult`, preenchido por `location.Candidates`. O seletor nunca usa candidatos rejeitados.

- [ ] **Step 4: Integrar seleção e controle**

Em `TrackPursuit()`, quando a rota atual falhar, chamar o seletor antes de iniciar grace/no-route. Uma seleção válida mantém a pursuit ativa enquanto waiting/changing e publica apenas decisões de junction do `DestinationPlan`.

Em `DetectObstacles()`, montar obstáculos a partir de jogadores e `AiState` inicializados, excluindo o próprio estado, e usar o lookahead mais restritivo de origem/destino.

Em `Update()`, usar o controlador somente em `Changing`; aplicar `CurrentSplinePointId`, progresso, comprimento e tangentes do destino apenas em `Completed`.

- [ ] **Step 5: Expor diagnóstico tipado**

```csharp
public sealed record AiPursuitLaneChangeDiagnostics(
    long Revision, AiPursuitLaneChangeEventKind EventKind,
    int FromPointId, int ToPointId, AiLaneChangeDirection Direction,
    long RouteRevision, float? DistanceToDecisionMeters,
    string? BlockingReason);
```

Adicionar ao final de `AiPursuitTrackingResult`. `ReleasePursuit()` e `Despawn()` resetam o controlador.

- [ ] **Step 6: Rodar core completo e commit**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore
git -C external/AssettoServer add -- AssettoServer/Server/Ai AssettoServer.Tests/Server/Ai
git -C external/AssettoServer commit -m "feat(ai): execute route-aware pursuit lane changes"
```

Expected: zero falhas e nenhum novo warning.

### Task 7: Configuração, adapter e logs do plugin

**Files:**
- Modify: `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`
- Modify: `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs`
- Modify: `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs`
- Modify: testes correspondentes.

**Interfaces:**
- Consumes: contratos da Task 6.
- Produces: opções YAML e logs deduplicados.

- [ ] **Step 1: Escrever testes failing de configuração, mapper e logs**

Defaults:

```csharp
PursuitLaneChangeEnabled = true;
PursuitLaneChangeDistanceMeters = 60;
PursuitLaneChangeCooldownMilliseconds = 3000;
```

Validar distância 20..200 m e cooldown 0..30.000 ms. Testar todos os eventos e garantir uma mensagem por revisão.

- [ ] **Step 2: Confirmar RED**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PoliceChaseConfiguration|FullyQualifiedName~AssettoServerPoliceAiStateTests|FullyQualifiedName~PolicePursuitServiceTests"
```

- [ ] **Step 3: Implementar mapping e deduplicação**

Transportar `PolicePursuitLaneChangeOptions`, mapear diagnóstico sem reflection e adicionar `_lastLoggedLaneChangeRevision`. Zerar junto aos diagnósticos de rota. Logar required, waiting, started, completed, cancelled e route-revised com from/to, direção, decisão e bloqueio.

- [ ] **Step 4: Rodar plugin completo e commit parent**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj -c Release --no-restore
git add -- src tests external/AssettoServer
git commit -m "feat: configure and diagnose pursuit lane changes"
```

Expected: todos os testes passam.

### Task 8: Regressão com o `fast_lane.aip` real

**Files:**
- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/RealSplineTransitionIntegrationTests.cs`

**Interfaces:**
- Consumes: parser, seletor e planner reais.
- Produces: teste `[Explicit]` reproduzível.

- [ ] **Step 1: Escrever descoberta e teste failing**

Percorrer junctions e procurar uma adjacência imediata de mesma direção cuja cadeia atual não alcance o target, mas cuja cadeia vizinha alcance a junction. O teste deve afirmar `Change`, destino imediato e `DestinationPlan.JunctionDecisions` contendo a junction real.

```csharp
[Test]
[Explicit("Requires POLICE_CHASE_FAST_LANE_AIP pointing to the real Shutoko package")]
public void RealShutokoPackageSelectsAdjacentLaneBeforeReachableJunction()
{
    using var spline = LoadRequiredRealSpline();
    var scenario = FindAdjacentJunctionScenario(spline);
    var result = new AiPursuitLaneSelector(spline, new AiRoutePlanner(spline))
        .Select(scenario.PolicePointId, new HashSet<int> { scenario.TargetPointId },
            new AiRouteSearchLimits(20_000, 50_000), 60);
    Assert.That(result.Kind, Is.EqualTo(AiPursuitLaneSelectionKind.Change));
    Assert.That(result.Selection!.DestinationPlan.JunctionDecisions,
        Contains.Key(scenario.JunctionId));
}
```

- [ ] **Step 2: Executar P0.7 e regressão P0.6 reais**

```powershell
$env:POLICE_CHASE_FAST_LANE_AIP='C:\Users\arthur.carlos\Downloads\fast_lane.aip'
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~RealShutokoPackageSelectsAdjacentLaneBeforeReachableJunction|FullyQualifiedName~RealShutokoPackageReproducesAndFixes171036Transition"
```

Expected: 2 PASS.

- [ ] **Step 3: Commitar core e ponteiro parent**

```powershell
git -C external/AssettoServer add -- AssettoServer.Tests/Server/Ai/RealSplineTransitionIntegrationTests.cs
git -C external/AssettoServer commit -m "test(ai): cover real Shutoko lane transition"
git add -- external/AssettoServer
git commit -m "test: pin P0.7 real spline coverage"
```

### Task 9: Verificação, documentação e publicação

**Files:**
- Create: `docs/P0.7-route-aware-lane-changing.md`
- Modify: `README.md` somente se já listar configuração instalada.
- Produce: `artifacts/publish/AssettoServer/` e `artifacts/publish/PoliceChasePlugin/`.

**Interfaces:**
- Produces: SHAs, artefatos e roteiro real.

- [ ] **Step 1: Documentar configuração, logs, instalação e validação**

Registrar os três campos/defaults; arquivos a copiar; logs esperados; seis testes reais (básico, simples, ponte, segunda saída, bloqueada, perdida); limitações e estado “pronta para validação”.

- [ ] **Step 2: Executar suites e builds Release**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release --no-restore
dotnet test PoliceChasePlugin.sln -c Release --no-restore
dotnet build external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release --no-restore
dotnet build src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release --no-restore
```

Expected: zero falhas/erros; apenas CS0162 preexistente no core é aceitável.

- [ ] **Step 3: Publicar artefatos locais**

```powershell
dotnet publish external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release --no-restore -o artifacts/publish/AssettoServer
dotnet publish src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release --no-restore -o artifacts/publish/PoliceChasePlugin
```

- [ ] **Step 4: Auditar escopo**

```powershell
git -C external/AssettoServer diff 793abe4810f5860b3f9b63f93dc9bce7240ba57b..HEAD --stat
git diff 946157f..HEAD --stat
rg -n "LeftId|RightId" external/AssettoServer/AssettoServer/Server/Ai/Routing/AiRoutePlanner.cs
```

Expected: sem parâmetros globais alterados e sem edge lateral no planner.

- [ ] **Step 5: Commitar e publicar**

```powershell
git add -- docs README.md external/AssettoServer
git commit -m "docs: add P0.7 server validation guide"
git -C external/AssettoServer push -u arthur codex/p0.7-route-aware-lane-changing
git push origin main
```

- [ ] **Step 6: Entregar handoff**

Informar causa raiz, mecanismo, alterações core/plugin, configuração, testes, builds, SHAs, branch, arquivos, logs, roteiro e limitações. Não declarar conclusão antes do teste real.

## Self-Review

- Cobertura: decisão, trajetória, segurança, histerese, forward-only, diagnóstico, configuração, fast_lane real, regressão, publicação e validação estão atribuídos.
- Placeholder scan: nenhum `TBD`, `TODO`, “depois” ou teste genérico permaneceu.
- Consistência: seletor → cursores/trajectory → safety → controller → `AiState` → plugin é a ordem de dependência.
- Escopo: cada task termina em teste e commit semanticamente revisável.
