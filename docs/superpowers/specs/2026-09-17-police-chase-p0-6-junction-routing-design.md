# Police Chase P0.6 — navegação stateful em bifurcações

## Objetivo

A P0.6 deve manter a perseguição física do `CAR_12` através de bifurcações alcançáveis da rede de AI da Shutoko. A solução deve escolher conexões reais da spline, sem teleporte, respawn deliberado, navegação reversa ou troca ofensiva de faixa.

A fase preserva a reserva dedicada da P0.4 e o controle longitudinal e a retenção espacial da P0.5. O tráfego comum não participa do novo roteamento.

## Evidência investigada

A investigação foi feita sobre o AssettoServer fixado no submodule no commit `54326833d502d1872182ca39398affff99b0eb5b` e sobre o `fast_lane.aip` usado no servidor real.

O pacote contém o `config.yml` e 31 arquivos `fast_lane*.ai`. Depois da composição realizada por `FastLaneParser` e `MutableAiSpline`, a rede possui:

- 31 splines, incluindo a spline principal;
- 318.796 pontos;
- aproximadamente 502 km de segmentos;
- junctions configuradas entre a spline principal e Shinjuku, Mirai, Bayshore, Yokohane, C1 e outros conectores;
- conectores com aproximadamente 1,75 km, 4,96 km, 5,76 km e um circuito de 14,7 km.

### Estruturas reais do AssettoServer

- `AiSpline` expõe `Points`, `Junctions`, `KdTree`, `WorldToSpline()` e `GetLanes()`.
- `SplinePoint` possui os IDs globais `Id`, `PreviousId`, `NextId`, `LeftId`, `RightId`, `JunctionStartId` e `JunctionEndId`.
- `SplineJunction` possui `Id`, `StartPointId`, `EndPointId` e `Probability`.
- `MutableAiSpline.ApplyConfiguration()` transforma `ConnectEnd` e `Junctions` do pacote em `NextId` e `SplineJunction`.
- `JunctionEvaluator.Next()` escolhe `EndPointId` quando a branch é tomada e `NextId` quando não é tomada.
- `AiState.Move()`, `CalculateTangents()` e `SplineLookahead()` usam o mesmo `JunctionEvaluator`; portanto, decisões explícitas conseguem direcionar o movimento físico sem reposicionar o carro.
- O AssettoServer original não possuía busca de caminho. A P0.5 introduziu `AiRouteGraph` e `AiRoutePlanner`, com busca de menor custo limitada e dirigida pelas arestas `NextId` e `EndPointId`.

## Causa técnica da falha na P0.5

A P0.5 representa junctions no grafo, mas ainda não implementa navegação stateful adequada à rede real.

As limitações confirmadas são cumulativas:

1. `AiState.TrackPursuit()` usa `WorldToSpline()` e recebe somente o ponto geometricamente mais próximo do jogador. O KD-tree não considera direção, candidato anterior ou continuidade. Em vias próximas ou sobrepostas, o ponto mais próximo pode pertencer a outro ramo.
2. O planner recebe esse único ponto e seus equivalentes laterais de mesma direção. Se o ponto escolhido estiver em uma conexão não alcançável a partir do policial, a busca retorna `null`, mesmo que outro candidato próximo represente corretamente a via do jogador.
3. `PursuitMaxDistanceMeters` é usado simultaneamente como limite espacial da perseguição e limite de comprimento da busca. Com o valor 1.500 m, conectores reais maiores que 1.500 m podem ser classificados como sem rota enquanto a distância espacial entre os carros ainda está dentro do limite.
4. O grafo é deliberadamente forward-only. Depois que o policial ultrapassa a junction necessária, ele não pode voltar para selecionar o ramo. Isso é correto para o movimento físico, mas exige decisão estável antes da bifurcação.
5. A rota é recalculada a cada atualização de 200 ms e não possui corredor persistente, revisão ou regra de invalidação. A continuidade do candidato e das decisões não é modelada.
6. O primeiro resultado nulo é convertido imediatamente em `NoRoute`. Não há tolerância para a transição do alvo entre pontos de uma junction.
7. O plugin libera a perseguição no primeiro `NoRoute` e não bloqueia o mesmo par polícia-alvo. O lifecycle nativo pode criar outra instância e produzir o ciclo `Pursuit started -> no-route -> Pursuit started`.

Os logs existentes não contêm IDs de pontos ou junctions, portanto não permitem atribuir cada ocorrência real a um único gatilho. A P0.6 adicionará esses diagnósticos sem alterar a conclusão estrutural acima.

