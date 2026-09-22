# Police Chase P0.7 — Segunda correção: alinhamento com a faixa do alvo

## Objetivo

Corrigir o bloqueio `NoRealJunction` observado no servidor real e fazer a viatura alinhar-se progressivamente com a faixa física do jogador perseguido. O alinhamento passa a ser uma intenção própria da perseguição, mesmo quando a rota forward da faixa policial ainda consegue alcançar algum destino equivalente da P0.6.

A manobra continua física, segura, contínua e limitada a uma faixa adjacente por vez. Esta entrega não implementa colisão intencional, PIT, ram, ultrapassagem ofensiva ou qualquer mudança no tráfego global.

## Base aprovada

- PoliceChasePlugin `main`: `6abe16564ae709959e46d4ab35d054e2d5045344`.
- AssettoServer fixado: `e584789afc1106afadf85acb9e5168f5960b6f12`.
- AssettoServer observado: `0.0.54+e584789afc`.
- O `CAR_12` permanece como a única AI policial dedicada.
- Os dez carros normais de tráfego, os slots de jogadores e os parâmetros globais de AI permanecem inalterados.
- A transição física já integrada ao core continua sendo executada pelo `AiLaneChangeController` e pela `AiLaneChangeTrajectory`.

## Causa comprovada no servidor real

O `AiPursuitLaneSelector.Evaluate()` encontra corretamente uma faixa candidata, e o `AiRoutePlanner` calcula uma rota válida a partir dela. Em seguida, o selector exige que o plano contenha uma decisão de `SplineJunction`. Quando não encontra uma junction, rejeita a candidata como `NoRealJunction`.

Os casos reais são transições diretas entre faixas paralelas:

| Ponto policial | Vizinha física | Alvo físico | Rota atual | Rota vizinha |
|---|---:|---:|---|---|
| `171761` | `283881` | `283943` | `DistanceLimit` | válida, aproximadamente `94,59 m`, zero junctions |
| `171763` | `283883` | `283945` | `DistanceLimit` | válida, aproximadamente `94,63 m`, zero junctions |
| `171765` | `283885` | `283947` | `DistanceLimit` | válida, aproximadamente `94,74 m`, zero junctions |

As relações `LeftId`/`RightId` são imediatas e recíprocas, a separação física é de aproximadamente 3,9 a 4,2 metros, a diferença vertical é desprezível e as tangentes seguem o mesmo sentido. A primeira junction futura da candidata fica a quilômetros de distância. Portanto, a troca necessária não é preparação para uma junction: é alinhamento direto com a faixa física do alvo.

Os testes anteriores não cobriam esses pares. Os cenários positivos injetavam ou encontravam uma junction real; o teste da região `171751` usava um alvo alcançável na própria faixa. Por isso toda a suíte passava enquanto o servidor produzia `NoRealJunction`.

## Decisão arquitetural

A perseguição passa a manter separadamente:

- **rota de navegação**: permite que a polícia continue avançando e alcance a região do alvo usando os candidatos equivalentes da P0.6;
- **faixa física desejada**: representa a faixa ocupada pelo alvo e orienta o alinhamento lateral da viatura;
- **manobra física ativa**: representa uma única transição já solicitada ou em execução.

Uma rota atual válida deixa de cancelar automaticamente a avaliação lateral. Enquanto houver perseguição ativa, o sistema pode solicitar alinhamento mesmo com navegação `Active`.

O objetivo lateral não será recalculado como uma ordem instantânea a cada tick. Um estado de `TargetLaneAlignment` reterá a âncora física aceita, a faixa desejada, o sentido da próxima transição e a fase da tentativa. Isso evita alternância quando o jogador passa sobre a linha divisória ou quando dois pontos possuem distância espacial semelhante.

## Classificação das relações de faixa

Antes de selecionar uma candidata, o core classificará explicitamente a relação entre o ponto atual e o ponto candidato:

- `SameLane`;
- `ImmediateLeft`;
- `ImmediateRight`;
- `NonAdjacent`;
- `OppositeDirection`;
- `InvalidGeometry`.

Uma relação lateral válida exige:

1. referência imediata por `LeftId` ou `RightId`;
2. referência recíproca no ponto candidato;
3. tangentes no mesmo sentido;
4. distância lateral e diferença vertical compatíveis com a relação já detectada pelo `AdjacentLaneDetector`;
5. pontos e splines válidos na revisão atual.

O route graph continuará contendo somente avanço por `NextId` e decisões reais de `SplineJunction`. Nenhuma aresta lateral será criada.

## Motivações aceitas

O selector distinguirá duas motivações:

### `TargetLaneAlignment`

Usada para aproximar a viatura da faixa física do alvo. Pode ser selecionada mesmo quando a rota atual é válida, desde que a faixa desejada seja diferente e exista uma transição adjacente segura que preserve uma rota forward útil.

