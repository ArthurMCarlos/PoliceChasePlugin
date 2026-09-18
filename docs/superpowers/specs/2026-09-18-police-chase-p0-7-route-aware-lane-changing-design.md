# Police Chase P0.7 — Route-Aware Lane Changing

## Objetivo

Adicionar uma transição física de faixa, exclusiva da viatura em perseguição, para que ela alcance junctions, rampas, pontes e saídas que exigem outra faixa. A mudança deve ocorrer sobre uma trajetória contínua, preservar o roteamento forward-only da P0.6 e não alterar o comportamento dos dez carros normais de tráfego.

A P0.7 termina pronta para validação no servidor real. Ela não será declarada funcional antes da Skoda executar visualmente a manobra na Shutoko.

## Base validada

- PoliceChasePlugin `main`: `168a63c9fdbd4e282fecb3d46a5fa56c5033f927`.
- Fork AssettoServer: `793abe4810f5860b3f9b63f93dc9bce7240ba57b`.
- AssettoServer exibido no servidor: `0.0.54+793abe4810`.
- P0.4: uma instância dedicada no CAR_12 sem reduzir os dez bots dinâmicos.
- P0.5: perseguição, controle individual de velocidade e retenção espacial.
- P0.6: roteamento forward-only, decisões explícitas de junction, corridor stateful, grace, no-route, probe e equivalência de faixas na localização.
- Testes atuais: 75 aprovados, nenhuma falha.

## Investigação do mecanismo existente

### O tráfego nativo não troca de faixa

No core fixado não existe estado de `current lane`, `target lane`, `lane offset`, `lane transition`, `desired lane` ou `overtake lane` durante a condução.

O fluxo real é:

1. `AiBehavior.GetSpawnPoint()` usa `AiSpline.RandomLane()` para escolher aleatoriamente um ponto entre os equivalentes retornados por `GetLanes()`.
2. `AiState.Teleport()` inicializa o estado exatamente nessa cadeia de pontos.
3. `AiState.Move()` avança somente por `JunctionEvaluator.TryNext()`.
4. `AiState.Update()` avalia Catmull–Rom entre `CurrentSplinePointId` e o próximo ponto e publica posição, rotação e velocidade.
5. O carro permanece nessa cadeia até chegar a uma `SplineJunction`, ao fim da spline ou ser reposicionado pelo lifecycle espacial.

Logo, os bots observados em faixas diferentes nasceram nelas. Uma transição visual em uma bifurcação é o caminho configurado por uma `SplineJunction`, não uma mudança de faixa genérica.

Sistemas como 2REAL e No Hesi utilizam outro controlador de tráfego. Esse comportamento não está disponível para reutilização no AssettoServer desta solução.

### Representação de faixas

`AdjacentLaneDetector.DetectAdjacentLanes()` procura pontos lateralmente próximos durante a criação do cache e preenche `SplinePoint.LeftId` e `SplinePoint.RightId`. `SplinePointOperations.GetLanes()` percorre essa cadeia respeitando direção. `MutableAiSpline` grava os grupos no cache e `AiSpline.GetLanes()` os expõe.

No tráfego normal, essa informação é usada para:

- escolha aleatória de faixa no spawn por `AiSpline.RandomLane()`;
- contagem de faixas e overrides de distância de segurança;
- filtragem de `AllowedLanes` no spawn;
- variação de velocidade conforme a posição da faixa.

Na P0.6, `GetLanes()` também expande destinos equivalentes do target. Esses equivalentes continuam sendo identidade topológica para localização, nunca arestas gratuitas de movimento.

### Junctions e movimento físico

`MutableAiSpline.ApplyConfiguration()` transforma `Junctions` e `ConnectEnd` do `config.yml` em `SplineJunction`. `JunctionEvaluator.Next()` escolhe `SplinePoint.NextId` ou `SplineJunction.EndPointId`. `AiState.Move()`, `CalculateTangents()` e `SplineLookahead()` usam o mesmo evaluator.

