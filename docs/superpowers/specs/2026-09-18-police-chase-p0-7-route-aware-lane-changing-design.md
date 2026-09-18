# Police Chase P0.7 — route-aware physical lane changing

## Objetivo

Permitir que a única instância dedicada da polícia mude fisicamente para uma faixa adjacente quando essa manobra ainda torna alcançável uma junction necessária da perseguição.

A P0.7 não adiciona ultrapassagem ofensiva, PIT, roadblocks, múltiplas viaturas, direção contrária, teleporte, sirene, luzes, Heat/Wanted ou HUD.

## Restrições preservadas

- O grafo continua contendo somente `SplinePoint.NextId` e `SplineJunction.EndPointId`.
- `GetLanes()` continua representando equivalência/localização, não uma aresta de movimento.
- Cada manobra atravessa somente uma relação imediata `LeftId` ou `RightId`.
- Junctions reais continuam sendo executadas por `JunctionEvaluator`.
- O roteamento continua forward-only.
- Grace, no-route, suspensão e probe da P0.6 permanecem ativos.
- O lifecycle, a reserva dedicada e os dez carros dinâmicos permanecem inalterados.
- O tráfego normal não recebe lane changing novo.
- Não será usada reflection.

## Componentes

### `AiPursuitLaneSelector`

Componente puro no namespace de routing. Ele recebe:

- o ponto físico atual da polícia;
- os candidatos localizados para o alvo;
- o resultado/corredor atual;
- os limites de busca já configurados;
- o estado/revisão da decisão anterior.

O seletor primeiro tenta a faixa atual. Se ela continuar alcançando o corredor, nenhuma troca é solicitada.

Quando a faixa atual não alcança o corredor confirmado do alvo, o seletor avalia somente `LeftId` e `RightId` imediatos que:

- existem;
- possuem a mesma direção;
- estão dentro de uma largura lateral plausível;
- possuem continuidade forward suficiente para uma manobra;
- conseguem produzir uma rota válida com o mesmo `AiRoutePlanner`.

Ele retorna uma decisão tipada com faixa origem, faixa destino, direção lateral, rota de destino, distância até a primeira decisão relevante e motivo. A escolha prioriza:

1. permanecer na faixa atual;
2. uma única faixa adjacente;
3. rota forward válida com espaço para concluir a manobra;
4. menor custo de rota como desempate.

Não haverá busca combinatória por todas as faixas. Para atravessar duas faixas, a segunda decisão só será avaliada após concluir a primeira.

### `AiLaneChangeController`

Estado por `AiState`, criado mas inativo para bots normais. Estados conceituais:

- `None`;
- `WaitingForGap`;
- `Changing`;
- `Cooldown`.

Uma solicitação guarda:

- ponto/faixa origem;
- ponto/faixa destino adjacente;
- direção;
- distância total da manobra;
- revisão da rota que originou a solicitação;
- rota válida a ser usada após a conclusão.

Durante `WaitingForGap`, revisões podem cancelar ou substituir a solicitação. Depois que `Changing` começa, a manobra fica comprometida: uma revisão não causa retorno lateral imediato. A nova rota será reavaliada ao terminar, evitando ping-pong.

### Integração com `AiState`

`AiState.TrackPursuit()` continuará sendo o único ponto público de controle da perseguição. Ele:

1. localiza o alvo;
2. atualiza a rota normal;
3. solicita avaliação lateral antes de publicar uma perda de rota definitiva;
4. publica decisões reais de junction;
5. inclui o estado tipado da lane change em `AiPursuitTrackingResult`.

`AiState.Update()` continuará calculando velocidade, rotação, pneus e flags. Quando não existir lane change, seu caminho atual permanecerá byte-for-byte conceitualmente igual. Quando a manobra estiver ativa, ele usará a trajetória transitória descrita abaixo.

## Trajetória física

No início da manobra serão criados dois cursores longitudinais:

- cursor origem, inicializado no progresso físico atual;
- cursor destino, inicializado no ponto adjacente alinhado.

Em cada tick ambos avançam a mesma `moveMeters`. Cada cursor avalia sua própria posição e tangente sobre os pontos reais da spline. O progresso lateral é a distância percorrida dividida pela distância total da manobra.

A posição é interpolada com uma função smoothstep de derivadas nulas nas extremidades. A tangente usada para rotação e velocidade inclui a derivada lateral, evitando snap de orientação.

Enquanto o progresso for menor que 1:

- `CurrentSplinePointId` continua representando a cadeia de origem;
- a posição permanece sobre a trajetória transitória;
- nenhuma aresta lateral é adicionada ao route planner.

Quando o progresso chega a 1:

1. a posição já coincide com a trajetória destino;
2. `CurrentSplinePointId` e `_currentVecProgress` recebem o cursor destino;
3. tangentes normais são recalculadas;
4. a rota é retomada/recalculada a partir desse ponto;
5. inicia o cooldown.

Se a cadeia destino termina ou diverge antes da distância exigida, a manobra não começa.

## Antecipação

O sistema não tenta prever uma escolha que o jogador ainda não realizou. A preparação começa assim que o movimento/localização do alvo confirma outro corredor e enquanto a polícia ainda está atrás da decisão.

