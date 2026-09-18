# P0.6 — correção da transição real entre splines equivalentes

## Escopo

Corrigir a P0.6 sem avançar para P0.7. A viatura deve continuar numa rota forward fisicamente válida quando o jogador muda entre splines/faixas que o próprio AssettoServer agrupou com `GetLanes()`. A correção não adiciona arestas laterais ao grafo, não ordena troca de faixa, não teleporta e não altera o lifecycle ou parâmetros globais do tráfego.

## Evidência do teste real

O segundo teste iniciou com:

```text
policePoint 171036, targetPoint 171048, distance 19.3, junctions 0
```

No `fast_lane.aip` real:

- `171036` e `171048` pertencem à `fast_lane.ai`;
- a distância forward entre eles é `19,2624 m`, confirmando a correlação com o log;
- o ponto correspondente na `fast_lane_bayshore_1.ai` é `227470`;
- no local de `227470`, o ponto alcançável `171048` aparece apenas depois dos 16 vizinhos brutos consultados pela P0.6 e fica a `7,0306 m`;
- `AdjacentLaneDetector`, com largura de faixa de 3 m, forma a cadeia real `227470 -> 57704 -> 171048`;
- `AiSpline.GetLanes(227470)` é a identidade topológica que permite tratar `171048` como destino equivalente, mesmo sem aresta lateral de movimento;
- o candidato `57704`, que entra nos 16 vizinhos, exige aproximadamente `179,18 km` de percurso forward a partir de `171036`, excedendo corretamente o orçamento de 20 km;
- a próxima junction configurada a partir daquela região é `174161`, aproximadamente `4,96 km` à frente. Em 39 segundos a 180 km/h seriam percorridos no máximo `1,95 km`, portanto `junctions 0` era esperado naquele instante.

## Causa raiz

A P0.5 expandia o ponto localizado pelo KD-tree com `_spline.GetLanes(targetPointId)`. A P0.6 substituiu isso por até 16 vizinhos espaciais em `AiPursuitTargetLocator` e não preservou a expansão de equivalência de faixas.

Quando o jogador passou à `fast_lane_bayshore_1.ai`, os 16 vizinhos ficaram ocupados por pontos densos da própria spline e de uma spline intermediária. O ponto `171048`, alcançável pela viatura a 19,3 m, não chegou ao conjunto de destinos do planner. Os destinos que chegaram eram desconectados ou exigiam mais de 20 km forward. O planner, a grace de 2 s e o `NoRoute` funcionaram corretamente sobre uma lista incompleta de destinos.

## Correção mínima

1. Continuar consultando até 16 sementes espaciais dentro do limite configurado.
2. Para cada semente aceita, expandir os pontos reais retornados por `AiSpline.GetLanes()`.
3. Filtrar direção também nos equivalentes e deduplicar por `PointId`.
4. Não aplicar o corte de 16 depois da expansão: 16 limita sementes do KD-tree, não a cadeia topológica já detectada pelo AssettoServer.
5. Manter o grafo exclusivamente em `NextId` e `SplineJunction.EndPointId`; equivalentes são somente destinos alternativos, nunca arestas de movimento.

## Diagnóstico

O cache `AiSpline` versão 1 não preserva os nomes de arquivos `fast_lane*.ai`. Portanto os logs não inventarão nomes de spline. Eles usarão somente identidades reais disponíveis em runtime:

- `PointId` policial;
- `PointId` anterior/selecionado do alvo;
- sementes espaciais;
- equivalentes de `GetLanes()`;
- rejeições por distância/direção;
- motivo do `AiRoutePlanner`;
- nós visitados;
- distância máxima explorada;
- quantidade de arestas de junction examinadas.

Os detalhes serão emitidos apenas na entrada em perda temporária, recuperação, recálculo relevante e falha definitiva. Não haverá log por atualização de 200 ms.

## Testes

- Unitário do locator: a semente de uma spline desconectada expande para um ponto equivalente alcançável que estava fora dos 16 vizinhos.
- Integração mínima: gerar um `.aip` sintético com três splines paralelas densas, executar `FastLaneParser`, `AdjacentLaneDetector`, cache `AiSpline`, locator, planner e navigator.
- Integração local explícita: usar `POLICE_CHASE_FAST_LANE_AIP` para carregar o pacote real sem versioná-lo e reproduzir `171036 -> 227470`, exigindo a escolha de `171048` com aproximadamente 19,3 m.
- Regressão de diagnóstico no core e no plugin.
- Suítes completas e builds Release.

## Critério de conclusão

A correção automatizada estará pronta quando o caso real de dados produzir rota ativa para `171048`, sem aresta lateral e sem aumento de grace/orçamentos. A P0.6 continuará marcada como pendente até um novo teste no servidor confirmar visualmente a transição física.
