# Police Chase P0.3 — detecção de jogador e seleção da viatura

## Objetivo

Comprovar que o plugin consegue detectar jogadores utilizáveis e preparar uma única instância AI policial identificada sem ambiguidade. A etapa não controla velocidade, spline, rota ou comportamento da viatura.

## Compatibilidade e limites

- Compilar exclusivamente contra AssettoServer `v0.0.54`, commit `51737e2c2ee892bc7800df479517eaaf38a828fc`.
- Não modificar o core nesta etapa.
- Não alterar automaticamente `entry_list.ini`, `extra_cfg.yml`, `server_cfg.ini` ou `csp_extra_options.ini`.
- Não alterar parâmetros globais de tráfego.
- Não implementar perseguição, velocidade-alvo, roteamento, teleporte, Heat, HUD, sirenes ou integração com Overtake.
- Preservar AI Traffic, RandomWeatherPlugin, WeatherFX, RainFX, Overtake e o ciclo de tempo existentes.

## Decisão de identificação da viatura

A viatura será identificada por `PoliceCarSessionId`, correspondente ao índice do slot em `entry_list.ini`. O slot deve estar configurado manualmente pelo operador com `AI=FIXED`.

`PoliceCarModel` continuará opcional. Quando preenchido, funcionará como uma proteção adicional: o modelo do slot localizado deverá ser exatamente igual ao configurado. O plugin nunca selecionará outro slot como fallback.

Essa decisão evita capturar acidentalmente um carro do tráfego comum, mesmo quando vários slots usam modelos iguais.

## Configuração

Adicionar à `PoliceChaseConfiguration`:

```yaml
PoliceCarSessionId: 0
```

O valor será representado como inteiro para permitir um sentinela claro no objeto padrão:

- valor padrão: `-1`;
- quando `Enabled: true`: intervalo obrigatório de `0` a `254`;
- quando `Enabled: false`: o valor sentinela será aceito para permitir desabilitar o plugin sem preparar um slot.

`PoliceCarModel` vazio significa que somente o Session ID será verificado. Um valor não vazio exige correspondência ordinal exata.

## Componentes

### PoliceTargetService

Responsável somente pela aquisição e liberação do jogador-alvo lógico.

- Recebe `EntryCarManager` por injeção de dependência.
- Examina jogadores já prontos ao iniciar.
- Assina `EntryCarManager.ClientConnected` e, para cada cliente, assina `ACTcpClient.FirstUpdateSent`.
- Assina `EntryCarManager.ClientDisconnected`.
- Considera elegível somente um `EntryCar` não AI cujo cliente tenha `HasSentFirstUpdate == true`.
- Quando não há alvo, seleciona deterministicamente o jogador elegível com menor `SessionId`.
- Mantém no máximo um alvo.
- Mantém o alvo adquirido até sua desconexão; a chegada posterior de outro jogador não troca a perseguição.
- Quando o alvo desconecta, seleciona o próximo jogador elegível.
- Remove todas as inscrições de eventos no encerramento.

O serviço expõe o alvo atual somente para leitura e notifica mudanças por um evento próprio. Não calcula distância e não lê splines nesta etapa.

Logs de transição:

```text
[PoliceChase] Player connected: <nome> (<session id>)
[PoliceChase] Target acquired: <nome> (<session id>)
[PoliceChase] Target released: <nome> (<session id>)
[PoliceChase] Waiting for an eligible player
```

O log de conexão pode ocorrer antes da aquisição; a aquisição só ocorre depois de `FirstUpdateSent`.

### PoliceAiService

Responsável somente por localizar e preparar o slot policial.

- Recebe `EntryCarManager` e `PoliceChaseConfiguration` por injeção de dependência.
- Localiza o `EntryCar` cujo `SessionId` seja igual a `PoliceCarSessionId`.
- Recusa slot inexistente.
- Exige `AiMode.Fixed`.
- Quando `PoliceCarModel` não estiver vazio, exige igualdade ordinal com `EntryCar.Model`.
- Define `AiMaxOverbooking = 1`.
- Chama `SetAiControl(true)`.
- Chama `SetAiOverbooking(1)` para criar exatamente um estado.
- Usa `GetInitializedStates` para distinguir o estado inicializado do estado ainda aguardando spawn nativo.
- Expõe o slot selecionado e o único `AiState` somente para leitura.

O serviço não chama `Teleport`, não escreve `TargetSpeed`, não altera spline e não toca em `AiParams` globais.

Logs operacionais:

```text
[PoliceChase] Police slot prepared: <session id> (<model>)
[PoliceChase] Police AI initialized
[PoliceChase] Police AI waiting for native spawn
```

