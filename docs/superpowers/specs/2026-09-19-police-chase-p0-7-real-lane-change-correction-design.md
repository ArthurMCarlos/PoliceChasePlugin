# Police Chase P0.7 — Correção real do route-aware lane changing

## Objetivo

Corrigir a P0.7 para que a viatura prepare e execute uma troca física de faixa antes de uma junction necessária à rota do alvo. A correção deve explicar por que o servidor real não produziu nenhuma solicitação de mudança, tornar cada etapa observável sem spam e preservar integralmente P0.4, P0.5 e P0.6.

Esta entrega continua sendo P0.7. Não inclui perseguição ofensiva, cópia de mudanças aleatórias do jogador nem funcionalidades planejadas para P0.8.

## Base e evidências

- PoliceChasePlugin `main`: `1d9f33577d8d843bdb532f2897c6017f8a57f09d`.
- AssettoServer fixado: `539fcbe65c45f237d51fed45400121d8aae33cea`.
- Versão observada no servidor: `0.0.54+539fcbe65c`.
- P0.4 mantém uma reserva dedicada para `PoliceCarSessionId=12` e dez carros de tráfego.
- P0.5 mantém pursuit, velocidade individual e retenção espacial.
- P0.6 mantém localização por candidatos espaciais e equivalentes de faixa, rota forward-only, grace, no-route e probe.
- Os 90 testes do plugin e os testes anteriores do core passavam, mas o servidor não registrou request, start ou completion de lane change.

## Investigação comprovada

### Não existe lane changing nativo para reutilizar

No AssettoServer fixado, os bots normais não possuem um controlador de troca de faixa em movimento. A pesquisa no código anterior à P0.7 mostra que `LeftId`, `RightId` e `GetLanes()` são usados para detectar faixas, escolher a faixa de spawn, aplicar regras de faixa e localizar o alvo. O movimento normal de `AiState` continua por `NextId` e pelas `SplineJunction` selecionadas pelo `JunctionEvaluator`.

Portanto, o comportamento visto em servidores como No Hesi/2REAL não demonstra uma API de lane change disponível neste core. Mudanças visuais dos bots desta instalação podem ser resultado das junctions configuradas, da geometria do pacote ou de controladores externos. A P0.7 não deixou de chamar uma API nativa oculta: a transição física própria implementada em `AiState` é a capacidade real existente neste fork.

### Ponto exato em que o pipeline para

O pipeline atual para antes da seleção de faixa:

1. `AiPursuitTargetLocator` encontra pontos espacialmente próximos do jogador.
2. Para cada semente aceita, o locator adiciona os equivalentes de `AiSpline.GetLanes()` ao conjunto de destinos.
3. `AiPursuitNavigator` procura uma rota para qualquer destino desse conjunto.
4. Em `AiState.TrackPursuit()`, `TryPrepareLaneChange(...)` só é executado quando o resultado do navigator não está `Active`.
5. Uma faixa paralela equivalente frequentemente permanece alcançável pela cadeia atual. O navigator então retorna `Active`, mesmo quando o ponto físico mais próximo do jogador está em outra faixa e exige preparação para uma junction futura.
6. Como a navegação fallback ainda está ativa, o selector P0.7 não é chamado, a safety check não é chamada e nenhuma request chega ao controlador físico.
7. Quando o fallback finalmente vira `DistanceLimit` ou `NoRoute`, a entrada pode já estar próxima demais ou ter sido ultrapassada.

O problema principal não está no blend lateral. A decisão de iniciar o lane change foi mascarada pela equivalência de faixas necessária à P0.6.

### Reprodução com o pacote real

Um teste diagnóstico com o `fast_lane.aip` real encontrou o seguinte cenário:

- ponto policial: `312936`;
- destino lógico: `175784`;
- ponto físico preferencial do alvo: `311797`;
- junction necessária: `2`;
- destino equivalente usado pelo fallback: `313071`;
- quantidade de candidatos do navigator: `18`.

Com todos os equivalentes da P0.6, o selector decide `Stay`, porque a cadeia atual alcança `313071`. Com apenas o ponto físico preferencial `311797`, a faixa atual não oferece a rota necessária e a faixa adjacente oferece uma rota forward que passa pela junction `2`; nesse caso, o selector decide `Change`.

