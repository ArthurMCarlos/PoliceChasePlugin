# P0.9 — perseguição próxima com recuperação e desvio de tráfego

## Estado e intenção

Especificação para revisão, ainda sem implementação. Execução inline escolhida
pelo usuário; commit/push de cada entrega autorizada permanece obrigatório.
Base: plugin d51157f4b69d8c2951820117aa05d818fdf59019 e core
76fd5df4af8aaff7b20689943b9a868087ad3378.

O usuário confirmou no servidor real que a P0.8.1 armou na revisão 216,
iniciou na 217, deslocou lateralmente a viatura, registrou colisão 12 ↔ 10,
PIT Contact e CollisionRecovery. Essa é evidência relatada pelo operador,
incluindo validação visual; não uma nova execução local nossa.

Objetivo aprovado: uma acelerada forte em um carro acima de 200 km/h não
deve bastar para deixar a viatura para trás. A polícia deve recuperar distância,
usar outra faixa quando o trânsito bloquear a atual e voltar atrás do alvo.
O desempenho realista do Skoda não é uma restrição. Permanecem movimento
contínuo, segurança de trajetória e possibilidade de escapar com vantagem
grande e sustentada. Não há promessa de captura inevitável em qualquer cenário.

## Evidências no código atual

- PoliceChaseConfiguration define PursuitMaxSpeedKph=180 por padrão;
  AiPursuitDrivingController.Update limita a solicitação ao máximo configurado.
  Não foi fornecido o YAML atual completo; 180 não é diagnóstico confirmado
  do teto efetivamente usado no último teste real.
- AiState.DetectObstacles termina chamando SetTargetSpeed com
  EntryCar.AiAcceleration. Uma solicitação alta não aumenta essa aceleração.
- Frenagem por outros jogadores, AI e spline pode limitar a velocidade final.
- AiPursuitLaneChangePipeline combina FutureJunction e TargetLaneAlignment;
  não contém uma motivação específica de ultrapassagem por obstáculo lento.
- A distância máxima é verificada em TrackPursuitLocked e ShouldRetainPursuit.
  Acrescentar apenas um timer ao plugin não resolve o lifecycle nativo.
- Após release, o mesmo alvo pode voltar a ser apresentado ao serviço.
  Fuga precisa impedir rearme imediato, além de liberar a perseguição.

Esses pontos explicam onde atuar, não provam qual limitador predominou em
cada situação observada. A instrumentação deve tornar essa distinção visível.

## Alternativas e decisão

1. Apenas elevar velocidade: simples, mas não resolve aceleração, bloqueios
   e escolha de faixa. Não escolhida.
2. Recuperação individual coordenada com desvio e retorno: escolhida.
   Reutiliza rota e transição física existentes, separando intenção e safety.
3. Aproximação ilimitada/teleporte: rejeitada; elimina fuga e continuidade visual.

## Contrato de comportamento

### Recuperação individual

Modo novo opt-in, desligado por padrão. Ao desligar, comportamento P0.8.1
preservado. Só o estado reservado da viatura com alvo válido recebe assistência.
Não escrever em configuração global, EntryCar compartilhado ou nos outros slots.

A solicitação toma a velocidade atual do alvo como referência e acrescenta
vantagem progressiva quando distante. A assistência não pode ficar presa ao
teto realista antigo do Skoda/plugin quando o novo modo estiver ativo.
Perto, a política P0.8 de aproximação, Contact e Recovery volta a prevalecer.

Valores iniciais PROPOSTOS, não calibrados nem validados no servidor:

| Parâmetro do novo modo | Valor inicial |
| --- | --- |
| Início da assistência de distância | 60 m |
| Assistência máxima | 250 m |
| Vantagem adicional máxima de recuperação | 80 km/h |
| Aceleração assistida máxima da viatura | 10 m/s² |
| Teto técnico absoluto da viatura | 400 km/h |
| Variação máxima da aceleração assistida | 5 m/s³ |

