# Police Chase P0.5 — perseguição básica com roteamento limitado

## Objetivo

Provar uma perseguição contínua com uma única viatura na spline do AssettoServer: o jogador acelera pela rota, o `CAR_12` escolhe as ramificações necessárias, ajusta individualmente a velocidade para reduzir ou manter a distância e continua existindo enquanto a perseguição estiver válida.

A P0.5 usa a reserva dedicada entregue pela P0.4. Ela não altera as regras espaciais, a quantidade ou a velocidade dos dez carros de tráfego.

## Evidência e causa do limite atual

O `AiBehavior.Update()` calcula a menor distância entre cada `AiState` inicializado e as posições deslocadas dos jogadores. Quando a distância ultrapassa `AiParams.PlayerRadiusSquared` e termina a proteção de spawn, o estado é incluído em `_uninitializedAiStates`.

O estado não é removido da reserva nesse ponto. No mesmo ciclo, o comportamento nativo pode escolher um novo ponto seguro e chamar `AiState.Teleport()`. O `SpawnCounter` muda, e a camada de rede passa a apresentar aquele estado na nova posição. Para uma perseguição, esse reaproveitamento espacial quebra a continuidade física e é percebido como o desaparecimento do policial.

Os únicos `Despawn()` diretos relacionados são o fim da spline, a proximidade insegura entre estados do mesmo slot, a redução de overbooking e o reset do slot. Portanto, aumentar globalmente `PlayerRadiusMeters`, `MaxSpeedKph` ou parâmetros de densidade não corrige a causa e afetaria o tráfego normal.

## Escopo funcional

Durante uma perseguição válida:

- somente o `AiState` reservado do `CAR_12` recebe controle especial;
- o alvo é o jogador selecionado pelo `PoliceTargetService`;
- o policial acompanha posição, velocidade e progresso de spline do alvo;
- a rota é recalculada de forma limitada para escolher as próximas ramificações;
- o policial percorre fisicamente a spline, sem teleporte comandado pelo plugin;
- a velocidade desejada aumenta conforme a distância, até um teto individual;
- curvas, obstáculos, colisões e frenagem nativa continuam podendo reduzir a velocidade;
- o lifecycle nativo não recicla o estado enquanto houver rota válida ou uma perda momentânea de localização e a separação permanecer dentro de 1.500 metros.

A perseguição termina quando o alvo desconecta ou quando não existe caminho alcançável dentro do limite de 1.500 metros. Ao terminar, toda decisão explícita de rota, retenção espacial e velocidade individual é removida, e o estado volta ao lifecycle nativo.

## Configuração

A configuração do plugin ganha estes campos:

```yaml
PursuitMaxDistanceMeters: 1500
PursuitDesiredDistanceMeters: 50
PursuitMaxSpeedKph: 180
PursuitUpdateIntervalMilliseconds: 200
```

Regras de validação:

- `PursuitMaxDistanceMeters` deve ser maior que zero;
- `PursuitDesiredDistanceMeters` deve ser maior que zero e menor que `PursuitMaxDistanceMeters`;
- `PursuitMaxSpeedKph` deve ser maior que zero;
- `PursuitUpdateIntervalMilliseconds` deve ser maior que zero.

Esses parâmetros são exclusivos do policial. Não substituem `AiParams.MaxSpeedKph`, `PlayerRadiusMeters`, `TrafficDensity`, `AiPerPlayerTargetCount` ou `MaxAiTargetCount`.

## Arquitetura do core

### Controle opcional por estado

O core expõe uma API tipada no `AiState` para iniciar, atualizar e encerrar uma perseguição. O controle mantém:

- referência ao `EntryCar` alvo;
- última posição e ponto de spline válidos do alvo;
- rota limitada atual e decisões de bifurcação;
- distância percorrível até o alvo;
- velocidade desejada e teto individual;
- distância máxima de retenção;
- estado de rota disponível ou momentaneamente perdida.

Ausência desse controle preserva o comportamento atual byte por byte nas decisões relevantes. Não haverá reflection, acesso a membros privados pelo plugin ou identificação do policial por modelo dentro do core.