Isso reproduz a discrepância entre os testes anteriores e o servidor: os testes antigos chamavam o selector isoladamente com o ponto físico exato, sem atravessar `TargetLocator -> Navigator -> AiState`.

### Região 171751/171772/171780

O pacote real mostra:

- `171751` possui equivalentes `171751`, `58405` e `228170`;
- `171772` possui equivalentes `280965`, `283893`, `171772`, `58425` e `228190`;
- uma rota de `171751` até equivalentes de `171780` mede aproximadamente `49,23 m` sem junction;
- uma rota de `171772` até o mesmo grupo mede aproximadamente `13,59 m` sem junction;
- as próximas junctions forward observadas a partir de `171751` ficam a quilômetros de distância.

Logo, `171780` no log era o alvo anterior retido, não o destino que produziu o `DistanceLimit` de aproximadamente 20 km. Esses três IDs não bastam para reconstruir a falha final. A correção deve registrar os destinos atuais e os resultados das rotas atual e adjacentes para que o próximo log identifique a destination exata.

## Decisão arquitetural

A localização passará a distinguir duas noções que hoje estão misturadas:

- **âncora física preferencial**: o ponto espacial válido mais próximo da pose do jogador, antes da expansão por equivalentes de faixa;
- **destinos de navegação fallback**: o conjunto expandido usado pela P0.6 para manter localização e pursuit quando pequenas diferenças de faixa não devem causar no-route.

O navigator continuará usando todos os destinos fallback. Em paralelo, o lane-change selector avaliará proativamente se a cadeia física atual alcança a âncora preferencial e se uma faixa imediatamente adjacente consegue alcançá-la por uma junction real.

A transição lateral continua sendo uma manobra física separada. `LeftId` e `RightId` não serão inseridos como arestas do `AiRouteGraph`.

## Âncora física e estabilidade

O locator ordenará as sementes espaciais pela distância à pose do jogador e aplicará os filtros atuais de direção, alinhamento e validade. A primeira semente aceita será a âncora física preferencial.

Para evitar alternância quando o jogador está sobre uma divisória:

1. a âncora anterior permanece preferida enquanto continuar aceita e estiver dentro da tolerância de distância da melhor semente;
2. fora dessa tolerância, vence a menor distância;
3. empate residual é resolvido de forma determinística pelo ID do ponto.

A tolerância será uma constante interna baseada na resolução espacial do locator, não um novo parâmetro de servidor nesta fase.

O resultado do locator carregará separadamente:

- `PreferredPhysicalTargetPointId`;
- `RouteCandidatePointIds`.

Uma localização sem âncora física válida não solicita lane change, mas pode continuar usando o comportamento de grace/no-route existente.

## Avaliação proativa

`AiState.TrackPursuit()` executará a avaliação route-aware mesmo quando o navigator fallback estiver `Active`. A ordem será:

1. localizar âncora física e candidatos fallback;
2. calcular ou atualizar a navegação P0.6 normal;
3. avaliar a necessidade de faixa contra a âncora física;
4. validar janela temporal e safety;
5. emitir ou manter a request física;
6. continuar aplicando as decisões de junction reais ao `JunctionEvaluator`.

A avaliação solicita uma mudança somente quando todas as condições forem verdadeiras:

1. lane changing está habilitado;
2. não existe uma transição física incompatível já em andamento;
3. a faixa atual não alcança adequadamente a âncora física pela busca forward-only;
4. uma faixa `LeftId` ou `RightId` imediatamente adjacente, de mesmo sentido e geometria plausível, alcança a âncora;
5. o plano da adjacente contém uma `SplineJunction` real que explica a necessidade de posicionamento;
6. a primeira decisão está à frente, ainda não foi ultrapassada e possui distância mínima para completar a manobra;
7. a primeira decisão está dentro do lookahead de preparação;
8. cooldown e histerese permitem a mudança;
9. a faixa destino está segura.

A faixa atual ganha em qualquer empate. Entre duas adjacentes válidas, vence a rota forward de menor custo; empate residual não produz mudança. Cada manobra atravessa exatamente uma adjacência.