A P0.6 publica decisões explícitas no `JunctionEvaluator`, permitindo à polícia escolher uma conexão real. Isso funciona somente quando a cadeia física atual alcança o ponto inicial da junction.

### Obstáculos

`AiState.DetectObstacles()` combina:

- `SplineLookahead()`, que consulta `SlowestAiStates` ao longo do caminho decidido;
- `FindClosestPlayerObstacle()`, que procura jogadores à frente;
- frenagem, parada e collision stop já existentes.

Esse mecanismo protege a faixa atual, mas não procura veículo ao lado ou atrás em uma faixa destino porque o core nunca precisou decidir uma mudança lateral. A P0.7 deve complementar a verificação apenas para a manobra policial e continuar aplicando os limites nativos de velocidade e colisão.

## Causa raiz confirmada

A P0.6 consegue localizar o jogador por pontos espacialmente próximos e equivalentes e consegue dirigir junctions que já sejam alcançáveis pela cadeia atual. Entretanto, o `AiState` não possui mecanismo para sair dessa cadeia e ocupar fisicamente uma faixa adjacente.

Quando o alvo confirma um ramo acessível apenas por outra faixa:

1. a faixa policial atual deixa de oferecer rota forward para o corredor do alvo;
2. um ponto equivalente pode identificar corretamente a região, mas não move o carro lateralmente;
3. `AiRoutePlanner` não deve transformar `LeftId`/`RightId` em arestas de rota;
4. sem uma transição física, a polícia passa da entrada;
5. depois disso, `RouteTemporarilyUnavailable` e `NoRoute` são resultados corretos.

## Decisão arquitetural

Será criada uma transição física própria no core, exclusiva de um `AiState` com pursuit ativa. A solução terá duas responsabilidades separadas:

- seleção route-aware da faixa adjacente necessária;
- execução stateful de uma trajetória lateral contínua.

O plugin continuará sendo o orquestrador da perseguição e dos logs. O core continuará sendo o proprietário do grafo, spline, movimento, obstáculos e estado físico.

## Seleção route-aware

Um componente puro de seleção receberá o ponto policial, os candidatos do target, a rota/corridor atual, os limites de busca e a revisão da rota.

Regras:

1. A faixa atual sempre tem preferência quando ainda alcança adequadamente o corredor confirmado do alvo.
2. Se a faixa atual não alcançar esse corredor, avaliar somente `LeftId` e `RightId` imediatos e de mesma direção.
3. Executar o mesmo planejamento forward-only a partir de cada candidato adjacente, sem inserir a transição lateral no `AiRouteGraph`.
4. Aceitar uma faixa somente se ela alcançar o corredor por `NextId` e `SplineJunction` reais.
5. Identificar a primeira decisão relevante e sua distância ao longo do plano.
6. Solicitar a manobra somente quando existir distância suficiente para concluí-la antes da decisão.
7. Em empate, preferir nenhuma mudança; depois, uma única faixa adjacente; por fim, a rota forward de menor custo.

O sistema não tenta adivinhar a intenção futura do jogador. A preparação começa quando a posição e o movimento do target confirmam o novo corredor, desde que a polícia ainda não tenha passado da entrada.

Se a saída já foi perdida, nenhuma faixa é aceita como correção artificial e o fluxo de no-route permanece.

## Transição física

O AssettoServer representa sua AI de modo cinemático: `AiState.Update()` calcula e publica diretamente a pose sobre a spline. Não existe comando nativo de volante para reutilizar.

O controlador de lane change manterá:

- ponto/progresso da cadeia de origem;
- ponto/progresso da cadeia de destino;
- distância total da manobra;
- distância já percorrida;
- revisão de rota que originou a solicitação;
- instante da última conclusão/cancelamento;
- fase da manobra.

As fases serão equivalentes a:

- `None`: sem necessidade;
- `WaitingForGap`: faixa necessária, mas ainda insegura;
- `Changing`: manobra comprometida;
- `Completed`: destino fisicamente alcançado;
- `Cancelled`: solicitação ainda não iniciada tornou-se inválida.

