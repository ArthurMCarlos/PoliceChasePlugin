# Police Chase P0.6 Junction Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer o `CAR_12` manter uma perseguição física através de bifurcações alcançáveis da Shutoko, com localização stateful, busca limitada, tolerância temporária e reaquisição sem loop.

**Architecture:** O fork do AssettoServer continuará usando o grafo dirigido de `NextId` e `SplineJunction.EndPointId`, mas passará a produzir corredores de pontos e diagnósticos sob limites independentes de distância e nós. Um navegador stateful escolherá entre múltiplos candidatos do KD-tree, reutilizará ou estenderá a rota e preservará decisões durante falhas transitórias; o plugin fornecerá configuração, logs de transição e suspensão verificável após `NoRoute`.

**Tech Stack:** C# 12, .NET 8, NUnit 3, AssettoServer 0.0.54 forkado, Autofac, FluentValidation, Serilog, Git submodule.

**Spec:** `docs/superpowers/specs/2026-09-17-police-chase-p0-6-junction-routing-design.md`

## Global Constraints

- Trabalhar no projeto principal diretamente em `main`, conforme escolha explícita do usuário.
- Criar no fork `external/AssettoServer` a branch `codex/p0.6-junction-routing` a partir de `54326833d502d1872182ca39398affff99b0eb5b`.
- Publicar todos os commits do core em `arthur/codex/p0.6-junction-routing` e todos os commits do plugin em `origin/main`.
- Manter `PursuitMaxDistanceMeters: 1500` como limite exclusivamente espacial.
- Usar `PursuitRouteSearchMaxDistanceMeters: 20000`, `PursuitRouteSearchMaxVisitedNodes: 50000`, `PursuitRouteGraceMilliseconds: 2000` e `PursuitNoRouteProbeIntervalMilliseconds: 2000` como defaults.
- Não alterar `PlayerRadiusMeters`, `MaxSpeedKph`, `TrafficDensity`, `AiPerPlayerTargetCount`, `MaxAiTargetCount` ou distâncias globais de spawn.
- Não escrever em `Status.Position`, não chamar `Teleport()` para perseguição e não provocar despawn/respawn para corrigir rota.
- Não adicionar `LeftId` ou `RightId` como arestas de movimento; troca deliberada de faixa permanece fora de escopo.
- Preservar obstáculos, curvas, colisões, frenagem, tangentes e movimento nativos.
- Não usar reflection.
- Aplicar TDD em cada mudança comportamental e executar testes do core e do plugin em série, pois ambos compartilham outputs do submodule.

---

## File Structure

### Fork AssettoServer

- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiRoutePlanner.cs` — busca com limites de distância/nós, corredor e diagnóstico.
- Create: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiRouteSearch.cs` — contratos imutáveis da busca.
- Create: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitTargetLocator.cs` — candidatos do KD-tree filtrados por distância e direção.
- Create: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitNavigator.cs` — cache, extensão, recálculo e tolerância.
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs` — resultados e diagnósticos públicos da perseguição.
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiState.cs` — integração do navegador com o estado dedicado e com `JunctionEvaluator`.
- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiRoutePlannerTests.cs` — rotas, limites e corredor.
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitTargetLocatorTests.cs` — seleção de candidatos.
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitNavigatorTests.cs` — cache, branch, tolerância e recuperação.
- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/JunctionEvaluatorTests.cs` — preservação das decisões explícitas.

### PoliceChasePlugin

- Modify: `src/PoliceChasePlugin/PoliceChaseConfiguration.cs` — quatro parâmetros P0.6.
- Modify: `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs` — validação dos parâmetros.
- Modify: `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs` — opções e metadados tipados.
- Modify: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs` — ponte explícita para o core.
- Modify: `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs` — suspensão, sondagem, logs e relógio injetável.
- Modify: `tests/PoliceChasePlugin.Tests/Ai/FakePoliceAiSlotSource.cs` — captura das opções e fila de resultados.
- Modify: `tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiStateTests.cs` — mapeamento dos novos contratos.
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs` — defaults P0.6.
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs` — valores inválidos.
- Modify: `tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs` — suspensão, sondagem e logs.
- Modify: `docs/P0.5-perseguicao-basica.md` — registrar a limitação real de bifurcações.
- Create: `docs/P0.6-navegacao-bifurcacoes.md` — causa, arquitetura, instalação e roteiro real.
- Modify: `README.md` — índice P0.6.

---

### Task 1: Route planner com corredor e orçamentos independentes

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiRouteSearch.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiRoutePlanner.cs`
- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiRoutePlannerTests.cs`

**Interfaces:**
- Consumes: `AiRouteGraph.GetEdges(int)` e as arestas dirigidas P0.5.
- Produces: `AiRouteSearchLimits`, `AiRouteSearchFailure`, `AiRouteNode`, `AiRoutePlan`, `AiRouteSearchResult` e `AiRoutePlanner.TryPlan(int, IReadOnlySet<int>, AiRouteSearchLimits)`.

- [ ] **Step 1: Criar a branch do core e confirmar o baseline**

Run:

```powershell
git -C external/AssettoServer switch -c codex/p0.6-junction-routing
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore
```

Expected: branch criada a partir de `5432683`; core `40/40`; plugin `58/58`.

- [ ] **Step 2: Escrever testes que exijam corredor, múltiplas conexões e limite de nós**

Adicionar casos com a seguinte forma:

```csharp
[Test]
public void ReturnsOrderedCorridorAcrossMultipleJunctions()
{
    var planner = CreatePlanner(new Dictionary<int, AiRouteEdge[]>
    {
        [0] = [new(1, 10, 3, true)],
        [1] = [new(2, 20, 4, false)],
        [2] = [new(5, 30, null, null)],
        [5] = []
    });

    var result = planner.TryPlan(
        0,
        new HashSet<int> { 5 },
        new AiRouteSearchLimits(20_000, 50_000));

    Assert.Multiple(() =>
    {
        Assert.That(result.Failure, Is.EqualTo(AiRouteSearchFailure.None));
        Assert.That(result.Plan!.Nodes.Select(node => node.PointId),
            Is.EqualTo(new[] { 0, 1, 2, 5 }));
        Assert.That(result.Plan.DistanceMeters, Is.EqualTo(60));
        Assert.That(result.Plan.JunctionDecisions,
            Is.EqualTo(new Dictionary<int, bool> { [3] = true, [4] = false }));
    });
}

