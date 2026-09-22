# Police Chase P0.8 — Aggressive Pursuit & Close-Contact Driving

## Objetivo

Adicionar um controlador longitudinal dedicado à viatura policial para que ela recupere distância, aproxime-se do jogador, mantenha pressão a poucos metros, permita contato físico controlado e retome a perseguição depois de uma colisão.

A P0.8 decide somente com que velocidade e agressividade a polícia avança. A P0.6 e a P0.7 continuam decidindo a rota, a faixa, as bifurcações e a transição lateral física.

## Baseline aprovada

- PoliceChasePlugin: `7edc4732d0de4296c0f5398de557df0089ac1bfa`.
- AssettoServer core: `910f4727cc5891b85efc7f2f46c5dc8e2aae05ce`.
- Runtime validado: AssettoServer `0.0.54+910f4727cc`.
- 106 testes automatizados aprovados antes da P0.8.
- P0.4, P0.5, P0.6 e P0.7 são baseline estável.
- O `CAR_12` permanece como a única AI policial dedicada.
- Os dez carros normais de tráfego e os slots de jogadores não recebem o controlador P0.8.

## Causa do comportamento atual

`PolicePursuitSpeedPolicy.Calculate()` calcula a velocidade solicitada desta forma:

```text
distanceError = max(0, routeDistance - PursuitDesiredDistanceMeters)
requestedSpeed = playerSpeed + distanceError * 0,25 km/h por metro
```

Com `PursuitDesiredDistanceMeters: 50`, o bônus de aproximação desaparece quando a distância de rota chega a 50 metros. A solicitação passa a ser igual à velocidade do jogador, eliminando closing speed positivo.

Em seguida, `AiState.DetectObstacles()` trata o próprio jogador perseguido como obstáculo comum. Cada estado AI possui `_minObstacleDistance` aleatória entre 8 e 12 metros. Abaixo dela, a velocidade alvo vira zero. Antes disso, a regra nativa reduz a velocidade quando o jogador entra em:

```text
brakingDistance(policeSpeed - playerSpeed, AiDeceleration) * 2 + 20 metros
```

A distância confortável resulta da combinação dessas duas regras. `MinAiSafetyDistanceMeters`, `PlayerRadiusMeters`, densidade e overbooking não são a causa e não serão modificados.

## Decisão arquitetural

Será criado `AiPursuitDrivingController` no core do AssettoServer. Ele será independente de `AiPursuitNavigator`, `AiPursuitLaneSelector` e `AiLaneChangeController`.

```text
Pursuit target
  ├── P0.6/P0.7 navigation → rota, faixa, junction e transição lateral
  └── P0.8 driving        → velocidade, closing speed, contato e recovery
```

`AiState.TrackPursuit()` continuará atualizando a rota e passará ao controlador:

- distância de rota;
- distância física livre entre os veículos;
- velocidade da polícia;
- velocidade do jogador;
- fase da troca de faixa;
- revisão da rota;
- instante atual;
- opções P0.8.

O resultado será armazenado no snapshot imutável da perseguição e consumido por `AiState.DetectObstacles()`. O snapshot continuará sendo trocado de forma atômica para não introduzir locks entre o update da perseguição e o loop de obstáculos.

## Modelo de distância

O controlador usa duas medidas distintas:

- **distância de rota** para `CatchUp` e `Approach`, preservando a semântica forward-only da P0.6;
- **clearance físico** para `ClosePressure` e `Contact`.

O clearance físico é calculado conservadoramente como:

```text
max(0,
    centerDistance
    - police.VehicleLengthPreMeters
    - target.VehicleLengthPostMeters)
```

Ele representa aproximadamente a folga entre a frente da viatura e a traseira do jogador quando ambos seguem no mesmo sentido. O controlador não usa essa aproximação para escolher rota ou faixa.

## Estados longitudinais

### `CatchUp`

- distância acima de `PursuitCatchUpDistanceMeters`;
- busca closing speed elevado, limitado por `PursuitMaxClosingSpeedKph`;
- respeita `PursuitMaxSpeedKph`, curvas, tráfego e rota.

### `Approach`

- entre as faixas de catch-up e close pressure;
- reduz progressivamente o closing speed desejado conforme a distância diminui;
- reage à frenagem e aceleração do jogador.

