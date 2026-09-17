# Police Chase P0.4 — reserva dedicada de AI

## Objetivo

Garantir permanentemente exatamente um `AiState` para o slot identificado por `PoliceCarSessionId`, sem transformar o carro policial em parte da distribuição dinâmica do tráfego e sem alterar os slots de jogadores ou a quantidade normal de tráfego.

Com a configuração validada no servidor real:

- `CAR_0..CAR_9` continuam sendo os dez slots dinâmicos de tráfego;
- `CAR_10` e `CAR_11` continuam sendo slots de jogadores;
- `CAR_12` mantém exatamente uma instância AI dedicada;
- o total operacional passa a ser dez instâncias de tráfego mais uma instância policial reservada.

## Quando o carro aparecerá

O carro policial começará a aparecer nesta P0.4. Depois que o plugin reservar o único `AiState`, o `AiBehavior` nativo poderá inicializá-lo quando existir um jogador elegível e um ponto seguro de spawn.

A P0.4 garante presença e permanência da instância, mas ainda não implementa perseguição. Até uma fase posterior, o posicionamento inicial e o movimento permanecem sob o lifecycle nativo de AI do AssettoServer.

## Causa raiz confirmada

No AssettoServer `51737e2c2ee892bc7800df479517eaaf38a828fc`, `AiBehavior.AdjustOverbooking()` trata todos os carros com `Client == null && AiControlled` como um único conjunto. `AI=FIXED` impede que humanos ocupem o slot, mas não cria uma reserva mínima.

Com um jogador e onze slots controlados por AI, o cálculo observado foi:

```text
targetAiCount = 10
overbooking = 10 / 11 = 0
rest = 10 % 11 = 10
```

Os dez primeiros slots recebem target `1`; o décimo primeiro, `CAR_12`, recebe target `0`. `AiMaxOverbooking = 1` é apenas um teto, portanto não impede a redução. Quando `EntryCar.CanSpawnAiState()` encontra o estado policial com índice zero e `TargetAiStateCount == 0`, executa `Despawn()` e remove esse estado.

## Arquitetura proposta

### Reserva mínima no core

`EntryCar` receberá a propriedade runtime:

```csharp
public int AiMinOverbooking { get; set; }
```

O valor padrão será zero, preservando integralmente o comportamento dos slots existentes.

`EntryCar.SetAiOverbooking(int count)` limitará o target entre o mínimo e o máximo. Para o policial, o plugin configurará mínimo e máximo como `1`, tornando sua cardinalidade exata.

### Separação da distribuição dinâmica

`AiBehavior.AdjustOverbooking()` separará:

- slots dinâmicos, com `AiMinOverbooking == 0`;
- slots reservados, com `AiMinOverbooking > 0`.

O target calculado por jogador será distribuído somente entre os slots dinâmicos. Slots reservados receberão diretamente seu mínimo e não consumirão uma posição da distribuição normal.

Para a topologia validada:

```text
dynamic slots = CAR_0..CAR_9 = 10
dynamic target = 10
reserved slots = CAR_12 = 1
final targets = [1,1,1,1,1,1,1,1,1,1,1]
```

O `MaxAiTargetCount` continua limitando o tráfego dinâmico. A instância policial dedicada é adicional e não exige aumentar nenhuma configuração global.

### Marcação pelo Session ID exato

Depois de validar `PoliceCarSessionId`, `AI=FIXED` e o modelo opcional, o adaptador do plugin configurará somente o `EntryCar` selecionado:

```csharp
slot.AiMinOverbooking = 1;
slot.AiMaxOverbooking = 1;
slot.SetAiControl(true);
slot.SetAiOverbooking(1);
```

A reserva mínima não será configurada por `CarSpecificOverrides`, pois esse mecanismo seleciona por modelo e poderia afetar outros slots que utilizem o mesmo veículo.

## Política isolada e testável

O cálculo de targets será extraído para uma unidade pura, sem dependência de rede, spline ou estado global. Ela deverá:

1. distribuir o target dinâmico somente entre entradas com mínimo zero;
2. manter o mínimo de entradas reservadas;
3. preservar exatamente a distribuição antiga quando nenhum mínimo existir;
4. funcionar independentemente da posição do slot reservado;
5. rejeitar valores negativos;
6. fazer o mínimo prevalecer se uma configuração inválida declarar máximo menor que mínimo.

## Lifecycle esperado

1. O servidor cria `EntryCar` para todos os slots.
2. O plugin localiza exatamente o slot configurado.
3. O plugin define mínimo e máximo como um e cria o estado inicial.
4. A entrada de um jogador dispara `AdjustOverbooking()`.
5. Os dez slots normais recebem a mesma distribuição anterior.
6. O slot policial continua com target um.
7. `AiBehavior.Update()` encontra o estado policial ainda válido e pode posicioná-lo pelo spawn nativo.
8. Recalcular densidade ou conectar/desconectar jogadores não reduz a reserva.

## Observabilidade

O log de P0.3 `Police AI waiting for native spawn` representa apenas um snapshot imediatamente após a criação do estado. A ausência posterior de `Police AI initialized` não é, isoladamente, uma observação contínua.

Na validação da P0.4, o log de overbooking deverá distinguir slots dinâmicos e reservados. A presença efetiva do `CAR_12` será o critério primário de inicialização; uma futura melhoria de log de transição poderá ser adicionada somente se não ampliar o lifecycle desta fase.

## Testes de aceitação

### Core

- Dez slots dinâmicos e um reservado produzem dez targets dinâmicos mais uma reserva.
- A posição do reservado não altera os dez slots dinâmicos.
- Sem reservas, a distribuição anterior permanece idêntica.
- Uma solicitação de zero não reduz um target com mínimo um.
- Valores negativos são rejeitados.

### Plugin

- Somente o Session ID configurado recebe `AiMinOverbooking = 1` e `AiMaxOverbooking = 1`.
- O slot preparado continua contendo exatamente um estado.
- Slot inexistente, não `FIXED` ou modelo incompatível continua falhando sem fallback.
- Com `Enabled: false`, nenhuma reserva é aplicada.

### Servidor real

- O log informa dez slots dinâmicos e um reservado.
- `CAR_0..CAR_9` continuam presentes como antes.
- `CAR_12` ganha uma instância AI e não desaparece após recálculos.
- `CAR_10` e `CAR_11` permanecem disponíveis aos jogadores.
- RandomWeatherPlugin e AI Traffic permanecem funcionais.

## Fora de escopo

- perseguição;
- velocidade dinâmica;
- controle de spline ou rota;
- pathfinding próprio;
- teleporte comandado pelo plugin;
- Heat, Wanted, HUD ou sirene;
- alterações em `AiPerPlayerTargetCount`, `MaxAiTargetCount`, `TrafficDensity` ou `MaxSpeedKph`;
- reflection ou acesso a membros privados.

## Compatibilidade

A alteração será mínima e compatível por padrão: `AiMinOverbooking == 0` reproduz a política original. O plugin usa somente API pública e marca o slot exato em runtime. Nenhum arquivo de configuração do servidor será editado automaticamente.