[Test]
public void StopsAtVisitedNodeBudget()
{
    var planner = CreateLinearPlanner(pointCount: 10);

    var result = planner.TryPlan(
        0,
        new HashSet<int> { 9 },
        new AiRouteSearchLimits(20_000, 3));

    Assert.Multiple(() =>
    {
        Assert.That(result.Plan, Is.Null);
        Assert.That(result.Failure, Is.EqualTo(AiRouteSearchFailure.NodeLimit));
        Assert.That(result.VisitedNodes, Is.EqualTo(3));
    });
}
```

Implementar o helper linear sem ocultar comportamento:

```csharp
private static AiRoutePlanner CreateLinearPlanner(int pointCount)
{
    var edges = Enumerable.Range(0, pointCount)
        .ToDictionary(
            pointId => pointId,
            pointId => pointId + 1 < pointCount
                ? new[] { new AiRouteEdge(pointId + 1, 1, null, null) }
                : Array.Empty<AiRouteEdge>());
    return CreatePlanner(edges);
}
```

Também adaptar os sete testes existentes para o novo resultado e acrescentar distinção entre `DistanceLimit` e `Unreachable`.

- [ ] **Step 3: Executar os testes focados e observar RED**

Run:

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --filter AiRoutePlannerTests --no-restore
```

Expected: FAIL de compilação porque os novos contratos ainda não existem.

- [ ] **Step 4: Criar os contratos da busca**

Implementar em `AiRouteSearch.cs`:

```csharp
namespace AssettoServer.Server.Ai.Routing;

public sealed record AiRouteSearchLimits(
    float MaxDistanceMeters,
    int MaxVisitedNodes);

public enum AiRouteSearchFailure
{
    None,
    InvalidRequest,
    DistanceLimit,
    NodeLimit,
    Unreachable
}

public sealed record AiRouteNode(
    int PointId,
    float DistanceFromStartMeters);

public sealed record AiRoutePlan(
    float DistanceMeters,
    IReadOnlyList<AiRouteNode> Nodes,
    IReadOnlyDictionary<int, bool> JunctionDecisions);

public sealed record AiRouteSearchResult(
    AiRoutePlan? Plan,
    AiRouteSearchFailure Failure,
    int VisitedNodes);
```

Validar `MaxDistanceMeters` finito e positivo e `MaxVisitedNodes > 0` no início da busca.

- [ ] **Step 5: Alterar o Dijkstra para respeitar ambos os orçamentos**

Substituir o retorno anulável por:

```csharp
public AiRouteSearchResult TryPlan(
    int startPointId,
    IReadOnlySet<int> targetPointIds,
    AiRouteSearchLimits limits)
```

Manter durante as Tasks 1–3 o overload usado pela P0.5, para que cada commit continue compilando:

```csharp
public AiRoutePlan? TryPlan(
    int startPointId,
    IReadOnlySet<int> targetPointIds,
    float maxDistanceMeters) =>
    TryPlan(
        startPointId,
        targetPointIds,
        new AiRouteSearchLimits(maxDistanceMeters, int.MaxValue)).Plan;
```

Incrementar `visitedNodes` quando um nó válido for removido da fila. Retornar `NodeLimit` antes de expandir além do orçamento. Marcar quando uma aresta foi podada pela distância para distinguir `DistanceLimit` de `Unreachable`. Na reconstrução, produzir `AiRouteNode` em ordem crescente, começando no ponto policial com distância zero.

- [ ] **Step 6: Executar testes focados e suíte do core**