### `ClosePressure`

- clearance pequeno, mas ainda acima da zona de contato;
- não recria a antiga margem de 50 metros;
- acompanha a velocidade do jogador mantendo closing speed pequeno e positivo.

### `Contact`

- clearance igual ou inferior a `PursuitContactDistanceMeters`;
- se contato estiver habilitado, usa `PursuitContactClosingSpeedKph` como pressão máxima;
- se contato estiver desabilitado, closing speed desejado torna-se zero;
- nenhum impulso, força, dano, teleport ou snap é aplicado.

### `Recovery`

- ativado quando o cliente do target reporta colisão com a AI policial;
- mantém target, rota, lane state e snapshot;
- substitui somente para essa colisão a pausa aleatória de tráfego de 1 a 3 segundos;
- reduz closing speed e permite que a separação física volte a uma faixa segura;
- sai por condição física de distância/velocidade, sem timer longo;
- se o jogador abrir distância, retorna imediatamente a `Approach` ou `CatchUp`.

O AssettoServer movimenta a AI cinematicamente pela spline. A P0.8 não simula yaw ou dinâmica própria da viatura. O contato acontece pelo carro AI publicado aos clientes e deverá ser confirmado no servidor real.

## Controle por velocidade relativa

```text
closingSpeed = policeSpeed - playerSpeed
requestedSpeed = playerSpeed + desiredClosingSpeed(distance, state)
```

`desiredClosingSpeed` forma um envelope contínuo:

- máximo em `CatchUp`;
- interpolado para baixo em `Approach`;
- pequeno em `ClosePressure`;
- limitado a `PursuitContactClosingSpeedKph` em `Contact`;
- conservador em `Recovery` e durante transição lateral física.

Como `AiState.SetTargetSpeed()` compara a solicitação com `CurrentSpeed`, o erro entre closing speed real e closing speed desejado determina naturalmente se a AI acelera ou freia. A aceleração e desaceleração existentes continuam sendo integradas por `AiState.Update()`.

Para evitar oscilação entre aceleração e frenagem:

- estados usam histerese nas fronteiras de distância;
- o erro de closing speed possui zona morta pequena;
- a velocidade solicitada não muda quando o erro fica dentro da zona morta;
- a rampa física existente de `AiAcceleration`/`AiDeceleration` permanece responsável pela variação de `CurrentSpeed`.

`AiMaxEngineRpm` continua apenas como valor visual do `CarStatus`; ele não participa do controlador.

## Integração com obstáculos

`AiState.DetectObstacles()` dará tratamento especial somente quando todas estas condições forem verdadeiras:

1. existe snapshot de pursuit ativo;
2. aggressive driving está habilitado;
3. o obstáculo jogador possui o mesmo `SessionId` do target;
4. o controlador retornou uma decisão válida.

Nesse caso, o target não passa pelo hard-stop genérico de 8–12 metros nem pelo buffer genérico de frenagem `+20 m`. A velocidade controlada pela P0.8 já considera distância e closing speed.

Continuam inalterados:

- limites de curvas da spline;
- obstáculos AI;
- jogadores que não são o target;
- safety do lane change;
- comportamento de qualquer AI sem pursuit ativa.

O mecanismo global de ignorar todos os obstáculos depois de uma parada longa não será usado como implementação da P0.8.

## Integração com P0.7

O controlador observa a fase existente do `AiLaneChangeController`:

- `WaitingForGap`: preserva obstacle/safety checks e não força a manobra;
- `Changing`: reduz o envelope de closing speed, sem impor uma parada normal;
- `Cooldown`/`None`: restaura agressividade longitudinal normal.

Uma mudança de route revision não reinicia o estado longitudinal. Em perda temporária de rota, o último pedido já limitado permanece como hoje; a P0.8 não inventa rota nem substitui grace/probe. Em `NoRoute` definitivo, `MaxDistanceExceeded`, target indisponível ou release explícito, o controlador é limpo junto com o snapshot.

## Colisão

`AiBehavior.OnCollision()` continuará usando o comportamento nativo para tráfego normal. Para o estado AI que estiver perseguindo exatamente o cliente remetente:

- registra a colisão no controlador;
- não agenda `StopForCollision()` com pausa aleatória;
- entra em `Recovery` sem limpar navegação;
- publica diagnóstico para o plugin no próximo update.

Colisões com outros jogadores ou envolvendo AI sem pursuit continuam chamando `StopForCollision()` exatamente como antes.

## Configuração

```yaml
PursuitAggressiveDrivingEnabled: true
PursuitContactEnabled: true
PursuitCatchUpDistanceMeters: 100
PursuitCloseDistanceMeters: 15
PursuitContactDistanceMeters: 3
PursuitMaxClosingSpeedKph: 35
PursuitContactClosingSpeedKph: 5
```

Regras de validação:

- distâncias finitas e positivas;
- `PursuitCatchUpDistanceMeters > PursuitCloseDistanceMeters`;
- `PursuitCloseDistanceMeters > PursuitContactDistanceMeters`;
- closing speeds finitos e não negativos;
- `PursuitContactClosingSpeedKph <= PursuitMaxClosingSpeedKph`;
- `PursuitMaxSpeedKph` permanece como limite absoluto.

Quando `PursuitAggressiveDrivingEnabled` for `false`, `PolicePursuitSpeedPolicy` e `PursuitDesiredDistanceMeters` mantêm o comportamento legado. Nenhuma configuração local do usuário será alterada automaticamente.

## Diagnóstico

O core retorna `AiPursuitDrivingDiagnostics` com:

- revisão;
- estado longitudinal;
- route distance;
- physical clearance;
- player speed;
- police speed;
- closing speed;
- desired closing speed;
- requested target speed;
- motivo da decisão;
- fase do lane change;
- evento de colisão/recovery, quando houver.

O plugin registra somente mudanças de estado, mudança de motivo relevante, entrada/saída de recovery e eventos de contato. Pequenas mudanças contínuas de distância ou velocidade não geram uma linha por update.

`Revision` pertence ao diagnóstico longitudinal, não à frequência do loop. Ela só aumenta quando muda o estado, o motivo semântico, o estado de colisão/recovery ou o target. Distância e velocidades atualizadas permanecem disponíveis no diagnóstico mais recente, mas não criam uma nova revisão sozinhas.

## Testes obrigatórios

1. jogador muito distante entra em `CatchUp`;
2. jogador distante e mais rápido produz aceleração adequada;
3. closing speed elevado reduz a solicitação;
4. proximidade não tenta restaurar 50 metros;
5. zona de contato permite pressão controlada;
6. frenagem forte do jogador reduz a velocidade solicitada;
7. nova aceleração do jogador retoma approach/catch-up;
8. colisão preserva a pursuit;
9. recovery retorna ao comportamento normal;
10. `WaitingForGap` preserva safety;
11. `Changing` limita closing speed sem quebrar a transição;
12. completion restaura agressividade normal;
13. route revision preserva o estado longitudinal;
14. perda temporária de rota preserva a semântica P0.6;
15. tráfego normal não recebe P0.8;
16. `PursuitMaxSpeedKph` permanece respeitado;
17. `PursuitMaxDistanceMeters` mantém sua semântica;
18. modo P0.8 desabilitado mantém o comportamento anterior;
19. outro jogador continua sendo obstáculo;
20. AI de tráfego continua sendo obstáculo;
21. diagnósticos são deduplicados por significado.

Todos os 106 testes da baseline permanecem obrigatórios, salvo substituição explicitamente justificada de uma expectativa longitudinal antiga.

## Fora de escopo

- PIT maneuver;
- múltiplas viaturas;
- roadblocks ou interceptadores;
- Heat/Wanted, HUD, sirene ou luzes;
- força, dano ou reposicionamento artificial;
- reverse routing, teleport ou respawn como catch-up;
- alteração do route graph, splines ou parâmetros globais de tráfego;
- hardcode de IDs da Shutoko.

## Validação

A validação local exige testes Release completos e builds Release do core e do plugin. A entrega deve informar commits, ponteiro do submodule, arquivos, resultados, warnings, configuração e roteiro de teste.

A P0.8 não será considerada validada até o usuário realizar o deploy manual e confirmar catch-up, aproximação, pressão, contato, frenagem, recovery, lane change e bifurcações no servidor real.