## Alternativas consideradas

### Aumentar apenas o limite e adicionar cooldown

Reduz falsos negativos causados por comprimento, mas não corrige associação ao ramo errado, decisões instáveis ou recálculo excessivo. Rejeitada como solução incompleta.

### Roteador stateful e limitado

Mantém o grafo real, adiciona seleção de candidatos, corredor em cache, orçamento independente, tolerância e suspensão verificável. É a opção escolhida por resolver as causas identificadas com alterações específicas à polícia.

### Navegador global completo

Um serviço geral de map matching e roteamento teria maior abrangência, mas excederia a P0.6 e aumentaria o risco para o tráfego. Adiado.

## Arquitetura escolhida

### Localização do alvo

Um componente de localização específico da perseguição consultará até 16 vizinhos no `AiSpline.KdTree`. Cada candidato deve:

- permanecer dentro de `MaxPlayerDistanceToAiSplineSquared`;
- ter direção compatível com a velocidade do alvo quando ela for suficiente para determinar sentido;
- favorecer o ponto ou corredor anterior quando o alvo estiver lento;
- ser avaliado pela alcançabilidade dirigida a partir do ponto atual do policial.

A seleção prioriza continuidade e alcançabilidade antes da menor distância geométrica. `LeftId` e `RightId` podem identificar equivalência de faixas, mas não serão adicionados como arestas de movimento. A P0.6 não implementa troca deliberada de faixa.

### Planejamento limitado

`AiRoutePlanner` continuará usando a rede imutável formada por `NextId` e `EndPointId`. A busca receberá dois limites independentes:

- distância máxima da rota;
- número máximo de nós visitados.

O resultado deve conter:

- distância da rota;
- sequência/corredor de pontos;
- decisões de junction;
- número de nós visitados;
- ponto inicial e candidato escolhido para o alvo.

O grafo continuará sendo construído de forma lazy. Nenhum carro de tráfego executará a busca enquanto não possuir uma perseguição.

### Estado e cache

`AiPursuitSnapshot` manterá o alvo, sua posição, o candidato selecionado, o corredor, as decisões, a revisão da rota, a velocidade desejada e o instante da primeira falha.

A rota será reutilizada enquanto:

- o policial permanecer no corredor esperado;
- o alvo continuar compatível com o corredor ou sua extensão;
- nenhuma mudança de ramo invalidar as decisões futuras;
- os limites configurados continuarem válidos.

Uma nova busca será feita quando o alvo mudar de ramo, o policial sair do corredor, o alvo avançar além da janela reutilizável, a rota expirar ou uma perda temporária precisar ser recuperada.

As decisões explícitas devem ser publicadas no `JunctionEvaluator` antes da AI alcançar o ponto de início da junction. `Move()`, tangentes, indicadores, obstáculos e limites de curva permanecem nativos.

## Estados da perseguição

O fluxo lógico será:

```text
WaitingForSpawn
  -> RouteSearch
     -> Active
     -> NoRouteSuspended

Active
  -> Active (cache ou recálculo válido)
  -> RouteTemporarilyUnavailable
  -> MaxDistanceExceeded

RouteTemporarilyUnavailable
  -> Active (recuperação dentro da tolerância)
  -> NoRoute (tolerância expirada)

NoRoute
  -> NoRouteSuspended
  -> Active somente após uma sondagem confirmar rota
```

Durante uma perda temporária, a última rota e suas decisões são preservadas. O plugin não aumenta a velocidade desejada nesse estado. Se a tolerância expirar sem rota válida, o core libera o controle de perseguição e retorna `NoRoute` definitivo.

Distância física acima de `PursuitMaxDistanceMeters` continua encerrando imediatamente por `MaxDistanceExceeded`, sem tolerância.

## Suspensão e reaquisição

Depois de um `NoRoute` definitivo, o plugin registra o par polícia-alvo como suspenso. Enquanto suspenso:

- não registra novo `Pursuit started`;
- não força spawn ou reposicionamento;
- espera uma instância policial nativa inicializada;
- executa no máximo uma sondagem por intervalo configurado;
- reativa somente quando `TrackPursuit()` confirmar uma rota válida.

Desconexão, troca de alvo, distância máxima e estado policial não inicializado continuam tendo prioridade e limpam o estado apropriado de forma idempotente.

## Configuração

Os valores aprovados são:

```yaml
PursuitMaxDistanceMeters: 1500
PursuitRouteSearchMaxDistanceMeters: 20000
PursuitRouteSearchMaxVisitedNodes: 50000
PursuitRouteGraceMilliseconds: 2000
PursuitNoRouteProbeIntervalMilliseconds: 2000
```

`PursuitMaxDistanceMeters` permanece exclusivamente espacial. Os novos valores controlam apenas navegação, tolerância e sondagem. Todos devem ter validação explícita e limites finitos/positivos.

## Contratos entre core e plugin

Os tipos de resultado da perseguição serão estendidos de forma tipada. Além de status, distância e velocidade do alvo, o plugin receberá metadados suficientes para registrar:

- revisão da rota;
- ponto atual do policial;
- ponto selecionado para o alvo;
- distância planejada;
- decisões de junction e respectivos `EndPointId`;
- motivo do recálculo quando houver uma transição relevante.

Não será usada reflection. Os IDs são os IDs globais reais de `SplinePoint` e `SplineJunction`.

## Logs

Logs de informação serão emitidos somente em transições:

- seleção inicial de rota;
- nova decisão futura de junction;
- recálculo por mudança de ramo, saída do corredor ou recuperação;
- perda temporária;
- recuperação;
- encerramento definitivo;
- sucesso de uma sondagem após suspensão.

Estatísticas repetitivas, como nós visitados em extensões normais do corredor, ficam em nível Debug. Nenhum log será gerado a cada atualização de 200 ms.

## Segurança e lifecycle

- Não haverá escrita direta em `Status.Position` para corrigir rota.
- Não haverá chamada de `Teleport()` durante perseguição.
- Não haverá despawn ou respawn deliberado para alcançar o alvo.
- O spawn inicial e o lifecycle após término permanecem nativos.
- `PlayerRadiusMeters`, `MaxSpeedKph`, `TrafficDensity`, `AiPerPlayerTargetCount`, `MaxAiTargetCount` e distâncias globais de spawn não serão alterados.
- Somente o estado dedicado do `PoliceCarSessionId` receberá rota, cache e decisões explícitas.

## Testes automatizados

### Core

Os testes comportamentais cobrirão:

1. mesma spline;
2. conexão direta;
3. bifurcação escolhendo ramo A;
4. bifurcação escolhendo ramo B;
5. rota com múltiplas conexões;
6. candidato geometricamente mais próximo incorreto e candidato alternativo alcançável;
7. continuidade em vias próximas;
8. caminho inexistente;
9. limite de distância;
10. limite de nós visitados;
11. reutilização do cache;
12. invalidação por mudança de ramo;
13. perda temporária;
14. recuperação dentro da tolerância;
15. `NoRoute` após a tolerância;
16. decisões explícitas antes da junction;
17. ausência de arestas laterais de troca de faixa;
18. construção lazy do grafo.

Uma topologia pequena equivalente aos padrões encontrados no pacote será usada nos testes. O arquivo real de 5,4 MB não será incluído no repositório.

### Plugin

Os testes cobrirão:

1. mapeamento dos novos resultados do core;
2. início com rota válida;
3. perda temporária e recuperação;
4. suspensão após `NoRoute`;
5. ausência de reaquisição imediata;
6. sondagens respeitando o intervalo;
7. reativação somente após rota confirmada;
8. logs únicos por transição;
9. limite físico;
10. desconexão e troca de alvo;
11. estado policial não inicializado;
12. release idempotente;
13. regressão da configuração e do controle longitudinal da P0.5.

Todos os testes existentes do core e do plugin devem continuar passando.

## Documentação e validação real

A implementação produzirá `docs/P0.6-navegacao-bifurcacoes.md` e corrigirá a documentação da P0.5 para registrar que o suporte a bifurcações daquela fase era experimental e não foi confirmado na Shutoko real.

O roteiro real deve validar pelo menos duas bifurcações, decisões registradas com IDs reais, continuidade física sem respawn, `NoRoute` em caminho impossível, ausência do loop de reaquisição e preservação dos dez carros de tráfego.

## Limitações mantidas

A P0.6 não implementa:

- navegação reversa;
- teleporte ou recuperação artificial;
- ultrapassagem deliberada;
- mudança ofensiva de faixa;
- PIT, roadblocks ou múltiplas viaturas;
- sirene, luzes, HUD ou Heat/Wanted;
- roteamento global para carros de tráfego.

Uma junction já ultrapassada pode ser fisicamente inalcançável no grafo dirigido. Nesse caso, a perseguição somente será retomada quando a rede voltar a oferecer uma rota forward válida.