A manobra somente será aceita se a distância forward até a junction/decisão necessária comportar:

- a distância da troca atual;
- uma troca adicional para cada faixa ainda necessária;
- uma margem estrutural para concluir antes da bifurcação.

A margem será derivada da própria distância de manobra; não será introduzido um quarto parâmetro arbitrário.

Se a polícia já passou da entrada, nenhuma faixa candidata produzirá rota forward válida. O resultado continuará sendo grace/no-route, sem retorno, teleporte ou aresta artificial.

## Segurança

### Antes de começar

Uma verificação conservadora do corredor destino considera:

- AI à frente na cadeia destino;
- AI ao lado da trajetória;
- AI atrás e sua velocidade relativa;
- jogadores dentro do corredor da manobra;
- comprimentos dos veículos e safety distances existentes.

Se o espaço não for seguro, o estado permanece `WaitingForGap` e a polícia continua longitudinalmente na faixa atual.

### Durante a manobra

- O lookahead longitudinal da origem continua ativo.
- Um lookahead equivalente na cadeia destino limita a velocidade pelo menor valor seguro.
- Jogadores no corredor continuam participando da frenagem.
- Se surgir risco após o commit, a polícia freia; ela não inverte automaticamente a trajetória lateral.
- Collision handling existente permanece como última proteção, não como mecanismo de decisão.

Se a oportunidade não surgir antes da bifurcação, a troca expira e o no-route existente permanece correto.

## Histerese e revisão de rota

- Uma manobra ativa precisa terminar antes de outra começar.
- O cooldown impede troca imediata de volta.
- Uma decisão em espera é substituída somente quando a revisão de rota muda o destino necessário.
- Mudança de faixa do jogador sem alteração de reachability não gera solicitação.
- Mudanças múltiplas são sempre sequenciais.

## Configuração mínima

Adicionar ao `PoliceChaseConfiguration`:

- `PursuitLaneChangeEnabled`, padrão `true`;
- `PursuitLaneChangeDistanceMeters`, distância física de uma manobra;
- `PursuitLaneChangeCooldownMilliseconds`, intervalo mínimo após conclusão.

Os valores padrão serão definidos e validados a partir do teste sintético e do pacote real. Não serão alterados parâmetros globais de AI Traffic.

Essas opções serão transportadas por `PolicePursuitTrackingOptions` até `AiPursuitTrackingOptions`. O core não conhecerá `PoliceChaseConfiguration` nem `PoliceCarSessionId`; a existência de um snapshot de pursuit é o gate da capacidade.

## Diagnóstico tipado

Adicionar um diagnóstico/evento de lane change ao resultado de tracking, com revisão monotônica e informações suficientes para o plugin deduplicar logs:

- estado/transição;
- ponto origem;
- ponto destino;
- direção;
- motivo;
- distância até a decisão;
- revisão da rota;
- motivo de espera/cancelamento, quando aplicável.

O plugin mapeará o contrato do core sem reflection e emitirá logs somente quando a revisão/evento mudar:

- required/requested;
- waiting for gap;
- started;
- completed;
- cancelled;
- route revised.

## Testes automatizados

### Core sintético

Cobrir:

1. faixa atual alcança o alvo: nenhuma troca;
2. adjacente é necessária: solicitação antecipada;
3. duas faixas: duas decisões consecutivas, nunca salto;
4. destino bloqueado: espera;
5. bloqueio removido: início permitido;
6. revisão antes do início: cancelamento/substituição segura;
7. revisão durante o commit: manobra termina sem ping-pong;
8. conclusão: identidade física muda somente no final;
9. continuidade de posição, tangente e velocidade nas extremidades;
10. saída já perdida: no-route sem reverse/teleport;
11. jogador troca de faixa, mas reachability não muda: nenhuma troca;
12. tráfego sem pursuit: comportamento existente inalterado.

### Integração

- Preservar os 75 testes existentes.
- Adicionar regressões do mapper e dos logs do plugin.
- Carregar o `fast_lane.aip` real por `POLICE_CHASE_FAST_LANE_AIP` sem versionar o arquivo.
- Identificar uma região real onde uma cadeia adjacente alcança uma junction indisponível na cadeia atual.
- Provar a seleção da faixa e a continuidade da rota no grafo real.
- Se o runner não simular todo o loop temporal do servidor, separar decisão, trajetória e integração de grafo, mantendo validação visual obrigatória.

## Validação real

A entrega automatizada ficará marcada como “pronta para validação”, não concluída. No servidor será necessário comprovar:

1. perseguição básica sem regressão;
2. mudança simples contínua e sem snap;
3. entrada na subida/ponte que motivou a P0.7;
4. uma segunda bifurcação real;
5. espera diante de faixa bloqueada;
6. no-route correto quando a saída já foi perdida;
7. dez bots normais e reserva dedicada preservados;
8. ausência de loop de reacquisition e de spam de logs.

## Publicação

O core será desenvolvido em nova branch `codex/p0.7-route-aware-lane-changing` do fork `ArthurMCarlos/AssettoServer`. O repositório principal atualizará o submodule somente após testes e build. Commits serão pequenos e separados entre contratos/decisão, trajetória/segurança, integração, testes e documentação.