Se a âncora mudar enquanto a viatura aguarda, a request pendente é invalidada e reavaliada. Se a mudança física já começou, ela termina de forma contínua antes de uma nova decisão.

## Lookahead de preparação

Será adicionado `PursuitLaneChangeLookaheadMeters`:

- padrão: `1000 m`;
- unidade: metros ao longo da rota candidata;
- intervalo aceito: `100 m` a `5000 m`;
- deve ser maior ou igual a `PursuitLaneChangeDistanceMeters`;
- finalidade: permitir preparação antecipada sem avaliar indefinidamente junctions remotas.

`PursuitLaneChangeDistanceMeters` continua descrevendo a extensão longitudinal da transição física, com padrão e semântica atuais. `PursuitLaneChangeCooldownMilliseconds` continua controlando a histerese após completion/cancelamento.

Configuração inválida falhará durante a validação do plugin com mensagem específica. Campo ausente no `extra_cfg.yml` usará o padrão e não desabilitará silenciosamente a funcionalidade.

## Controlador físico e safety

O `AiLaneChangeController` existente permanece responsável por request, espera, início, blend físico, conclusão e cancelamento. A correção não criará um segundo controlador.

Antes de iniciar, a safety check existente continuará examinando AI e jogadores à frente, ao lado e atrás na faixa destino, incluindo velocidade relativa. Nenhuma condição será forçada para `safe` apenas para produzir movimento.

Durante a transição:

- os cursores de origem e destino avançam longitudinalmente;
- a pose usa interpolação suave entre as curvas;
- o ID lógico da faixa destino só é assumido na conclusão;
- o lookahead de obstáculos considera o corredor mais restritivo;
- risco após o commit reduz ou interrompe a velocidade, sem teleport ou snap;
- completion força recálculo de rota a partir da nova cadeia física.

## Diagnóstico tipado e sem spam

O core publicará um diagnóstico por camada, em vez de condensar tudo em um boolean:

1. localização da âncora física;
2. rota da faixa atual até a âncora;
3. avaliação das adjacentes;
4. seleção/rejeição da faixa;
5. resultado de safety;
6. estado da request;
7. estado do controlador físico;
8. completion/cancelamento.

Os motivos tipados incluirão, conforme aplicável:

- `Disabled`;
- `NoPhysicalTarget`;
- `CurrentLaneValid`;
- `NoAdjacentLane`;
- `OppositeDirection`;
- `NoForwardRoute`;
- `NoRealJunction`;
- `BeyondLookahead`;
- `InsufficientPreparationDistance`;
- `Cooldown`;
- `ObstacleAhead`;
- `ObstacleAlongside`;
- `ObstacleBehind`;
- `RouteRevisionChanged`;
- `Requested`;
- `Started`;
- `Completed`;
- `Cancelled`.

Quando houver uma candidata, o diagnóstico carregará:

- ponto policial e faixa atual;
- âncora física e candidatos fallback;
- revisão da rota;
- falha/distância/junction count da rota atual;
- lado, ponto e falha/distância/junction count de cada adjacente avaliada;
- junction relevante e distância até ela;
- resultado da safety;
- fase do controlador.

O core atribuirá uma assinatura semântica ao diagnóstico. O plugin manterá a última assinatura por pursuit e registrará somente quando mudar motivo, âncora, candidata, junction ou fase. Distâncias contínuas não mudarão a assinatura a cada update; eventos `Requested`, `Started`, `Completed` e `Cancelled` sempre serão registrados uma vez.

No startup, o plugin registrará uma linha resumida:

- habilitado: lookahead, distância de transição e cooldown efetivos;
- desabilitado: motivo explícito.

## Compatibilidade com P0.4–P0.6

Permanecem inalterados:

- cálculo global de AI, overbooking e a reserva única do CAR_12;
- dez carros normais de tráfego;
- CAR_10 e CAR_11 como slots de jogadores;
- spawn e lifecycle dedicado da polícia;
- controle individual de velocidade e retenção espacial;
- limites de busca de `20000 m` e de visited nodes;
- expansão de equivalentes da P0.6;
- grafo forward-only formado apenas por `NextId` e `SplineJunction`;
- grace, no-route, probe e recuperação;
- ausência de reverse, teleport, respawn e snap.