Durante `Changing`, os cursores de origem e destino avançam pela mesma distância longitudinal. A pose é obtida por uma interpolação suave entre as duas curvas, com velocidade lateral nula nos extremos. Rotação e velocidade derivam da tangente da trajetória combinada.

Enquanto a interpolação não termina, a cadeia lógica atual não é substituída antecipadamente. Ao atingir fisicamente o destino, `CurrentSplinePointId` e o progresso passam para o cursor de destino, as tangentes são recalculadas e o navigator continua a partir da nova posição.

Uma manobra atravessa exatamente uma adjacência. `RIGHT -> LEFT` exige duas conclusões físicas quando existe `MIDDLE` entre elas.

## Segurança da manobra

Antes do início, uma verificação dedicada e tipada examinará o corredor da faixa destino:

- AI à frente;
- AI ao lado;
- AI atrás;
- jogadores nessas regiões;
- distância disponível;
- velocidade relativa de aproximação traseira;
- largura, alinhamento, sentido e diferença vertical plausíveis entre os pontos adjacentes.

Serão reutilizados `EntryCarManager`, os `AiState` inicializados, `SlowestAiStates`, posições e velocidades existentes. Não haverá reflection.

Se bloqueada antes do início, a viatura permanece na faixa atual e aguarda. Durante a manobra, o lookahead considera origem e destino e aplica o limite de velocidade mais restritivo. Um risco detectado reduz ou zera a velocidade usando a frenagem atual; não causa snap nem inversão lateral.

Depois do commit, a manobra termina antes que outra possa começar. Uma revisão de rota pode cancelar apenas uma solicitação ainda em espera. Uma manobra já iniciada termina de forma estável; a nova rota é considerada depois da conclusão.

Se não surgir uma janela segura antes da junction, a entrada pode ser perdida legitimamente.

## Histerese e mudanças sequenciais

- Uma única solicitação ativa por `AiState`.
- Identidade baseada em origem, destino e revisão da rota.
- Solicitações idênticas não são republicadas a cada tick.
- Cooldown após conclusão ou cancelamento.
- Uma nova faixa somente após conclusão física da anterior.
- A faixa atual volta a ter preferência após cada conclusão.
- Mudanças do jogador que não alterem reachability não geram solicitação.

## Integração com P0.5 e P0.6

`AiState.TrackPursuit()` continuará sendo a entrada pública. As opções serão estendidas com a política mínima de lane change.

O snapshot de pursuit armazenará a decisão/estado necessário. O `JunctionEvaluator` continuará recebendo apenas decisões de junction reais. Ao concluir a troca, o corridor será recalculado a partir do ponto físico de destino.

Permanecem intactos:

- retenção espacial da polícia;
- velocidade desejada e limites de segurança;
- lifecycle e reserva dedicada;
- grace period;
- suspensão por no-route;
- probes de recuperação;
- target localization por lane equivalents;
- grafo exclusivamente formado por `NextId` e `SplineJunction`.

## Contrato e diagnóstico

O resultado de tracking carregará diagnóstico tipado da transição de faixa. O contrato representará somente mudanças de estado, contendo conforme aplicável:

- evento/fase;
- faixa/ponto de origem;
- faixa/ponto de destino;
- motivo da seleção ou cancelamento;
- distância até a decisão;
- revisão da rota;
- indicação de bloqueio.

O adapter do plugin traduzirá esse contrato para seus próprios tipos. `PolicePursuitService` registrará somente transições:

- lane change required;
- waiting for safe gap;
- started;
- completed;
- cancelled;
- route revised.

Não haverá log por frame.

## Configuração mínima

Serão expostos no plugin:

- `PursuitLaneChangeEnabled`: habilita a capacidade, padrão `true`.
- `PursuitLaneChangeDistanceMeters`: distância longitudinal usada para completar uma adjacência.
- `PursuitLaneChangeCooldownMilliseconds`: intervalo mínimo após uma manobra.