Erros de slot devem encerrar a inicialização do Police Chase com uma mensagem específica, sem selecionar outro carro:

```text
[PoliceChase] Police slot <id> was not found
[PoliceChase] Police slot <id> is not configured as AI=FIXED
[PoliceChase] Police slot <id> model mismatch: expected <model>, actual <model>
```

### PoliceChaseService

Continua sendo o único `IAssettoServerAutostart` do plugin.

Quando habilitado:

1. prepara o slot por meio de `PoliceAiService`;
2. inicia a observação por meio de `PoliceTargetService`;
3. registra `[PoliceChase] Plugin initialized`;
4. aguarda o cancelamento do host;
5. encerra as inscrições do target service;
6. registra `[PoliceChase] Plugin stopping`.

Quando desabilitado, mantém o comportamento atual: registra o estado desabilitado e não acessa jogadores ou slots AI.

## Interação com o lifecycle nativo da AI

`SetAiOverbooking(1)` cria um único `AiState`, mas o `AiBehavior` nativo ainda pode recalcular o overbooking posteriormente. `AiMaxOverbooking = 1` impede múltiplos estados, porém não impede que o core reduza o alvo para zero.

Essa limitação será explicitada nos logs e na documentação de teste. Tornar a reserva permanente exige excluir somente a instância policial do balanceamento global, mudança de core deliberadamente reservada para P0.4. Antecipar essa extensão nesta etapa esconderia qual parte foi comprovada apenas com as APIs públicas.

## Concorrência e lifecycle

Eventos de conexão, primeira atualização e desconexão podem chegar de tarefas diferentes. `PoliceTargetService` protegerá o alvo e as inscrições com sincronização interna. Logs e notificações só serão emitidos quando o alvo efetivamente mudar.

Inicialização e encerramento serão idempotentes para evitar inscrições duplicadas. O encerramento não desativa o slot AI nem restaura configurações globais porque nenhuma configuração global é modificada.

## Testes

O desenvolvimento seguirá RED–GREEN.

### Configuração

- `PoliceCarSessionId` padrão é `-1`.
- Quando habilitado, valores abaixo de `0` ou acima de `254` são rejeitados.
- Quando desabilitado, `-1` é aceito.

### Seleção lógica do alvo

- jogadores sem primeira atualização não são elegíveis;
- o menor Session ID elegível é selecionado;
- a chegada de outro jogador não substitui o alvo atual;
- a desconexão do alvo seleciona o próximo elegível;
- a desconexão do último alvo retorna ao estado sem alvo;
- iniciar e encerrar não duplica nem deixa inscrições de eventos.

A regra de ordenação será isolada em uma função pura sobre candidatos simples. Isso permite testar a política sem fabricar toda a árvore de dependências de `ACTcpClient` e `EntryCar`. A adaptação aos eventos reais será coberta por testes do serviço com uma pequena porta interna de eventos, implementada pelo adaptador do AssettoServer.

### Preparação da AI

- encontra somente o Session ID configurado;
- rejeita slot ausente;
- rejeita slot que não seja `AI=FIXED`;
- rejeita divergência de modelo quando a proteção está configurada;
- limita o overbooking a um;
- prepara exatamente um estado;
- diferencia estado inicializado de estado aguardando spawn;
- não usa fallback para outro slot.

As decisões serão testadas por uma porta interna mínima sobre slots. O adaptador de produção será o único componente que chama `SetAiControl`, `SetAiOverbooking` e `GetInitializedStates` nas classes reais do AssettoServer.

### Regressão

- preservar os testes da P0.2;
- executar a suíte completa;
- compilar em Release;
- verificar que o pacote contém somente `PoliceChasePlugin.dll`;
- confirmar que não existem referências a controle de velocidade, teleporte, spline, junction, Heat ou Wanted no código novo.

## Validação manual

A documentação P0.3 fornecerá um exemplo de slot `AI=FIXED` para o operador adaptar manualmente, além do novo bloco YAML. O teste no servidor deverá confirmar:

1. plugin carrega;
2. slot policial correto é preparado;
3. uma única AI é encontrada ou fica aguardando o spawn nativo;
4. o primeiro jogador pronto é adquirido;
5. desconexão libera ou troca o alvo;
6. tráfego comum continua funcionando;
7. nenhuma velocidade ou rota policial é alterada nesta etapa;
8. desabilitar o plugin evita toda preparação e observação.

## Critério de conclusão

A P0.3 termina quando build, testes e pacote passam, e o guia de validação manual está disponível. A implementação deve então parar e aguardar confirmação do operador antes da P0.4.
