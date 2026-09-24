# P0.9 Close Pursuit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Implement inline, task by task, with RED/GREEN tests and one independent final review. Checkboxes track execution.

**Goal:** Recuperar distância de jogadores rápidos, contornar tráfego e retornar atrás do alvo, preservando PIT e fuga sustentada.

**Architecture:** Intenção longitudinal, decisão tática e lifecycle possuem políticas separadas e estado por perseguição. AiState aplica os resultados pelos escritores de movimento existentes, depois dos limites de segurança. Plugin configura, apresenta diagnósticos e impede rearme da conexão que escapou.

**Tech Stack:** C#/.NET 8, NUnit, FluentValidation, Serilog; core AssettoServer em submodule.

**Spec:** `docs/superpowers/specs/2026-09-24-p0-9-close-pursuit-design.md`.

## Global Constraints

- Trabalhar inline na main como autorizado; preservar alterações externas que surgirem.
- Opt-in desligado por padrão; modo antigo permanece funcional e sem assistência.
- Sem mudança global de AI/tráfego, teleporte, snap, dano ou força no jogador.
- Somente viatura dedicada recebe assistência. CAR_0..9 e CAR_10/11 não mudam.
- PIT permanece em 6 m, 10 km/h, 0,8 m, 1200 ms, 3000 ms no cenário validado.
- Assistência: 60–250 m, vantagem máxima 80 km/h, aceleração máxima 10 m/s², jerk positivo 5 m/s³, teto técnico 400 km/h.
- Bypass: limitação por terceiro durante 1000 ms, déficit mínimo 10 km/h, horizonte 150 m, retorno com folga mínima 12 m mais safety/cooldown existentes.
- Fuga: mais de 800 m por 15000 ms; corte duro existente 1500 m por padrão; rearme da mesma conexão suspenso por 60000 ms.
- Valores são configuráveis, iniciais e não calibrados. Não prometer captura ilimitada.
- Core e plugin publicados juntos, core primeiro; nenhuma publicação de código com suíte ou build falhando.

## Review Focus

1. Velocidade desejada alta não basta: verificar aceleração aplicada e ausência de sobrescrita posterior (Task 2).
2. Proximidade física com rota longa não pode gerar boost, nem fechamento perigoso após desaceleração brusca do alvo (Task 2).
3. Reciclagem de AI/slot e conexão nova não podem herdar identidade tática ou punição antiga (Tasks 3–4).
4. ShouldRetainPursuit pode liberar a AI antes do update do plugin; preservar comunicação do motivo de saída (Task 4).
5. Seleção de faixa não pode disputar o controle com PIT, junction ou transição já comprometida (Task 3).

## Estrutura e sequência

Paths core abaixo relativos a `external/AssettoServer/AssettoServer/Server/Ai/`;
testes core relativos a `external/AssettoServer/AssettoServer.Tests/Server/Ai/`.
Paths plugin são relativos à raiz deste repositório.
Não recriar P0.6–P0.8.1. Novos arquivos concentram políticas puras; integração
fica nos pontos já existentes. Não criar um segundo loop de física.

### Task 1: Contratos opt-in, configuração e validação

**Files:** core novo `AiPursuitCloseOptions.cs`, modificar `AiPursuitControl.cs`;
plugin `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`,
`PoliceChaseConfigurationValidator.cs`, `Ai/IPoliceAiSlotSource.cs`,
`Ai/AssettoServerPoliceAiSlotSource.cs`, `Pursuit/PolicePursuitService.cs`.
**Tests:** core novo `AiPursuitCloseOptionsTests.cs`; plugin
`tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs`,
`PoliceChaseConfigurationValidatorTests.cs`, `Ai/AssettoServerPoliceAiStateTests.cs`.

**Interfaces:** acrescentar propriedade opcional `ClosePursuit` aos tracking options de ambas as camadas, preservando construtores existentes. Contrato core em SI:

```csharp
public sealed record AiPursuitCloseOptions(
    float AssistStartMeters, float AssistFullMeters,
    float MaxAdvantageMetersPerSecond, float MaxAccelerationMetersPerSecondSquared,
    float MaxJerkMetersPerSecondCubed, float MaxSpeedMetersPerSecond,
    int ObstacleHoldMilliseconds, float ObstacleDeficitMetersPerSecond,
    float BypassLookaheadMeters, float ReturnClearanceMeters,
    float EscapeDistanceMeters, int EscapeHoldMilliseconds,
    int RearmDelayMilliseconds);
```