Nenhum parâmetro global de tráfego será alterado.

## Testes orientados à falha real

Os novos testes atravessarão o pipeline real, não apenas o selector isolado.

### Casos funcionais

- **A — faixa atual válida:** âncora alcançável pela cadeia atual produz `CurrentLaneValid` e nenhuma request.
- **B — preparação antecipada:** fallback equivalente está ativo, mas somente uma adjacente alcança a âncora por junction real; produz request antes de perder a saída.
- **C — obstáculo:** candidata correta, mas safety bloqueada; mantém espera com motivo explícito.
- **D — liberação:** remoção do obstáculo em nova avaliação produz request.
- **E — cooldown/histerese:** não alterna esquerda/direita entre revisões equivalentes.
- **F — pós-completion:** planner reconhece a nova cadeia e não solicita retorno imediato.
- **G — ramp/junction:** lane change é decidido enquanto a junction ainda está entre distância mínima e lookahead.
- **H — nenhuma alternativa:** adjacentes sem rota não geram manobra inútil.
- **I — revisão:** request ainda não iniciada é invalidada; mudança em andamento conclui antes do recálculo.
- **J — P0.6:** equivalentes continuam impedindo falso no-route na navegação normal.

### Regressão real do SRP

O teste com `fast_lane.aip` deverá fixar o cenário `312936 -> 311797`, demonstrando que:

1. o navigator fallback encontra `313071` e permanece ativo;
2. a âncora física continua sendo `311797`;
3. a avaliação proativa não é suprimida pelo fallback ativo;
4. a adjacente possui rota pela junction `2`;
5. uma request é emitida antes da junction;
6. a conclusão coloca a viatura na cadeia que alcança `311797`.

O diagnóstico da região `171751/171772/171780` continuará como teste de topologia/documentação, sem afirmar que `171780` foi o destino do `DistanceLimit` real.

### Regressões gerais

- todos os testes atuais continuam aprovados;
- tráfego sem pursuit não cria estado de lane change;
- nenhuma aresta lateral entra no route graph;
- P0.4, P0.5 e P0.6 mantêm seus contratos;
- build Release do core e do plugin termina sem erros novos.

## Validação no servidor real

A entrega será candidata à validação, não declarada funcional apenas pelos testes locais.

O próximo teste deve confirmar:

1. startup informa que route-aware lane changing está habilitado e mostra valores efetivos;
2. em trecho comum sem necessidade de rota, o log explica `CurrentLaneValid` sem mandar copiar aleatoriamente o jogador;
3. antes da saída/rampa, o log mostra âncora, rota atual, adjacente, junction e distância;
4. aparecem request, start e completion exatamente uma vez por transição;
5. a Skoda se move lateralmente sem desaparecer, reaparecer, teleportar ou inverter;
6. a pursuit continua na rampa após o recálculo;
7. obstáculo produz espera tipada e a remoção do obstáculo permite nova tentativa;
8. dez bots, clima e demais plugins continuam funcionando.

Se não houver mudança, o log deve identificar a camada e o motivo exatos. A P0.7 só será considerada concluída depois da confirmação visual no servidor.

## Fora de escopo

- mudança oportunista de faixa e ultrapassagem ofensiva;
- copiar toda troca do jogador;
- PIT, ram, roadblock, reforços ou múltiplas viaturas;
- Heat/Wanted, HUD, sirene, luzes, multas ou prisão;
- direção contrária ou reverse routing;
- aumento de budgets ou parâmetros globais de tráfego;
- edição do `fast_lane.aip` para fabricar conectividade;
- P0.8.

## Publicação e estado Git

O core será corrigido na branch `codex/p0.7-real-lane-change-fix`, baseada em `539fcbe65c45f237d51fed45400121d8aae33cea`. Depois dos testes, o commit exato do core atualizará o submodule no `main` do PoliceChasePlugin.

Commits e pushes serão separados de forma a manter o fork e o repositório principal reproduzíveis. A entrega final deverá informar hashes, branch, `git status`, `git submodule status`, comandos de atualização/compilação e artefatos para inspeção. Instruções destrutivas de instalação no servidor permanecerão bloqueadas até a validação dos artefatos compilados.