### Planejador de rota limitado

`AiRoutePlanner` executa uma busca direcionada a partir do ponto atual do policial. Cada aresta usa o comprimento do segmento da spline. Os sucessores possíveis são o `NextId` normal e, quando houver uma bifurcação, o `EndPointId` da ramificação.

A busca termina quando:

- alcança o ponto do alvo ou uma faixa/lane equivalente na mesma direção;
- o comprimento acumulado excede `PursuitMaxDistanceMeters`;
- não restam nós alcançáveis.

O resultado contém distância percorrível e decisões explícitas somente para as bifurcações do caminho encontrado. A busca não produz uma rota global, não atravessa a spline ao contrário e não teletransporta o estado.

O planejador terá um núcleo puro, independente do arquivo de spline mapeado, para permitir testes com grafos pequenos e resultados literais.

### Decisões de bifurcação

`JunctionEvaluator` aceita decisões explícitas opcionais. Quando existe uma decisão para a bifurcação atual, ela substitui o sorteio e permanece estável durante aquela passagem. Quando não existe, `WillTakeJunction()` mantém o comportamento aleatório atual.

Encerrar a perseguição limpa somente as decisões explícitas. O histórico nativo volta a operar normalmente.

### Retenção espacial

Antes de adicionar um estado distante à fila de reposicionamento, `AiBehavior.Update()` consulta o controle opcional daquele estado.

- Controle válido e separação dentro do limite: o estado não entra na fila.
- Controle ausente ou encerrado: aplica-se exatamente a regra nativa de `PlayerRadiusSquared`.
- Distância ou rota acima do limite: o controle sinaliza perda definitiva, é encerrado pelo controlador do plugin e o estado volta a ser elegível ao lifecycle normal.

A retenção é por `AiState`, não por `EntryCar` global, preservando a semântica dos demais estados e slots.

### Velocidade e segurança

O plugin calcula uma velocidade desejada a partir da velocidade do alvo e do erro de distância em relação a `PursuitDesiredDistanceMeters`. O ganho é progressivo e limitado por `PursuitMaxSpeedKph`.

O `AiState.DetectObstacles()` usa esse valor como teto individual inicial durante a perseguição. O resultado ainda passa por:

- limite de curva calculado por `SplineLookahead()`;
- distância para outros estados AI;
- detecção de jogador à frente;
- frenagem e aceleração configuradas para o modelo;
- parada por colisão.

Assim, o controle especial pode pedir aceleração acima da velocidade aleatória de tráfego do policial, mas não ignora as proteções nativas.

## Arquitetura do plugin

### `PolicePursuitService`

Um serviço periódico, iniciado pelo `PoliceChaseService`, executa a cada `PursuitUpdateIntervalMilliseconds`:

1. lê o alvo atual do `PoliceTargetService`;
2. resolve o `EntryCar` do alvo e o `AiState` reservado do `PoliceAiService`;
3. aguarda se o estado ainda não foi posicionado pelo spawn nativo;
4. inicia ou atualiza o controle de perseguição;
5. calcula a velocidade desejada usando distância percorrível e velocidade do alvo;
6. encerra o controle se o alvo desconectar ou se a rota exceder 1.500 metros.

O serviço é o único proprietário do início e término da perseguição. O adaptador AssettoServer traduz interfaces testáveis do plugin para a API tipada do core.

### Perda momentânea da spline

Se `WorldToSpline()` não localizar temporariamente o alvo numa posição utilizável, o controlador preserva a última rota válida e mantém a retenção enquanto a distância espacial continuar dentro de 1.500 metros. Ele não cria uma nova decisão de ramificação até recuperar um ponto válido.

Se o alvo voltar à spline, a rota é recalculada. Se a distância espacial ultrapassar 1.500 metros ou o alvo desconectar, a perseguição termina.

## Fluxo de estados