Run:

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --filter AiRoutePlannerTests --no-restore
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
```

Expected: todos os testes do planner e os 40 testes anteriores mais os novos passam.

- [ ] **Step 7: Commit e push do core**

```powershell
git -C external/AssettoServer add -- AssettoServer/Server/Ai/Routing/AiRouteSearch.cs AssettoServer/Server/Ai/Routing/AiRoutePlanner.cs AssettoServer.Tests/Server/Ai/AiRoutePlannerTests.cs
git -C external/AssettoServer commit -m "feat(ai): bound pursuit route searches"
git -C external/AssettoServer push -u arthur codex/p0.6-junction-routing
```

---

### Task 2: Localização de múltiplos candidatos do alvo

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitTargetLocator.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitTargetLocatorTests.cs`

**Interfaces:**
- Consumes: `AiSpline.KdTree`, `AiSpline.Points`, `SplinePointOperations.GetForwardVector()` e `MaxPlayerDistanceToAiSplineSquared` fornecido pelo chamador.
- Produces: `AiPursuitTargetCandidate` e `AiPursuitTargetLocator.FindCandidates(Vector3, Vector3, float, int)`.

- [ ] **Step 1: Escrever testes de proximidade, direção e fallback parado**

Usar o construtor interno baseado em delegates para não depender de arquivo mmap:

```csharp
[Test]
public void RejectsNearestPointDrivingInOppositeDirection()
{
    var locator = CreateLocator(
        new AiPursuitTargetCandidateSource(10, 1, Vector3.UnitX),
        new AiPursuitTargetCandidateSource(20, 4, -Vector3.UnitX));

    var candidates = locator.FindCandidates(
        Vector3.Zero,
        -Vector3.UnitX * 20,
        maximumDistanceSquared: 49,
        maximumCandidates: 16);

    Assert.That(candidates.Select(candidate => candidate.PointId),
        Is.EqualTo(new[] { 20 }));
}

[Test]
public void KeepsNearbyCandidatesWhenTargetIsStopped()
{
    var locator = CreateLocator(
        new AiPursuitTargetCandidateSource(10, 1, Vector3.UnitX),
        new AiPursuitTargetCandidateSource(20, 4, -Vector3.UnitX));

    var candidates = locator.FindCandidates(
        Vector3.Zero,
        Vector3.Zero,
        maximumDistanceSquared: 49,
        maximumCandidates: 16);

    Assert.That(candidates.Select(candidate => candidate.PointId),
        Is.EqualTo(new[] { 10, 20 }));
}
```

Adicionar teste que descarta ponto fora do limite e teste que limita a 16 resultados.

- [ ] **Step 2: Executar teste focado e observar RED**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --filter AiPursuitTargetLocatorTests --no-restore
```

Expected: FAIL de compilação.

- [ ] **Step 3: Implementar o localizador**

Criar os contratos:

```csharp
public readonly record struct AiPursuitTargetCandidateSource(
    int PointId,
    float DistanceSquared,
    Vector3 Forward);

public readonly record struct AiPursuitTargetCandidate(
    int PointId,
    float DistanceSquared,
    float DirectionAlignment);
```

O construtor público consulta `spline.KdTree.NearestNeighbors(position, maximumCandidates)`. O construtor interno recebe `Func<Vector3, int, IReadOnlyList<AiPursuitTargetCandidateSource>>`.

Aplicar estas regras exatas:

```csharp
const float DirectionSpeedThreshold = 2.0f;
const float MinimumDirectionAlignment = 0.25f;
```

Quando `velocity.Length() >= 2`, normalizar velocidade e forward e remover alinhamentos menores que `0.25`. Quando parado, não filtrar por direção. Ordenar por `DistanceSquared`, depois por `PointId`, garantindo resultado determinístico.

- [ ] **Step 4: Executar testes focados e suíte do core**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --filter AiPursuitTargetLocatorTests --no-restore
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
```

Expected: todos passam.

- [ ] **Step 5: Commit e push do core**

```powershell
git -C external/AssettoServer add -- AssettoServer/Server/Ai/Routing/AiPursuitTargetLocator.cs AssettoServer.Tests/Server/Ai/AiPursuitTargetLocatorTests.cs
git -C external/AssettoServer commit -m "feat(ai): locate pursuit target candidates"
git -C external/AssettoServer push arthur codex/p0.6-junction-routing
```

---

### Task 3: Navegador stateful, cache e tolerância

**Files:**
- Create: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitNavigator.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitNavigatorTests.cs`

**Interfaces:**
- Consumes: `AiRoutePlanner`, `AiPursuitTargetLocator`, `AiRouteSearchLimits`.
- Produces: `AiPursuitNavigationOptions`, `AiPursuitRouteState`, `AiPursuitNavigationStatus`, `AiPursuitRouteUpdateKind`, `AiPursuitNavigationResult` e `AiPursuitNavigator.Update(...)`.

- [ ] **Step 1: Escrever testes comportamentais da máquina de estados**

Cobrir com planners/localizadores injetados:

```csharp
[Test]
public void SelectsReachableAlternativeWhenNearestCandidateHasNoPath()
{
    var navigator = CreateNavigator(
        candidates: [Candidate(20, 1), Candidate(30, 4)],
        routes: new Dictionary<(int, int), AiRouteSearchResult>
        {
            [(0, 20)] = NoRoute(AiRouteSearchFailure.Unreachable),
            [(0, 30)] = Route(0, 5, 30)
        });

    var result = navigator.Update(Request(now: 0), previous: null, Options());

    Assert.Multiple(() =>
    {
        Assert.That(result.Status, Is.EqualTo(AiPursuitNavigationStatus.Active));
        Assert.That(result.State!.TargetPointId, Is.EqualTo(30));
    });
}