`PolicePursuitCloseOptions` espelha os campos em SI; não expor o tipo core na API do plugin.
Configuração YAML usa prefixo `PursuitClose` nos nomes correspondentes, com
unidades `Kph` onde aplicável, e `PursuitCloseEnabled=false`. O nome
`PursuitCloseAssistStartMeters` não conflita com o `PursuitCloseDistanceMeters`
existente da P0.8. Construir opções somente quando enabled; exigir aggressive
driving e lane change ativos, sem exigir PIT ligado.

- [ ] RED: testar modo desligado → ClosePursuit null, modo ligado → conversão 80/3.6f e 400/3.6f, persistência das opções PIT antigas.

```csharp
Assert.That(configuration.PursuitCloseEnabled, Is.False);
Assert.That(configuration.PursuitCloseMaxAdvantageKph, Is.EqualTo(80));
Assert.That(configuration.PursuitCloseMaxSpeedKph, Is.EqualTo(400));
```

- [ ] Escrever testes tabelados que rejeitam NaN/infinito, distâncias <=0, full<=start, start<=distância ClosePressure, escape<=full, escape>=corte duro, aceleração/jerk<=0, tempos<=0, horizonte menor que distância de transição e teto técnico abaixo do máximo antigo configurado. Validar tanto no plugin quanto na entrada pública core; valores desativados não mudam execução antiga.
- [ ] Rodar testes focados e confirmar falha pela ausência do contrato/comportamento antes de implementá-lo. Adicionar os records, defaults, regras FluentValidation e conversões explícitas. Não aplicar assistência ainda.
- [ ] GREEN: executar suítes core e plugin; verificar que parâmetros antigos e conversões anteriores continuam passando. Guardar resultado no ledger; commit somente com verificação da tarefa aprovada.

### Task 2: Assistência longitudinal e integração física

**Files:** novos core `AiPursuitRecoveryAssist.cs`, testes `AiPursuitRecoveryAssistTests.cs` e `AiPursuitRecoveryAssistIntegrationTests.cs`;
modificar `AiPursuitDrivingController.cs`, `AiPursuitControl.cs`, `AiState.cs`.
**Consumes:** AiPursuitCloseOptions, decisão P0.8 e medidas do update atual.
**Produces:** intenção assistida, aceleração positiva limitada por tempo, diagnóstico do limitador efetivo.

```csharp
public sealed record AiRecoveryAssistInput(
    float PhysicalClearanceMeters, float RouteDistanceMeters,
    float TargetSpeedMetersPerSecond, float PoliceSpeedMetersPerSecond,
    float BaseRequestedSpeedMetersPerSecond, float NativeAcceleration,
    float Deceleration, bool NavigationActive, bool RecoveryActive);
public sealed record AiRecoveryAssistDecision(
    float RequestedSpeedMetersPerSecond, float AccelerationLimit,
    bool Active);
// Classe pura AiPursuitRecoveryAssist:
// static AiRecoveryAssistDecision Evaluate(AiPursuitCloseOptions options,
//     AiRecoveryAssistInput input);
// static float StepAcceleration(float current, float desired, float jerk, float dtSeconds);
```

- [ ] RED: testes reais da política para alvos 200/250/300 km/h, distância 300 m, solicitação base equivalente à velocidade do alvo, desaceleração 8 m/s². Em trecho livre, resultados esperados 280/330/380 km/h; alvo 390 → limite 400. Testar ausência de assistência em Recovery, navegação indisponível, medidas inválidas e proximidade física de 10 m com rota 1000 m.

```csharp
Assert.That(AiPursuitRecoveryAssist.StepAcceleration(2, 10, 5, .2f), Is.EqualTo(3));
Assert.That(AiPursuitRecoveryAssist.StepAcceleration(2, 10, 5, .1f), Is.EqualTo(2.5f));
```

- [ ] Implementar fator smoothstep com distância efetiva mínima entre clearance físico e rota. Aceleração desejada interpola a nativa até o máximo assistido. Não usar a distância de rota para ativar boost perto fisicamente.

```csharp
float x = Math.Clamp((distance - start) / (full - start), 0f, 1f);
float weight = x * x * (3f - 2f * x);
float assisted = baseSpeed + weight * Math.Max(0, targetSpeed + advantage - baseSpeed);
float brakingEnvelope = MathF.Sqrt(targetSpeed * targetSpeed
    + 2f * deceleration * Math.Max(0, physicalClearance - start));
// Aplicar envelope apenas à parcela adicional; perto, usar a política P0.8.
// A validação de finitude ocorre antes da fórmula e depois do resultado.
```

