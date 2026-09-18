# P0.6 Real Spline Transition Correction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restaurar a navegação forward da P0.6 quando o jogador muda entre splines equivalentes detectadas pelo AssettoServer e fornecer diagnóstico suficiente para o novo teste real.

**Architecture:** `AiPursuitTargetLocator` continuará limitado a 16 sementes geométricas, mas expandirá cada semente por `AiSpline.GetLanes()` antes do planejamento. O grafo continuará sem arestas laterais; o planner apenas poderá terminar num ponto equivalente alcançável. Resultados tipados carregarão evidência compacta da localização e busca até os logs de transição do plugin.

**Tech Stack:** C# 12, .NET 8, NUnit 3, AssettoServer 0.0.54 forkado, Autofac, Serilog, Git submodule.

**Spec:** `docs/superpowers/specs/2026-09-18-p0-6-real-spline-transition-correction.md`

## Global Constraints

- Não avançar para P0.7.
- Manter `PursuitMaxDistanceMeters=1500`, rota `20000 m`, `50000` nós e grace `2000 ms`.
- Não alterar parâmetros globais de tráfego.
- Não adicionar `LeftId`/`RightId` como arestas do planner.
- Não ordenar troca de faixa, teleporte, marcha a ré, despawn ou respawn.
- Preservar suspensão e sondagem após `NoRoute`.
- Não versionar o `fast_lane.aip` real.
- Implementar com TDD e publicar cada alteração no repositório correspondente.

---

### Task 1: Reproduzir e corrigir a equivalência topológica no locator

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitTargetLocator.cs`
- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitTargetLocatorTests.cs`
- Modify: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiPursuitNavigatorTests.cs`

**Interfaces:**
- Consumes: `AiSpline.GetLanes(int)` e `SplinePointOperations.GetForwardVector(int)`.
- Produces: sementes espaciais, destinos equivalentes deduplicados e diagnóstico de rejeição.

- [ ] Escrever teste que fornece 16 sementes desconectadas e um ponto equivalente alcançável fora desse conjunto.
- [ ] Executar o teste e confirmar `NoRoute`/ausência do equivalente no estado atual.
- [ ] Expandir somente sementes espacialmente válidas por `GetLanes`, filtrar direção e deduplicar.
- [ ] Confirmar que o navigator escolhe o equivalente alcançável e que nenhuma aresta lateral foi criada.
- [ ] Executar testes focados e suíte do core.
- [ ] Commitar e publicar no novo branch do core.

### Task 2: Testar parser, cache, locator e planner em conjunto

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Splines/FastLaneParser.cs`
- Create: `external/AssettoServer/AssettoServer.Tests/Server/Ai/RealSplineTransitionIntegrationTests.cs`

**Interfaces:**
- Consumes: `.aip`, `TrafficConfiguration`, `MutableAiSpline`, `AiSplineWriter`, `AiSpline`, `AiPursuitNavigator`.
- Produces: construtor interno testável do parser e dois testes de integração.

- [ ] Criar teste mínimo que gera três splines paralelas densas e demonstra que os 16 vizinhos não contêm o corredor alcançável.
- [ ] Executar e observar falha antes da correção do locator.
- [ ] Criar construtor interno do parser baseado em track/AiParams, sem mudar o construtor de produção.
- [ ] Passar o pacote pelo parser e cache reais, então exigir rota ativa no corredor equivalente.
- [ ] Criar teste explícito controlado por `POLICE_CHASE_FAST_LANE_AIP` para `171036 -> 227470 -> 171048`.
- [ ] Executar localmente contra `C:\Users\arthur.carlos\Downloads\fast_lane.aip` e registrar distância/IDs reais.
- [ ] Commitar e publicar no branch do core.

### Task 3: Diagnóstico tipado de localização e busca

**Files:**
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiRouteSearch.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiRoutePlanner.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/Routing/AiPursuitNavigator.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiPursuitControl.cs`
- Modify: `external/AssettoServer/AssettoServer/Server/Ai/AiState.cs`
- Modify: corresponding core tests.

**Interfaces:**
- Produces: falha exata, nós visitados, distância máxima explorada, junction edges examinadas, sementes, equivalentes e rejeições.

- [ ] Escrever testes para distância explorada, junctions examinadas e diagnóstico preservado na grace.
- [ ] Executar e observar falhas nos campos ausentes.
- [ ] Acrescentar campos imutáveis aos resultados sem log por frame.
- [ ] Mapear o diagnóstico em `AiState.TrackPursuit()` para Active, grace e NoRoute.
- [ ] Executar testes focados e suíte do core.
- [ ] Commitar e publicar no branch do core.

### Task 4: Mapear e registrar transições no plugin

**Files:**
- Modify: `src/PoliceChasePlugin/Ai/IPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`
- Modify: `src/PoliceChasePlugin/Pursuit/PolicePursuitService.cs`
- Modify: plugin tests and fakes.

**Interfaces:**
- Consumes: diagnóstico tipado do core.
- Produces: um log compacto na entrada em grace, recuperação/recálculo e término definitivo.

- [ ] Escrever teste que exige IDs/candidatos/falha no primeiro `temporarily lost` e ausência de repetição.
- [ ] Executar e observar falha por diagnóstico ausente.
- [ ] Mapear contratos sem reflection.
- [ ] Emitir logs somente em transições; preservar o loop de sondagem corrigido.
- [ ] Executar testes focados e suíte do plugin.
- [ ] Commitar e publicar na `main`.

### Task 5: Documentação, gitlink e verificação final

**Files:**
- Modify: `docs/P0.6-navegacao-bifurcacoes.md`
- Modify: `external/AssettoServer` gitlink
- Modify: `README.md` only if the existing index needs clarification.

- [ ] Registrar que `44028e3` falhou no primeiro teste real e documentar a causa comprovada.
- [ ] Documentar novo SHA do core, logs esperados e roteiro que começa em `171036/171048` e muda para a spline equivalente.
- [ ] Executar teste real explícito, suítes completas, builds Release e `git diff --check`.
- [ ] Confirmar que parâmetros globais, teleporte e arestas laterais não mudaram.
- [ ] Atualizar gitlink, commit, push e conferir hashes remotos/árvores limpas.

## Acceptance Checklist

- [ ] O pacote real retorna rota `171036 -> 171048` quando o alvo está em `227470`.
- [ ] A distância planejada permanece aproximadamente 19,3 m.
- [ ] A expansão usa `GetLanes()` e não cria movimento lateral.
- [ ] Junctions configuradas continuam dirigidas por `SplineJunction`.
- [ ] Falhas futuras informam candidatos, rejeições, limites e busca sem spam.
- [ ] Grace e reprobe permanecem inalterados.
- [ ] Todos os testes e builds passam.
- [ ] A documentação mantém a validação real como pendente.