[Test]
public void KeepsLastRouteDuringGraceThenReturnsDefinitiveNoRoute()
{
    var navigator = CreateNavigatorWithInitialRouteThenFailure();
    var active = navigator.Update(Request(now: 0), null, Options(graceMs: 2000));

    var temporary = navigator.Update(Request(now: 1000), active.State, Options(graceMs: 2000));
    var definitive = navigator.Update(Request(now: 2001), temporary.State, Options(graceMs: 2000));

    Assert.Multiple(() =>
    {
        Assert.That(temporary.Status,
            Is.EqualTo(AiPursuitNavigationStatus.RouteTemporarilyUnavailable));
        Assert.That(temporary.State!.Plan, Is.SameAs(active.State!.Plan));
        Assert.That(definitive.Status, Is.EqualTo(AiPursuitNavigationStatus.NoRoute));
    });
}
```

Adicionar casos para mesma spline, branch A, branch B, duas junctions, cache reutilizado, extensão forward, mudança de ramo, limite de nós, recuperação e busca inicial sem rota.

- [ ] **Step 2: Executar os testes e observar RED**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --filter AiPursuitNavigatorTests --no-restore
```

Expected: FAIL de compilação.

- [ ] **Step 3: Criar contratos imutáveis do navegador**

```csharp
public sealed record AiPursuitNavigationOptions(
    AiRouteSearchLimits SearchLimits,
    int RouteGraceMilliseconds,
    int MaximumTargetCandidates = 16,
    float ExtensionDistanceMeters = 500,
    int ExtensionMaxVisitedNodes = 5000);

public enum AiPursuitNavigationStatus
{
    Active,
    RouteTemporarilyUnavailable,
    NoRoute
}

public enum AiPursuitRouteUpdateKind
{
    Selected,
    Reused,
    Extended,
    Recalculated,
    Recovered
}

public sealed record AiPursuitRouteState(
    int TargetPointId,
    AiRoutePlan Plan,
    long Revision,
    long? FirstFailureMilliseconds);

public sealed record AiPursuitNavigationResult(
    AiPursuitNavigationStatus Status,
    AiPursuitRouteState? State,
    AiPursuitRouteUpdateKind? UpdateKind,
    AiRouteSearchFailure SearchFailure,
    int VisitedNodes);
```

Fornecer um construtor público com `AiRoutePlanner`/`AiPursuitTargetLocator` e este construtor interno para os testes:

```csharp
internal AiPursuitNavigator(
    Func<int, IReadOnlySet<int>, AiRouteSearchLimits, AiRouteSearchResult> tryPlan,
    Func<Vector3, Vector3, float, int, IReadOnlyList<AiPursuitTargetCandidate>> findCandidates)
```

Nos testes, `CreateNavigator` montará esses delegates a partir dos dicionários fornecidos. `Route(int[])` criará um `AiRoutePlan` com distâncias cumulativas de 10 m por aresta; `NoRoute(failure)` criará `AiRouteSearchResult(null, failure, 1)`. `CreateNavigatorWithInitialRouteThenFailure` usará uma fila contendo um resultado válido seguido por resultados `Unreachable`.

- [ ] **Step 4: Implementar seleção, reutilização, extensão e recálculo**

Assinatura:

```csharp
public AiPursuitNavigationResult Update(
    int policePointId,
    Vector3 targetPosition,
    Vector3 targetVelocity,
    float maximumTargetDistanceSquared,
    long nowMilliseconds,
    AiPursuitRouteState? previous,
    AiPursuitNavigationOptions options)
```

Algoritmo:

1. Obter candidatos ordenados do locator.
2. Se `previous` existe e o candidato anterior continua no conjunto, localizar o ponto policial no corredor, remover os nós já percorridos e recalcular `DistanceFromStartMeters` a partir de zero.
3. Se o alvo avançou, tentar extensão a partir de `previous.TargetPointId` com 500 m/5.000 nós; concatenar sem duplicar o nó inicial e somar as distâncias da extensão ao final do corredor restante.
4. Se extensão falhar ou branch mudar, executar busca completa do policial para cada candidato em ordem e aceitar a primeira rota válida. Passar a cada tentativa somente o saldo de `MaximumVisitedNodes`, somar os nós visitados e parar quando o saldo chegar a zero.
5. Incrementar `Revision` apenas em seleção, extensão, recálculo ou recuperação; reutilização mantém a revisão.
6. Em falha com estado anterior, definir `FirstFailureMilliseconds` e preservar plano/decisões até `RouteGraceMilliseconds`.
7. Em falha inicial ou tolerância expirada, retornar `NoRoute` sem estado.