- [ ] Integrar no driving controller: quando modo novo ativo, usar teto técnico em vez do antigo teto 180 ao calcular também a política base. Isso permite seguir alvo >180 perto, mas não aumenta os limites de closing/PIT. No modo antigo, caminho original sem alteração.
- [ ] Integrar aceleração no AiState no local de SetTargetSpeed/Update, usando dt real e campo individual de aceleração assistida; não escrever em EntryCar.AiAcceleration. Obstáculos e curvas sempre reduzem a intenção depois. Frenagem ignora jerk positivo e mantém ação imediata.
- [ ] Capturar motivo de limitação em DetectObstacles antes da aplicação: Requested, TechnicalCap, Curve, AiObstacle, PlayerObstacle, LaneSafety ou Recovery. Alteração observacional não muda prioridade dos limites antigos.
- [ ] Durante perseguição close ativa, impedir caminho `_ignoreObstaclesUntil` de pular safety; eliminar ativação desse timeout apenas para esse modo. Depois de desativação/reset, não herdar timer antigo de bypass perigoso. Tráfego comum continua no código anterior.
- [ ] RED/GREEN de integração: mesmo pedido com modo off/on produz aceleração nativa/assistida respectivamente no ponto realmente usado para movimento; curva e terceiro ainda limitam velocidade efetiva. Testar alvo que freia abruptamente, dt zero/negativo/grande, resultado finito e reset em release/despawn/troca de alvo. Não validar somente o record de intenção.
- [ ] Rodar suíte completa core e plugin, builds Release; guardar contagens e avisos.

### Task 3: Bypass e retorno táticos sobre a P0.7

**Files:** novos core `Routing/AiPursuitTrafficTactics.cs`, testes
`AiPursuitTrafficTacticsTests.cs`, `AiPursuitTrafficBypassIntegrationTests.cs`;
modificar `Routing/AiPursuitLaneSelector.cs`, `Routing/AiPursuitLaneChangePipeline.cs`,
`AiState.cs`, mapeamentos de motivos de lane change no plugin e respectivos testes.

**Interfaces:** acrescentar motivações `TrafficBypass` e `TrafficReturn` sem
mudar valores anteriores; produzir AiPursuitLaneSelection usando o construtor
e DestinationPlan existentes. Não fazer o controlador tático escrever cursores.

```csharp
internal enum AiTrafficTacticPhase { Follow, Bypass, Return }
internal sealed record AiTrafficTacticState(
    AiTrafficTacticPhase Phase, long? BlockedSinceMilliseconds,
    long? ObstacleIdentity, int? CommittedDestinationPoint);
internal sealed record AiTrafficLaneOption(
    AiPursuitLaneSelection Selection, float FreeDistanceMeters,
    bool SafeToEnter);
internal sealed record AiTrafficTacticRequest(
    long NowMilliseconds, bool BlockedByNonTarget, long? ObstacleIdentity,
    float SpeedDeficitMetersPerSecond, float CurrentFreeDistanceMeters,
    bool ObstaclePassedWithClearance, bool TargetLaneReached,
    bool PitBusy, bool JunctionHasPriority, bool TransitionCommitted,
    IReadOnlyList<AiTrafficLaneOption> Candidates,
    AiPursuitLaneSelection? ReturnSelection, AiTrafficTacticState Previous);
internal sealed record AiTrafficTacticDecision(
    AiTrafficTacticState State, AiPursuitLaneSelection? Selection);
// AiPursuitTrafficTactics.Update(AiPursuitCloseOptions, AiTrafficTacticRequest)
// returns AiTrafficTacticDecision.
```

- [ ] RED: bloqueio 999 ms não inicia; aos 1000 ms, déficit 10 km/h e candidato livre/seguro inicia Bypass. Ambos os lados bloqueados retornam seleção null e não desativam frenagem. Verificar observações repetidas com o mesmo obstáculo versus novo obstáculo (timer reinicia).
- [ ] Implementar comparação de candidatos: excluir unsafe/sem melhoria de espaço; minimizar custo de rota ao alvo entre os restantes, maior distância livre como próximo desempate e Left antes de Right como último. Avaliar no máximo dois vizinhos imediatos, com observação de tráfego limitada a 150 m e busca total respeitando budget compartilhado, não multiplicado por candidato.
- [ ] Reutilizar verificação de relação física recíproca e mesma direção. Novo método selector `SelectForTrafficBypass` aceita ponto atual, alvo físico e opções/budget; retorna candidatos usando planos válidos até o alvo, sem exigir uma junction para justificar lateralidade. Espaço livre na faixa vem de observação real e alinhamento à faixa candidata, não do comprimento do plano sozinho.
- [ ] Identidade de obstáculo usa estado AI + geração monotônica de spawn, ou conexão para jogador não-alvo. Não confiar apenas no byte SpawnCounter que pode dar volta. Expor contador interno de geração se necessário; não muda reserva ou comportamento de spawn. Remover referências ao reciclar/despawn.
- [ ] Integrar prioridade: transição comprometida → exigência FutureJunction → restrição PIT/offset → decisão tática → alinhamento normal. Bypass suprime alinhamento normal que tentaria desfazer a ultrapassagem. Alteração de faixa do alvo não interrompe a transição atual.
- [ ] Confirmar avanço para Bypass somente quando a transição foi aceita; rejeição por preparação/cooldown não marca ultrapassagem iniciada. Espera por gap mantém memória e reavalia segurança real antes de executar.
- [ ] Return usa faixa atual do alvo e só começa com obstáculo efetivamente atrás, folga >=12 m e safety/cooldown P0.7 aprovados. Despawn remove a condição de ultrapassar aquele obstáculo, nunca a checagem de gap. Return conclui somente ao chegar à faixa-alvo; novo obstáculo pode manter estado sem zigue-zague.
- [ ] Testes integrados obrigatórios com pipeline/cursores reais:

```csharp
Assert.That(decision.State.Phase, Is.EqualTo(AiTrafficTacticPhase.Bypass));
Assert.That(decision.Selection!.Motivation, Is.EqualTo(AiPursuitLaneMotivation.TrafficBypass));
// Após transição e ultrapassagem, faixa-alvo mudou: o retorno usa o novo alvo.
Assert.That(returnDecision.Selection!.ToPointId, Is.EqualTo(physicalTargetLanePoint));
// physicalTargetLanePoint é literal da fixture, não calculado pela política sob teste.
```

- [ ] Cobrir junction, PIT Armed/Attempting, offset retornando ao centro, espera por gap, cooldown, curva sem espaço, candidato oposto/não adjacente, slot reciclado, geração >255 e mudança rápida de alvo. Confirmar nenhum snap nem aresta lateral no grafo.
- [ ] Suítes completas e builds; registrar limitações do pacote real explicitamente, sem contar testes ignorados como aprovados.

### Task 4: Fuga e bloqueio de rearme por conexão

**Files:** novo core `AiPursuitEscapePolicy.cs`, testes `AiPursuitEscapePolicyTests.cs`;
modificar core `AiState.cs`, `AiPursuitControl.cs` e, se necessário para transportar
o evento de corte duro, `AiBehavior.cs` sem mudar regras dos slots comuns.
Plugin `Players/IPolicePlayerSource.cs`, `Players/AssettoServerPlayerSource.cs`,
`Players/PoliceTargetService.cs`, `Pursuit/PolicePursuitService.cs`, adapter/contratos;
testes `Players/PoliceTargetServiceTests.cs`, `Players/FakePolicePlayerSource.cs`,
`Pursuit/PolicePursuitServiceTests.cs`, core lifecycle/integration.

```csharp
public sealed record AiPursuitEscapeState(long? FarSinceMilliseconds);
public sealed record AiPursuitEscapeDecision(AiPursuitEscapeState State, bool Escaped);
// AiPursuitEscapePolicy.Update(float spatialDistance, bool navigationActive,
//     long nowMilliseconds, AiPursuitCloseOptions options, AiPursuitEscapeState previous)
// returns AiPursuitEscapeDecision.
```

- [ ] RED: 801 m em t=1000, depois t=15999 → não escapou; t=16000 → escapou. Um update em 800 m, medida inválida ou navegação indisponível reinicia timer (continuidade não foi demonstrada). Não tratar distância de rota como distância espacial.
- [ ] Implementar relógio monotônico por tentativa; reset em conexão/alvo/estado novo. Acrescentar Escaped aos enums de resultado ao final, preservar valores existentes. Avaliar corte duro primeiro, navegação depois, timer somente em navegação ativa.
- [ ] Auditar ciclo ShouldRetainPursuit → lifecycle: se a AI perder inicialização antes de TrackPursuit, preservar motivo terminal para entrega única. Usar evento/resultado pendente por estado, não manter a viatura fisicamente além do corte duro. Teste deve executar essa ordem real; getter não deve disparar log nem somar tempo.
- [ ] Acrescentar `long ConnectionGeneration` ao snapshot de jogador como propriedade init. Fonte mantém geração monotônica vinculada à instância ACTcpClient; snapshots repetidos mantêm identidade. Disconnect atrasado não remove conexão nova no mesmo slot. Fake da suite modela reconexão explicitamente.
- [ ] PoliceTargetService recebe TimeProvider opcional e oferece exclusão temporária da identidade atual e refresh de seleção. Todas as operações usam o lock existente. Em escape/corte duro, excluir por 60000 ms, limpar alvo atual e selecionar outro elegível; refresh no update permite expirar exclusão mesmo sem novo evento de conexão.