A distância de preparação será derivada da distância de manobra multiplicada pela quantidade de adjacências necessárias, acrescida da distância até a primeira decisão real. Não será criado parâmetro separado enquanto os testes não demonstrarem necessidade.

Valores finais e limites de validação serão escolhidos a partir do fast_lane real e registrados no plano de implementação.

## Testes

### Testes puros

- faixa atual alcança o target: não trocar;
- esquerda ou direita é a única adjacente alcançável: solicitar;
- direção oposta ou geometria inválida: rejeitar;
- saída já ultrapassada: não criar rota reversa;
- revisão enquanto espera: cancelar ou substituir com segurança;
- revisão durante manobra: concluir, depois recalcular;
- cooldown e deduplicação impedem ping-pong;
- duas faixas: duas solicitações sequenciais, nunca salto direto.

### Segurança e trajetória

- destino livre inicia a mudança;
- veículo à frente, ao lado ou atrás bloqueia o início;
- velocidade relativa traseira torna um gap inseguro;
- remoção do obstáculo libera a manobra;
- posição e tangente são contínuas no início e no fim;
- ID de destino somente é assumido ao completar;
- lookahead mais restritivo limita a velocidade durante a transição.

### Regressão

- todos os 75 testes atuais continuam passando;
- tráfego sem pursuit não cria controlador de lane change ativo;
- dez bots dinâmicos e slot dedicado permanecem inalterados;
- junctions explícitas, grace, no-route e probe continuam funcionando;
- nenhuma aresta lateral aparece no `AiRouteGraph`.

### Fast lane real

O pacote `C:\Users\arthur.carlos\Downloads\fast_lane.aip` será carregado pelo parser real. Os testes devem identificar pelo menos uma região em que:

1. a cadeia policial atual não alcance o ramo do target;
2. uma adjacente de mesma direção alcance uma junction real;
3. a seleção solicite a adjacência antes dessa junction;
4. a conclusão coloque a polícia na cadeia correta;
5. o plano subsequente atravesse a conexão real.

Se a pose completa não puder ser executada no teste de integração sem construir o servidor inteiro, serão separados teste de decisão, teste matemático da trajetória, integração de grafo real e validação visual obrigatória.

## Validação no servidor

O roteiro final deverá verificar:

1. perseguição básica sem regressão;
2. mudança simples em vias paralelas;
3. subida/ponte que motivou a P0.7;
4. uma segunda saída real;
5. target mudando de faixa sem mudar reachability;
6. faixa destino bloqueada e posterior liberação;
7. saída propositalmente perdida;
8. ausência de start/no-route loop;
9. tráfego, RandomWeatherPlugin, WeatherFX, RainFX e Overtake inalterados.

## Fora de escopo

- alteração do tráfego comum;
- lane change oportunista ou ultrapassagem ofensiva;
- PIT, ram, roadblock ou reforços;
- múltiplas viaturas;
- Heat/Wanted, HUD, sirene ou luzes;
- direção contrária;
- teleport, respawn deliberado ou reverse routing;
- integração ou dependência de No Hesi/2REAL;
- edição do fast_lane para fabricar conexões inexistentes;
- P0.8.

## Critério de pronto para validação

- testes anteriores e novos testes aprovados;
- builds Release do core e plugin sem novos erros;
- commits separados e publicados no parent e no fork;
- artefatos e arquivos de instalação documentados;
- logs de transição documentados;
- nenhuma mudança nos parâmetros globais de tráfego;
- implementação entregue como candidata à validação real, sem declarar a P0.7 concluída antes do teste visual.

## Publicação

O core será desenvolvido na branch `codex/p0.7-route-aware-lane-changing` do fork `ArthurMCarlos/AssettoServer`. O repositório principal permanecerá em `main` e atualizará o ponteiro do submodule depois da verificação completa. Os commits serão separados por investigação/documentação, decisão de faixa, trajetória e segurança, integração do plugin, testes e guia de validação.