- [ ] **Step 5: Executar testes focados e suíte do core**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --filter AiPursuitNavigatorTests --no-restore
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
```

Expected: todos passam; testes anteriores do planner e locator permanecem verdes.

- [ ] **Step 6: Commit e push do core**

```powershell
git -C external/AssettoServer add -- AssettoServer/Server/Ai/Routing/AiPursuitNavigator.cs AssettoServer.Tests/Server/Ai/AiPursuitNavigatorTests.cs
git -C external/AssettoServer commit -m "feat(ai): cache stateful pursuit routes"
git -C external/AssettoServer push arthur codex/p0.6-junction-routing
```

---

### Task 4: Integrar o navegador ao AiState e às decisões físicas

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiState.cs`
- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/JunctionEvaluatorTests.cs`
- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitControlTests.cs`

**Interfaces:**
- Consumes: `AiPursuitNavigator.Update()` e `SessionManager.ServerTimeMilliseconds`.
- Produces: `AiPursuitTrackingOptions`, `AiPursuitRouteDiagnostics`, `AiPursuitJunctionDecision` e `AiState.TrackPursuit(EntryCar, AiPursuitTrackingOptions)`.

- [ ] **Step 1: Escrever testes de contratos e decisões explícitas persistentes**

Adicionar:

```csharp
[Test]
public void ExplicitDecisionsCanBeReplacedWithoutChangingNativeFallback()
{
    var evaluator = new JunctionEvaluator(_ => 0.0f, savesState: true);
    evaluator.SetExplicitDecisions(new Dictionary<int, bool> { [7] = true });
    evaluator.SetExplicitDecisions(new Dictionary<int, bool> { [8] = true });

    Assert.Multiple(() =>
    {
        Assert.That(evaluator.WillTakeJunction(7), Is.False);
        Assert.That(evaluator.WillTakeJunction(8), Is.True);
    });
}
```

Nos testes de controle, validar opções inválidas, preservação espacial de 1.500 m e conversão de uma rota temporária em resultado público.

- [ ] **Step 2: Executar testes focados e observar RED onde os contratos não existem**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --filter "AiPursuitControlTests|JunctionEvaluatorTests" --no-restore
```

- [ ] **Step 3: Estender os contratos públicos do core**

```csharp
public sealed record AiPursuitTrackingOptions(
    float MaximumSpatialDistanceMeters,
    float MaximumRouteDistanceMeters,
    int MaximumVisitedNodes,
    int RouteGraceMilliseconds);

public sealed record AiPursuitJunctionDecision(
    int JunctionId,
    bool TakeBranch,
    int EndPointId);

public sealed record AiPursuitRouteDiagnostics(
    long Revision,
    AiPursuitRouteUpdateKind UpdateKind,
    int PolicePointId,
    int TargetPointId,
    float RouteDistanceMeters,
    int VisitedNodes,
    IReadOnlyList<AiPursuitJunctionDecision> JunctionDecisions);

public sealed record AiPursuitTrackingResult(
    AiPursuitTrackingStatus Status,
    float? RouteDistanceMeters,
    float TargetSpeedMetersPerSecond,
    AiPursuitRouteDiagnostics? RouteDiagnostics = null);
```

- [ ] **Step 4: Substituir o planner direto pelo navegador no AiState**

Alterar assinatura para:

```csharp
public AiPursuitTrackingResult TrackPursuit(
    EntryCar target,
    AiPursuitTrackingOptions options)
```

Manter a checagem espacial antes da navegação. Chamar o navegador com `CurrentSplinePointId`, posição/velocidade do alvo e `ServerTimeMilliseconds`. Em `Active`, publicar decisões e snapshot. Em `RouteTemporarilyUnavailable`, preservar snapshot e decisões. Em `NoRoute`, chamar `ReleasePursuit()`.

Substituir o campo `AiRoutePlan Route` de `AiPursuitSnapshot` por `AiPursuitRouteState NavigationState`; preservar `DesiredSpeedMetersPerSecond` nas atualizações do navegador.

Converter cada decisão em `AiPursuitJunctionDecision` usando o `EndPointId` real de `_spline.Junctions[junctionId]`.

Não modificar `Move()`, `Teleport()`, `CalculateTangents()`, `SplineLookahead()` ou `DetectObstacles()` além das integrações já existentes da P0.5.

- [ ] **Step 5: Executar a suíte completa do core e build Release**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
dotnet build external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release --no-restore
```

Expected: zero falhas e zero erros. O aviso preexistente `CS0162` em `OnlineEventGenerator.cs` pode permanecer.

- [ ] **Step 6: Commit e push do core**

```powershell
git -C external/AssettoServer add -- AssettoServer/Server/Ai/AiPursuitControl.cs AssettoServer/Server/Ai/AiState.cs AssettoServer.Tests/Server/Ai/AiPursuitControlTests.cs AssettoServer.Tests/Server/Ai/JunctionEvaluatorTests.cs
git -C external/AssettoServer commit -m "feat(ai): route police through pursuit junctions"
git -C external/AssettoServer push arthur codex/p0.6-junction-routing
```

---

### Task 5: Configuração P0.6 e ponte tipada do plugin