```text
WaitingForTarget
  -> WaitingForPoliceSpawn
  -> Pursuing
  -> RouteTemporarilyLost
  -> Pursuing                 (rota recuperada)
  -> Released                 (desconexão ou distância > 1500 m)
  -> WaitingForTarget
```

O estado `RouteTemporarilyLost` não desativa, não teletransporta e não acelera cegamente a viatura. Ele mantém a última rota e um teto seguro até haver nova telemetria utilizável.

## Concorrência e lifecycle

O loop do plugin e os loops nativos de atualização podem executar em threads diferentes. O controle compartilhado do `AiState` será publicado como um snapshot imutável e substituído atomicamente. O planejador produz uma nova rota antes da publicação; leitores nunca observam uma coleção parcialmente alterada.

O cancelamento do host encerra o loop periódico, remove o controle especial e depois encerra os serviços de alvo. Chamadas repetidas para iniciar ou encerrar a mesma perseguição serão idempotentes.

## Observabilidade

Serão registrados somente eventos de transição:

- perseguição iniciada, com sessões do policial e do alvo;
- rota temporariamente perdida;
- rota recuperada;
- perseguição encerrada, com motivo `target-disconnected` ou `max-distance-exceeded`.

O loop de 200 ms não emitirá log por atualização. Distância e velocidades poderão aparecer em `Debug` apenas quando houver transição.

## Estratégia de testes

### Core AssettoServer

- caminho direto retorna distância literal esperada;
- bifurcação escolhe o ramo que alcança o alvo;
- busca não atravessa caminho acima do limite;
- caminho inexistente retorna falha sem alterar decisões nativas;
- decisão explícita do `JunctionEvaluator` vence o sorteio somente durante o controle;
- estado perseguindo dentro do limite não entra na fila de reposicionamento;
- estado sem controle continua sujeito ao `PlayerRadiusSquared`;
- estado liberado volta a ser elegível ao lifecycle nativo;
- teto individual pode superar a velocidade inicial do policial;
- curva, obstáculo e colisão ainda reduzem a velocidade final.

### Plugin

- aguarda alvo elegível e spawn inicializado;
- inicia exatamente uma perseguição com o alvo selecionado;
- velocidade desejada cresce com o erro de distância;
- velocidade desejada nunca supera 180 km/h por padrão;
- perda momentânea preserva a última rota e não encerra o controle;
- recuperação recalcula a rota;
- desconexão encerra o controle;
- distância acima de 1.500 metros encerra o controle;
- troca ou ausência de alvo não afeta os slots de tráfego;
- parada do host remove o controle especial.

Todos os testes novos seguirão RED, GREEN e refatoração. Ao final serão executadas as suítes completas, builds Release dos dois repositórios, `git diff --check` e uma busca de escopo proibido.

## Validação no servidor real

1. Instalar o core AssettoServer fixado pelo submodule e o novo `PoliceChasePlugin.dll`.
2. Confirmar dez AI de tráfego e a reserva única do `CAR_12`.
3. Conectar um jogador e aguardar o spawn nativo do Skoda.
4. Acelerar pela mesma rota e observar redução ou manutenção da distância.
5. Passar por uma ramificação e confirmar que o policial escolhe o caminho do alvo.
6. Ultrapassar o antigo raio de 200 metros e confirmar que o policial não é reciclado.
7. Manter a separação abaixo de 1.500 metros e confirmar continuidade física, sem teleporte.
8. Exceder 1.500 metros e confirmar encerramento da perseguição e retorno ao lifecycle nativo.
9. Confirmar que `CAR_0..CAR_9`, `CAR_10`, `CAR_11`, RandomWeatherPlugin e colisões continuam funcionando.

## Fora de escopo

- sirene e luzes policiais;
- Heat/Wanted, HUD e pontuação;
- PIT ou lógica agressiva de colisão;
- múltiplas viaturas;
- bloqueios e roadblocks;
- teleporte de recuperação comandado pelo plugin;
- navegação reversa, mudança deliberada de faixa ou roteamento global pelo mapa;
- mudanças globais em densidade, quantidade, raio ou velocidade do tráfego.