Quando o alvo está na vizinha imediata, a candidata é essa própria vizinha. Quando está a duas ou mais faixas, a candidata é apenas o primeiro passo adjacente na direção do alvo. Uma nova troca só pode ser avaliada depois da conclusão e do cooldown da anterior.

### `FutureJunction`

Mantém o comportamento já existente quando uma faixa adjacente é necessária para preparar uma junction real da rota. Essa motivação funciona como proteção de alcançabilidade: em uma bifurcação próxima, preservar uma rota que alcance o alvo tem prioridade sobre copiar imediatamente sua posição lateral.

`NoRealJunction` continua sendo um diagnóstico válido apenas para uma decisão que alegue preparação de junction. A ausência de junction não rejeita uma candidata cuja motivação seja `TargetLaneAlignment`.

## Ordem de decisão

Cada avaliação seguirá esta precedência:

1. confirmar pursuit ativa e âncora física válida;
2. manter uma manobra já iniciada até conclusão ou cancelamento seguro;
3. impedir qualquer troca que deixe a viatura sem rota forward útil para o alvo;
4. respeitar uma preparação obrigatória de junction quando adiar a decisão faria a polícia perder a ramificação;
5. determinar a faixa física desejada do jogador com histerese;
6. se já alinhado, permanecer;
7. escolher exatamente uma vizinha imediata que reduza a distância ordinal entre as faixas;
8. validar relação física, rota, janela de preparação, cooldown e safety;
9. solicitar a transição pelo controlador existente;
10. ao concluir, adotar a cadeia destino, recalcular a rota e reavaliar se outro passo lateral ainda é necessário.

Quando duas opções forem igualmente seguras e úteis, a faixa atual vence. Uma troca nunca será iniciada apenas porque o jogador oscilou momentaneamente entre duas âncoras equivalentes.

## Histerese e estabilidade

O alinhamento usará a âncora física preferencial separada dos destinos equivalentes de navegação. A âncora anterior continuará válida enquanto:

- ainda passar pelos filtros espaciais e de direção;
- sua distância estiver dentro da tolerância da melhor nova semente;
- não houver evidência estável de que o jogador adotou outra faixa.

A mudança de faixa desejada exigirá confirmação por avaliações consecutivas ou tempo mínimo estável. Os valores serão constantes internas cobertas por testes nesta correção, sem adicionar configuração de servidor desnecessária.

Após completion ou cancelamento, o cooldown existente impede uma troca inversa imediata. Se o alvo mudar novamente durante uma manobra já comprometida, a viatura conclui o movimento contínuo atual e então recalcula o próximo passo.

## Janela e rota da candidata

Para `FutureJunction`, a distância de decisão permanece a distância até a primeira junction relevante.

Para `TargetLaneAlignment`, a distância forward da rota candidata até a âncora física do alvo mede alcançabilidade e prioridade, não o espaço disponível para a transição. A candidata precisa:

- produzir uma rota válida;
- não exigir reverse;
- ficar dentro do lookahead configurado;
- oferecer rota válida até a âncora mesmo quando essa distância for menor que a trajetória física;
- possuir, nas cadeias físicas de origem e destino, espaço forward suficiente para a trajetória lateral;
- não piorar de forma definitiva a alcançabilidade do alvo.

Nos três casos inicialmente automatizados, aproximadamente 95 metros atendiam ao lookahead de 1.000 metros. O teste real posterior demonstrou o caso de fronteira: quando a âncora já está na faixa adjacente, a rota até ela pode ser próxima de zero, enquanto as duas splines ainda possuem os 60 metros necessários à transição. Por isso, `TargetLaneAlignment` não possui `DistanceToDecisionMeters`; esse campo permanece reservado para `FutureJunction`.

Uma rota atual válida não impede o alinhamento, mas uma candidata sem rota útil é rejeitada. Assim, a polícia busca a faixa do jogador sem sacrificar a continuidade da perseguição.

## Safety e movimento físico

O `AiLaneChangeController` existente continua sendo o único executor da transição. Não haverá segundo controlador nem teleporte.

Antes do início, a faixa destino continuará sendo verificada quanto a jogadores e AIs à frente, ao lado e atrás, incluindo velocidade relativa. Quando bloqueada, a intenção de alinhamento permanece pendente e observável, mas a viatura continua perseguindo na faixa atual até surgir uma janela segura.

Durante a mudança:

- cursores de origem e destino avançam longitudinalmente;
- a pose interpola suavemente entre as curvas;
- obstáculos são avaliados no corredor relevante;
- risco depois do commit reduz ou interrompe a velocidade sem snap lateral;
- o ID lógico da faixa destino é adotado apenas na conclusão;
- o planner recalcula a navegação a partir da nova cadeia física.

Esta fase somente posiciona a viatura na mesma faixa. Contato intencional, PIT e ram permanecem fora de escopo e deverão usar um controlador ofensivo separado no futuro.

## Diagnóstico

O diagnóstico tipado deverá mostrar, sem spam:

- ponto e faixa atuais da polícia;
- âncora física e faixa desejada do alvo;
- relação ordinal e física entre as faixas;
- próxima vizinha selecionada;
- motivação `TargetLaneAlignment` ou `FutureJunction`;
- resultado e distância das rotas atual e candidata;
- distância exigida da transição e distância física disponível nas cadeias de origem e destino;
- junction relevante, quando houver;
- estado de histerese e cooldown;
- resultado da safety;
- fases `Requested`, `WaitingForSafety`, `Started`, `Completed` e `Cancelled`;
- motivo exato de rejeição, inclusive `NonAdjacent`, `OppositeDirection`, `InvalidGeometry`, `NoForwardRoute`, `BeyondLookahead` e `InsufficientPreparationDistance`.

A assinatura semântica continuará suprimindo repetições. Alterações contínuas de distância não gerarão uma linha por tick; mudanças de faixa desejada, motivação, candidata, motivo ou fase gerarão uma nova linha.

## Testes obrigatórios

### Regressão dos três casos reais

Usando o `fast_lane.aip` real:

- `171761 -> 283881 -> 283943` seleciona alinhamento direto sem junction;
- `171763 -> 283883 -> 283945` seleciona alinhamento direto sem junction;
- `171765 -> 283885 -> 283947` seleciona alinhamento direto sem junction;
- cada candidata é confirmada como vizinha imediata, recíproca e de mesmo sentido;
- a distância da rota candidata permanece abaixo do lookahead, sem mínimo artificial;
- origem e destino possuem espaço físico suficiente para a transição configurada;
- a ausência de junction não produz `NoRealJunction` para `TargetLaneAlignment`.

### Comportamento stateful

- rota atual válida, mas alvo estabilizado na vizinha: solicita alinhamento;
- alvo na mesma faixa: permanece;
- alvo a duas ou mais faixas: executa uma transição por vez;
- alvo oscila sobre a divisória: histerese impede ping-pong;
- alvo muda durante espera por safety: intenção é atualizada sem transição inválida;
- alvo muda durante manobra comprometida: conclui e reavalia;
- completion atualiza a cadeia física e permite o próximo passo;
- cooldown impede retorno imediato.

### Prioridade de rota

- junction obrigatória próxima vence o alinhamento lateral momentâneo;
- candidata que perde a rota forward é rejeitada;
- current route e candidata válidas: alinhamento é permitido somente para reduzir distância até a faixa desejada;
- nenhuma alternativa alcançável: polícia continua na faixa atual sem encerrar prematuramente a pursuit;
- equivalentes da P0.6 continuam evitando falso `no-route`.

### Relação e segurança

- relação não recíproca é rejeitada;
- faixa não adjacente não é atravessada diretamente;
- sentido oposto e geometria incompatível são rejeitados;
- obstáculo à frente, ao lado ou atrás bloqueia o início;
- remoção do obstáculo permite nova tentativa;
- AI comum sem pursuit nunca cria estado de alinhamento.

### Regressão geral

- todos os testes atuais do core e do plugin permanecem aprovados;
- nenhum parâmetro global de tráfego é alterado;
- não há arestas laterais no route graph;
- reserva dedicada, lifecycle, velocidade, retenção espacial, grace, probe e recuperação permanecem funcionais;
- builds Release do core e do plugin terminam sem erros e sem novos warnings.

## Validação no servidor

O próximo teste real deverá comprovar:

1. o log identifica a faixa física do jogador e a motivação de alinhamento;
2. aparecem request, espera de safety quando necessária, start e completion uma vez por manobra;
3. a Skoda troca uma faixa por vez em direção ao jogador;
4. ela não oscila quando o jogador trafega sobre a divisória;
5. ela preserva a rota em bifurcações e continua a pursuit após cada completion;
6. quando bloqueada por tráfego, continua perseguindo e tenta novamente sem teleportar;
7. os dez bots de tráfego, jogadores, clima e demais plugins permanecem funcionando.

A P0.7 só será considerada validada após confirmação visual e análise desse log.

## Fora de escopo

- colisão deliberada, PIT, ram ou bloqueio do veículo perseguido;
- ultrapassagem ofensiva ou escolha de faixa baseada somente em velocidade;
- múltiplas viaturas, reforços ou roadblocks;
- Heat/Wanted, HUD, sirene, luzes, multas ou prisão;
- reverse routing, direção contrária, teleport, respawn ou snap;
- aumento de budgets, densidade, quantidade ou velocidade global da AI;
- modificação do `fast_lane.aip` para fabricar conectividade;
- importação do controlador Lua offline da 2REAL/CSP.

## Publicação

A implementação continuará na branch `codex/p0.7-real-lane-change-fix` do submodule. Após a validação automatizada, o commit exato do core será publicado no fork e o ponteiro do submodule será atualizado no `main` do PoliceChasePlugin.

Os commits deverão preservar separadamente a alteração do core e a integração/documentação do plugin. A entrega informará hashes, resultados dos testes, estado limpo dos dois repositórios e comandos de atualização do servidor.
