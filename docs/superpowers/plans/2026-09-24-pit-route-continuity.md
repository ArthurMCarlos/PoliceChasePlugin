# PIT Route Continuity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Permitir Armed → Started com revisão nova válida, sem remover aborts reais.

**Architecture:** A revisão é metadado, não certificado de segurança. Manter
os gates atuais e acrescentar contexto apenas aos eventos já deduplicados.

**Tech Stack:** C#, .NET, NUnit, Serilog; plugin com core em submodule.

**Spec:** `docs/P0.8.1-investigacao-PIT-RouteLost.md` e solicitação anexada de 2026-09-24.

**Registro de execução:** implementação inline aprovada e executada em 2026-09-24.
Os passos abaixo preservam o plano original; resultados RED/GREEN, revisão,
arquivos e procedimento de deploy estão no documento da investigação.

## Global Constraints

PIT: 6 m, 10 km/h, 0,8 m, 1200 ms e 3000 ms inalterados.
Preservar P0.6/P0.7/P0.8, zero-length, target exclusion e tráfego comum.
Não reutilizar grace para permitir PIT sem navegação ativa.
Execução inline na main, conforme preferência do usuário; push só após testes/builds.

## Review Focus

- Revisões sucessivas em Attempting não reiniciam o tempo de commit.
- Perda transitória real aborta mesmo que a perseguição retenha snapshot.
- Revisão nova com desalinhamento produz GeometryInvalid.
- Revisão nova com side blocker não permite troca oportunista do lado armado.
- Contexto de abort ausente não inventa snapshot atual a partir do anterior.

## Task 1 — Reproduzir e corrigir continuidade

**Files:** `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitPitControllerTests.cs`,
novo `AiPursuitPitRouteContinuityTests.cs` no mesmo diretório;
`external/AssettoServer/AssettoServer/Server/Ai/AiPursuitPitController.cs`.

**Interfaces:** AiPursuitNavigator.Update → AiPursuitNavigationResult;
AiPursuitPitController.Update(AiPursuitPitRequest) → AiPursuitPitDecision.

- [ ] Reproduzir a falha com o helper Request existente:

```csharp
var controller = new AiPursuitPitController();
var armed = controller.Update(Request());
var started = controller.Update(Request(armed.State, 1100) with { RouteRevision = 2 });
Assert.That(started.Diagnostics?.EventKind, Is.EqualTo(AiPursuitPitEventKind.Started));
```

- [ ] Acrescentar fixture com planner/navigator reais, grafo forward-only de
  segmentos de 1,5 m, avançando policePoint/targetPoint; verificar plano Active
  de 3 m e depois 1,5 m (também zero), revisão incrementada e Started.
- [ ] Cobrir RouteValid=false, SameLane=false, Behind=false, JunctionNear=true,
  WaitingForGap/Changing, lado armado bloqueado, target alterado, distância e
  closing inseguras; exigir os motivos atuais, mesmo com revisão incrementada.
- [ ] Cobrir Attempting com revisões sucessivas e expiração original de commit.
- [ ] Executar filtro FullyQualifiedName~AiPursuitPit; registrar falhas esperadas.
- [ ] Separar o teste antigo de Disabled do requisito incorreto de revisão;
  corrigir somente o gate:

```csharp
if (!request.RouteValid)
    return AiPursuitPitAbortReason.RouteLost;
```

- [ ] Reexecutar o filtro; exigir todos verdes antes da instrumentação.

## Task 2 — Contexto de transição e entrega

**Files:** core `AiPursuitControl.cs`, `AiPursuitPitController.cs`, `AiState.cs`;
plugin `Ai/IPoliceAiSlotSource.cs`, `Ai/AssettoServerPoliceAiSlotSource.cs`,
`Pursuit/PolicePursuitService.cs`; testes dos respectivos consumidores.

**Interfaces:** adicionar propriedade opcional `Continuity` ao diagnóstico PIT
do core/plugin, preservando construtores atuais. Contexto somente observacional.

```csharp
// Campos do contexto; usar enums correspondentes em cada camada.
// ArmedRouteRevision, CurrentRouteRevision: long
// PreviousPhase: AiPursuitPitPhase
// NavigationActive, RouteAvailable, TargetAligned, JunctionNear: bool
// PolicePoint, TargetPoint: int?
// RouteDistanceMeters: float?
// LaneChangePhase: AiLaneChangePhase
```

- [ ] Estender testes de mapeamento/log para verificar revisão armada distinta
  da atual, pontos/medidas e deduplicação; executar antes da implementação.
- [ ] Capturar contexto no ponto da decisão em AiState; fallback não declara
  rota atual disponível com base somente no snapshot anterior.
- [ ] Mapear contexto no adapter e anexá-lo ao LogPitTransition existente:

```csharp
// Acrescentar ao template e aos argumentos, sem criar log por frame:
"; continuity {@Continuity}"
// argumento: diagnostics.Continuity
```

- [ ] Executar regressões completas e builds:

```powershell
dotnet test -c Release
dotnet test ./external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release
dotnet build ./external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release
dotnet build ./src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release
```

- [ ] Registrar contagens, ignorados explícitos e avisos no documento da investigação.
- [ ] Revisar diff para confirmar escopo e ausência de mudanças de configuração.
- [ ] Commit/push no remoto arthur do core; depois commit/push de main do plugin
  com gitlink já acessível. Verificar sincronização e fornecer hashes reais.
- [ ] Entregar atualização via pull/submodule, rebuild/deploy conjunto e roteiro
  de log Armed → Started; novos aborts devem trazer contexto. Não declarar
  validação física antes do teste real.