```csharp
// Contrato de seleção a adicionar:
// void SuppressCurrentTarget(TimeSpan duration);
// void Refresh();
// Supressão usa (SessionId, ConnectionGeneration), não só SessionId.
```

- [ ] Testar mesma conexão bloqueada imediatamente, jogador 11 selecionável, expiração em 60000 ms, nova conexão no slot 10 não herda bloqueio, reset Stop e ausência de crescimento ilimitado da tabela.
- [ ] Confirmar release limpa assistência/tática sem perder evento terminal; nenhum respawn especial para aproximar polícia do fugitivo. Modo antigo mantém seleção e lifecycle anteriores.
- [ ] Suítes completas e builds; observar que fuga não substitui NoRoute/grace e não altera reserva dedicada.

### Task 5: Diagnóstico, regressão, documentação e publicação

**Files:** core novo `AiPursuitCloseDiagnostics.cs`, contratos core/plugin,
`AiState.cs`, adapter, `PolicePursuitService.cs`; testes de mapeamento/log;
novo `docs/P0.9-close-pursuit.md`; atualizar estado real P0.8.1 nos relatórios
existentes com atribuição ao relato do usuário.

**Interfaces:** diagnóstico opcional em TrackingResult, mantido em campos por
estado. Fase/motivo observacional não retroalimenta controle. Transportar:
distâncias física/rota, velocidade alvo/polícia, velocidade desejada/efetiva,
aceleração aplicada, motivo limitante, fase tática, revisão de evento e fuga.
Valores ausentes ficam nullable, não falsos zeros.

- [ ] RED: sink de logs recebe uma transição de assistência/Bypass/Return/fuga,
  não repete por frame; resumo só após 5000 ms e reset correto após release.
  Mapear unidades e motivos com casos de null, alvo novo e estado reciclado.
- [ ] Implementar logging com relógio monotônico e deduplicação, preservando
  side safety/continuity do PIT. Registrar motivo real após todos os limites,
  não rotular pedido limitado por curva como incapacidade de aceleração.
- [ ] Executar e ler resultados completos:

```powershell
dotnet test -c Release
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj -c Release
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj -c Release
dotnet build external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release
dotnet build src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release
git diff --check
git -C external/AssettoServer diff --check
```

Expected: zero falhas/erros; relatar avisos existentes e NotExecuted. Testes
de navegação real não são substituídos por teste sintético nem declarados verdes
se não rodarem. Baseline anterior: 158 plugin, 262 core executados, 8 explícitos
não executados; não fixar contagem como expectativa após novos testes.

- [ ] Escrever guia operacional com YAML opt-in completo, tabelas de limites,
  caminho dos DLLs, proteção de backup e teste comparativo off/on. Registrar
  observação separada de intenção alta e velocidade/distância realmente obtidas.
- [ ] Revisão independente única do diff completo e dos cinco Review Focus;
  corrigir achados relevantes com regressões RED/GREEN e nova suíte completa.
- [ ] Commit/push core no remoto arthur, branch codex/p0.7-real-lane-change-fix.
  Confirmar hash remoto antes de registrar/publicar gitlink na main do plugin.
  Stage explícito dos arquivos da tarefa; não incluir alterações alheias.
- [ ] Entregar hashes, testes/builds, avisos, configuração, instruções de pull
  e submodule, e roteiro de servidor: reta 200–300 km/h, desvio/retorno com e
  sem gap, junction, PIT, corte duro, fuga sustentada e outro jogador elegível.
  Não declarar P0.9 validada fisicamente antes do relato do servidor.

## Revisão do plano e decisões de integração

Tasks 1–5 formam uma entrega coordenada, não três produtos independentes.
Task 1 define opções comuns; 2 e 3 produzem comandos no mesmo AiState;
4 encerra e reseta esses comandos; 5 observa e valida o conjunto.
Adaptações de interfaces menores durante implementação precisam ser registradas
no ledger, sem mudar defaults, prioridades ou escopo silenciosamente.

No modo novo, o teto técnico substitui o teto antigo inclusive perto do alvo,
mas não substitui os limites de velocidade relativa Contact/PIT. Essa distinção
é necessária para seguir um alvo a 250 km/h sem voltar a ficar preso em 180.
Todos os ramos de reset e de perda temporária precisam ser testados como
integração; helpers que retornam números corretos sozinhos não bastam.

Plano aguardando revisão do usuário. Método de execução já escolhido: inline.