Esses limites são técnicos e configuráveis, não o desempenho real do Skoda.
400 km/h não é uma meta constante. Alvos acima do teto, curvas, bloqueios ou
rotas indisponíveis podem impedir a recuperação. Não usar infinito/NaN.

A distância física deve impedir boost quando os veículos estiverem próximos,
mesmo se a rota der uma volta longa; distância de rota sozinha não basta.
Rampas usam tempo decorrido, não número de frames. Solicitação assistida deve
ser antecipadamente reduzida pela distância necessária para frear e atingir
o envelope de aproximação: não esperar cruzar 60 m em alta velocidade relativa.
Não elevar limites PIT/Contact nem desativar ExcessClosingSpeed para compensar.

Safety de curvas, obstáculos e lane change é aplicada depois da intenção.
Recovery, medições inválidas, alvo indisponível e perda de navegação desativam
assistência. Frenagem de emergência não espera a rampa de aceleração.
Não permitir que o timeout nativo de ignorar obstáculos seja usado pela viatura
em perseguição ativa como mecanismo para atravessar congestionamento; manter
essa política nativa dos carros comuns inalterada.

### Contornar tráfego e retornar

Adicionar decisão tática stateful e limitada: Follow → Bypass → Return → Follow.
Reutilizar relações físicas adjacentes, rotas forward-only, cursores e transição
progressiva P0.7. Left/Right não viram arestas do grafo. Nunca saltar duas faixas.

Proposta de disparo: obstáculo não-alvo adiante limitando a viatura por pelo
menos 1 segundo, com diferença mínima de 10 km/h entre solicitação e limite
por obstáculo. Examinar somente as faixas adjacentes fisicamente compatíveis,
com horizonte limitado de 150 m e dentro dos budgets de busca existentes.
Todos esses valores são propostas de teste, não fatos sobre a pista real.

Escolher faixa com espaço longitudinal melhor, rota viável ao alvo e safety
de entrada aprovada. Se ambos os lados servirem, priorizar a menor perda de
progresso de rota; desempate determinístico. Ausência de opção mantém frenagem.
Junction que exige posicionamento tem prioridade sobre ultrapassagem opcional.

Durante Bypass, memorizar o obstáculo por identidade/geração do estado, não só
session id; não voltar imediatamente por TargetLaneAlignment. Após ultrapassar
e abrir folga longitudinal mínima de 12 m ao obstáculo, tentar Return para a
faixa ATUAL do alvo. Retorno exige safety e cooldown P0.7 existentes; se não
houver gap, permanecer e reavaliar. Despawn do obstáculo invalida sua referência,
mas não autoriza retorno sem safety. Nova faixa do jogador atualiza a intenção,
sem interromper uma transição física já comprometida.

Alvo à frente e compatível continua objetivo; esta fase não busca passar à
frente do jogador para brake-check. PIT Armed/Attempting tem precedência sobre
ultrapassagem opcional; a devolução do offset ao centro antecede lane change.
Necessidades reais de rota/safety continuam podendo abortar PIT como antes.

### Fuga sustentada

Proposta inicial: distância espacial maior que 800 m continuamente por 15 s
encerra perseguição com motivo Escaped. Voltar para 800 m ou menos zera o timer.
Usar relógio monotônico e limpar estado em troca de alvo, desconexão ou reset.
Timer só progride com medidas finitas; falha de navegação mantém seu tratamento
próprio, sem fingir que houve fuga por tráfego.

Preservar PursuitMaxDistanceMeters como corte duro de lifecycle (1500 m por
padrão). Ele deve ser maior que o limiar de fuga; cruzá-lo ainda libera a viatura
sem esperar 15 s, por proteção de recursos. A mesma regra precisa ser coerente
em ShouldRetainPursuit e TrackPursuitLocked, sem afetar reservas de tráfego.