**Files:**
- Modify: `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`
- Modify: `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs`
- Modify: `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs`
- Modify: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs`
- Modify: `tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiStateTests.cs`
- Modify: `tests/PoliceChasePlugin.Tests/Ai/FakePoliceAiSlotSource.cs`

**Interfaces:**
- Consumes: contratos públicos criados na Task 4.
- Produces: `PolicePursuitTrackingOptions`, `PolicePursuitRouteDiagnostics`, `PolicePursuitJunctionDecision` e ponte completa core/plugin.

- [ ] **Step 1: Escrever testes de defaults e validação**

Acrescentar aos defaults:

```csharp
Assert.That(configuration.PursuitRouteSearchMaxDistanceMeters, Is.EqualTo(20_000));
Assert.That(configuration.PursuitRouteSearchMaxVisitedNodes, Is.EqualTo(50_000));
Assert.That(configuration.PursuitRouteGraceMilliseconds, Is.EqualTo(2_000));
Assert.That(configuration.PursuitNoRouteProbeIntervalMilliseconds, Is.EqualTo(2_000));
```

Adicionar casos `0`, `-1`, `float.NaN` e `float.PositiveInfinity` para distâncias; `0` e `-1` para contagens/intervalos. Validar também que `PursuitRouteSearchMaxDistanceMeters >= PursuitMaxDistanceMeters`.

- [ ] **Step 2: Escrever teste da ponte com diagnóstico real**

Criar um resultado nativo com revisão 3, pontos 100/200 e junction 7 apontando para `EndPointId=300`. Verificar igualdade de todos os campos no resultado do plugin e verificar que as quatro opções recebidas pelo fake nativo correspondem à configuração.

- [ ] **Step 3: Executar testes focados e observar RED**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --filter "PoliceChaseConfigurationTests|PoliceChaseConfigurationValidatorTests|AssettoServerPoliceAiStateTests" --no-restore
```

- [ ] **Step 4: Implementar configuração e validação**

Adicionar:

```csharp
public float PursuitRouteSearchMaxDistanceMeters { get; set; } = 20_000;
public int PursuitRouteSearchMaxVisitedNodes { get; set; } = 50_000;
public int PursuitRouteGraceMilliseconds { get; set; } = 2_000;
public int PursuitNoRouteProbeIntervalMilliseconds { get; set; } = 2_000;
```

Usar `float.IsFinite` em `Must()` para as duas distâncias e regras `GreaterThan(0)` para contagens/intervalos.

- [ ] **Step 5: Implementar contratos e mapeamento tipados**

No plugin:

```csharp
public sealed record PolicePursuitTrackingOptions(
    float MaximumSpatialDistanceMeters,
    float MaximumRouteDistanceMeters,
    int MaximumVisitedNodes,
    int RouteGraceMilliseconds);

public sealed record PolicePursuitJunctionDecision(
    int JunctionId,
    bool TakeBranch,
    int EndPointId);

public enum PolicePursuitRouteUpdateKind
{
    Selected,
    Reused,
    Extended,
    Recalculated,
    Recovered
}

public sealed record PolicePursuitRouteDiagnostics(
    long Revision,
    PolicePursuitRouteUpdateKind UpdateKind,
    int PolicePointId,
    int TargetPointId,
    float RouteDistanceMeters,
    int VisitedNodes,
    IReadOnlyList<PolicePursuitJunctionDecision> JunctionDecisions);

public sealed record PolicePursuitTrackingResult(
    PolicePursuitTrackingStatus Status,
    float? RouteDistanceMeters,
    float TargetSpeedMetersPerSecond,
    PolicePursuitRouteDiagnostics? RouteDiagnostics = null);
```

Alterar `IPoliceAiState.TrackPursuit` para receber `PolicePursuitTrackingOptions`. Mapear todos os enums e records explicitamente em `AssettoServerNativePolicePursuitState`, sem casts dependentes da ordem dos enums.

- [ ] **Step 6: Executar testes focados e suíte do plugin**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --filter "PoliceChaseConfigurationTests|PoliceChaseConfigurationValidatorTests|AssettoServerPoliceAiStateTests" --no-restore
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore
```

Expected: todos passam.

- [ ] **Step 7: Commit e push do plugin**

```powershell
git add -- src/PoliceChasePlugin/PoliceChaseConfiguration.cs src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs tests/PoliceChasePlugin.Tests/Ai/AssettoServerPoliceAiStateTests.cs tests/PoliceChasePlugin.Tests/Ai/FakePoliceAiSlotSource.cs
git commit -m "feat: bridge P0.6 pursuit navigation"
git push origin main
```

---

### Task 6: Suspensão de NoRoute, sondagem e logs sem spam

**Files:**
- Modify: `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs`
- Modify: `tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs`

**Interfaces:**
- Consumes: `PolicePursuitTrackingOptions` e `PolicePursuitRouteDiagnostics`.
- Produces: lifecycle suspenso por alvo, sondagem monotônica e logs de rota/junction.

- [ ] **Step 1: Criar relógio manual nos testes e fila de resultados no fake**

Usar `TimeProvider` sem nova dependência:

```csharp
private sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => _timestamp;
    public void AdvanceMilliseconds(long milliseconds) => _timestamp += milliseconds;
}
```

Alterar `FakePoliceAiState` para consumir uma `Queue<PolicePursuitTrackingResult>` antes de usar `NextTrackingResult`.

- [ ] **Step 2: Escrever testes de suspensão e recuperação**

```csharp
[Test]
public void NoRouteSuspendsSameTargetUntilProbeFindsRoute()
{
    var context = CreateContext();
    context.State.Enqueue(ActiveResult(revision: 1));
    context.State.Enqueue(NoRouteResult());
    context.State.Enqueue(ActiveResult(revision: 2));

    context.Service.UpdateOnce();
    context.Service.UpdateOnce();
    context.Service.UpdateOnce();
    context.Clock.AdvanceMilliseconds(1999);
    context.Service.UpdateOnce();
    context.Clock.AdvanceMilliseconds(1);
    context.Service.UpdateOnce();

    Assert.Multiple(() =>
    {
        Assert.That(context.State.TrackRequests, Has.Count.EqualTo(3));
        Assert.That(LogCount("Pursuit started"), Is.EqualTo(2));
        Assert.That(LogCount("Pursuit ended"), Is.EqualTo(1));
        Assert.That(LogCount("route probe succeeded"), Is.EqualTo(1));
    });
}
```

No teste, `ActiveResult(long revision)` deve criar diagnóstico `Selected` com police point 100, target point 200, 150 m e lista vazia de junctions. `LogCount(string text)` deve contar `_sink.Events` cujo `RenderMessage()` contenha `text`.

Adicionar testes para: sondagem que continua sem rota; desconexão limpa suspensão; troca de alvo não herda suspensão; estado policial não inicializado não força probe; release idempotente; perda temporária não suspende; logs de revisão não repetem; cada junction é registrada somente quando aparece ou muda.

- [ ] **Step 3: Executar testes focados e observar RED**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --filter PolicePursuitServiceTests --no-restore
```

- [ ] **Step 4: Injetar TimeProvider e construir opções**

Manter o construtor compatível com Autofac:

```csharp
public PolicePursuitService(
    PoliceChaseConfiguration configuration,
    PoliceAiService policeAiService,
    PoliceTargetService targetService,
    TimeProvider? timeProvider = null)
```

Usar `_timeProvider.GetTimestamp()` e `_timeProvider.GetElapsedTime(start, end)`; não usar relógio de parede.

Construir uma única instância de `PolicePursuitTrackingOptions` a partir da configuração e fornecê-la em toda chamada a `TrackPursuit`.

- [ ] **Step 5: Implementar suspensão e sondagem**

Adicionar estado:

```csharp
private byte? _suspendedTargetSessionId;
private long _lastNoRouteProbeTimestamp;
private long? _lastLoggedRouteRevision;
private readonly Dictionary<int, bool> _loggedJunctionDecisions = new();
```

Regras:

1. Ao receber `NoRoute` durante perseguição ativa, liberar uma vez, registrar término, suspender o mesmo alvo e salvar o timestamp monotônico atual.
2. Enquanto suspenso e `_timeProvider.GetElapsedTime(_lastNoRouteProbeTimestamp, now)` for menor que o intervalo configurado, retornar sem chamar o core.
3. Na sondagem, `NoRoute` apenas reagenda; não gera `started`/`ended`.
4. `Active` limpa suspensão, registra `route probe succeeded` e então inicia perseguição.
5. Desconexão ou troca de alvo limpa suspensão e diagnósticos.
6. Instância policial não inicializada aguarda lifecycle nativo, sem spawn ou probe.

- [ ] **Step 6: Implementar logs de rota e junction por revisão**

Em `Active`, quando a revisão mudar:

```csharp
Log.Information(
    "[PoliceChase] Pursuit route selected: police {PoliceSessionId}, target {TargetSessionId}, policePoint {PolicePointId}, targetPoint {TargetPointId}, distance {RouteDistanceMeters:0.0}, junctions {JunctionCount}",
    policeSessionId,
    targetSessionId,
    diagnostics.PolicePointId,
    diagnostics.TargetPointId,
    diagnostics.RouteDistanceMeters,
    diagnostics.JunctionDecisions.Count);
```

Usar `route recalculated` para `Recalculated`/`Recovered`, `route selected` para `Selected`, e não emitir informação para `Reused`/`Extended` sem nova junction. Registrar `Pursuit junction decision` somente quando o par `(JunctionId, TakeBranch)` ainda não tiver sido registrado na perseguição atual ou tiver mudado.