Depois de Escaped ou corte duro, bloquear nova perseguição à mesma conexão
por 60 s, mesmo que o seletor ainda apresente esse alvo. Não bloquear outros
jogadores elegíveis. Não forçar respawn/teleporte perto do fugitivo. Isso é uma
pausa de rearme da perseguição, não Heat/Wanted nem persistência entre sessões.

## Responsabilidades e integração

- Plugin: opções validadas do novo modo, conversão de unidades, entrega ao core,
  logs de transição e suspensão de rearme após fuga. Defaults mantêm modo antigo.
- Política de recuperação: cálculo puro de intenção/assistência, entrada finita,
  redução por proximidade e frenagem; estado individual e testável.
- Decisão tática: memória de Bypass/Return e comparação limitada de candidatos;
  P0.6/P0.7 continuam responsáveis pela rota e execução lateral segura.
- AiState: captura de medidas/obstáculos, aplicação individual da aceleração
  assistida e limites efetivos; nenhum segundo escritor de posição ou velocidade.
- Lifecycle: estado e relógio da fuga por perseguição; resultado explícito para
  que o plugin não reinicie automaticamente uma perseguição encerrada.

Pontos principais a alterar no plano: PoliceChaseConfiguration/Validator,
contratos e adapter, PolicePursuitService, AiPursuitControl/DrivingController,
AiState e integração do LaneChangePipeline com a nova política tática.
Extrair políticas pequenas; não reescrever os controladores já validados.

## Diagnóstico e testes de aceitação

Logs de transição para assistência ativa/inativa, Bypass/Return, fuga pendente,
cancelada e concluída. Resumo limitado a uma vez por 5 s: distância física/rota,
velocidades alvo/polícia, intenção, limite efetivo e motivo dominante (teto,
curva, AI, jogador, lane safety, Recovery). Preservar diagnósticos PIT atuais.

Testes RED/GREEN obrigatórios:

- Alvo 200/250/300 km/h em trecho livre: intenção distante maior que a do alvo
  até teto técnico; integração da aceleração realmente usada, não só do pedido.
- Redução progressiva da assistência, timestep variável, sem NaN/infinito;
  velocidade alta relativa próxima não contorna gates de aproximação/PIT.
- Curva, terceiro e lane safety continuam limitando movimento efetivo.
- Obstáculo lento: desvio só com gap, continuidade Bypass, retorno seguro,
  alvo mudando de faixa, obstáculo reciclado/despawn, ambos os lados bloqueados.
- Prioridade de junction e transição comprometida; não oscilar faixa por frame.
- Fuga com pico curto, período sustentado, reset, corte duro e não rearme imediato.
- Modo desligado e CAR_0..9/CAR_10..11 preservados; opt-in atua só na polícia.
- Regressões completas de P0.6, P0.7, P0.8 e PIT, inclusive revisão 216 → 217,
  zero-length, target exclusion, Contact, CollisionRecovery e side safety.

Executar as duas suítes completas e os builds Release core/plugin; relatar
explicitamente testes não executados e avisos. Revisão independente antes de
publicação, core primeiro e main com gitlink alcançável depois.

No servidor: comparar reta com alvo a 200–300 km/h, trânsito com saída livre,
trânsito sem gap, mudanças de faixa rápidas, bifurcações, PIT e fuga sustentada.
Verificar redução real da distância e movimento sem saltos em vídeo; um número
alto no log de intenção não é prova de recuperação física.

## Limites e fora de escopo

Não reproduzir o runtime singleplayer da 2REAL nem prometer equivalência exata.
Não reduzir tráfego durante perseguição. Sem teleporte, snap, força artificial,
dano no jogador, multi-viatura, Heat, HUD, sirene ou alteração global de AI.
Os valores iniciais acima exigem aprovação desta especificação e calibração
posterior com evidência; nenhuma mudança de configuração foi aplicada agora.