- [ ] **Step 7: Executar testes focados e suíte completa do plugin**

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --filter PolicePursuitServiceTests --no-restore
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore
```

Expected: todos passam, incluindo regressões P0.5.

- [ ] **Step 8: Commit e push do plugin**

```powershell
git add -- src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs tests/PoliceChasePlugin.Tests/Pursuit/PolicePursuitServiceTests.cs tests/PoliceChasePlugin.Tests/Ai/FakePoliceAiSlotSource.cs
git commit -m "fix: prevent no-route reacquisition loop"
git push origin main
```

---

### Task 7: Documentação, submodule e verificação final

**Files:**
- Modify: `docs/P0.5-perseguicao-basica.md`
- Create: `docs/P0.6-navegacao-bifurcacoes.md`
- Modify: `README.md`
- Modify: `external/AssettoServer` gitlink

**Interfaces:**
- Consumes: hashes e resultados finais das Tasks 1–6.
- Produces: instalação reproduzível, roteiro real da Shutoko e `main` fixada no commit exato do core P0.6.

- [ ] **Step 1: Corrigir a afirmação da P0.5**

Substituir a promessa de bifurcações alcançáveis por uma seção de limitação conhecida:

```markdown
## Limitação confirmada no servidor real

A P0.5 validou perseguição e controle longitudinal, mas não validou navegação confiável em bifurcações da Shutoko. A localização por um único ponto e o limite de rota acoplado a 1.500 m podem produzir `no-route` em conexões visualmente alcançáveis. A correção pertence à P0.6.
```

- [ ] **Step 2: Criar o guia P0.6**

Documentar em `docs/P0.6-navegacao-bifurcacoes.md`:

- evidência do pacote: 31 splines, 318.796 pontos e ~502 km;
- causa técnica completa;
- classes do core envolvidas;
- diferenças entre distância espacial e orçamento de rota;
- configuração YAML completa;
- logs e significado dos IDs;
- hash final do core;
- comandos de pull/submodule/build;
- artefatos `AssettoServer.dll` e `PoliceChasePlugin.dll`;
- limitações mantidas;
- roteiro com duas bifurcações, rota impossível e verificação dos dez AI de tráfego.

- [ ] **Step 3: Atualizar o README e o gitlink**

Adicionar o link P0.6 ao índice e confirmar:

```powershell
git -C external/AssettoServer rev-parse HEAD
git diff --submodule=short
```

Expected: gitlink muda de `5432683` para o commit final da branch P0.6.

- [ ] **Step 4: Executar verificação completa em série**

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore
dotnet build external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release --no-restore
dotnet build src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release --no-restore
git diff --check
git -C external/AssettoServer diff --check
```

Expected: zero falhas, zero erros, apenas o possível aviso preexistente `CS0162` do core.

- [ ] **Step 5: Confirmar artefatos**

```powershell
Get-Item external/AssettoServer/AssettoServer/bin/Release/net8.0/win-x64/AssettoServer.dll
Get-Item artifacts/PoliceChasePlugin/PoliceChasePlugin.dll
```

Expected: ambos existem e têm timestamp do build atual.

- [ ] **Step 6: Fazer revisão final do diff**

Confirmar explicitamente:

```powershell
git diff 25793c4 --stat
git -C external/AssettoServer diff 5432683 --stat
rg -n "AiPerPlayerTargetCount|MaxAiTargetCount|TrafficDensity|PlayerRadiusMeters" src tests external/AssettoServer/AssettoServer/Server/Ai -g '*.cs'
rg -n "Status\.Position\s*=|\.Teleport\(" src external/AssettoServer/AssettoServer/Server/Ai -g '*.cs'
```

Expected: nenhuma atribuição/configuração nova nos parâmetros globais; nenhuma correção de perseguição por teleporte.

- [ ] **Step 7: Commit e push da documentação e gitlink**

```powershell
git add -- README.md docs/P0.5-perseguicao-basica.md docs/P0.6-navegacao-bifurcacoes.md external/AssettoServer
git commit -m "docs: add P0.6 server validation guide"
git push origin main
```

- [ ] **Step 8: Confirmar remotos e árvores limpas**

```powershell
git ls-remote origin refs/heads/main
git -C external/AssettoServer ls-remote arthur refs/heads/codex/p0.6-junction-routing
git status --short
git -C external/AssettoServer status --short
```

Expected: hashes locais iguais aos remotos e nenhum arquivo pendente.

## Acceptance Checklist

- [ ] Uma rota alcançável por uma ou mais junctions permanece ativa.
- [ ] O alvo pode ser associado a um candidato alcançável mesmo quando o ponto geometricamente mais próximo estiver em outro ramo.
- [ ] A separação física de 1.500 m permanece independente do orçamento de rota de 20 km.
- [ ] A busca nunca ultrapassa 50.000 nós.
- [ ] Falha transitória preserva rota por no máximo 2.000 ms.
- [ ] Falha definitiva retorna `NoRoute` e libera controle.
- [ ] O mesmo alvo não produz loop imediato de reaquisição.
- [ ] A reaquisição exige sondagem com rota válida.
- [ ] Junctions são dirigidas por IDs reais do pacote.
- [ ] Não existe teleporte, respawn deliberado ou nova aresta lateral.
- [ ] O tráfego comum e os comportamentos P0.4/P0.5 permanecem intactos.
- [ ] Core tests, plugin tests e ambos os builds Release passam.
- [ ] O teste real continua sendo necessário antes de considerar a P0.6 validada na Shutoko.
