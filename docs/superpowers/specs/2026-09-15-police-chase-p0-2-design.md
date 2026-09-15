# Police Chase P0.2 — design do plugin mínimo

## Objetivo

Criar um plugin mínimo e carregável para AssettoServer `0.0.54+51737e2c2e`, com configuração YAML validada e logs de lifecycle. Esta etapa comprova somente build, empacotamento, carregamento, configuração e logs.

## Limites desta etapa

Incluído:

- projeto .NET 8 do plugin;
- dependência reproduzível do AssettoServer 0.0.54;
- módulo Autofac do plugin;
- configuração YAML e validação;
- serviço autostart com logs de inicialização e encerramento;
- testes automatizados;
- instruções de build, instalação e validação manual.

Excluído:

- detecção ou seleção de jogadores;
- seleção, criação ou reserva de viatura AI;
- controle de velocidade, spline, lane ou junction;
- estados `IDLE`, `ACQUIRE_TARGET`, `CHASE` e `LOST`;
- qualquer alteração do core do AssettoServer;
- qualquer alteração automática em `server_cfg.ini`, `entry_list.ini`, `extra_cfg.yml` ou `csp_extra_options.ini`;
- Heat, HUD, sirenes, luzes, PIT, roadblocks, persistência e integração com Overtake.

## Compatibilidade e dependências

O repositório incluirá `external/AssettoServer` como submódulo Git apontando para o repositório oficial `https://github.com/compujuckel/AssettoServer.git` e fixado no commit `51737e2c2ee892bc7800df479517eaaf38a828fc` (`v0.0.54`).

O projeto `PoliceChasePlugin` terá target `net8.0`, `EnableDynamicLoading=true`, `SelfContained=false` e referências de projeto para:

- `external/AssettoServer/AssettoServer/AssettoServer.csproj`;
- `external/AssettoServer/AssettoServer.Shared/AssettoServer.Shared.csproj`.

As referências usarão `Private=false` e `ExcludeAssets=runtime`, seguindo o `SamplePlugin` da versão fixada. Dessa forma, os assemblies do servidor não serão duplicados no diretório do plugin.

## Estrutura de arquivos

```text
PoliceChasePlugin.sln
.gitmodules
external/AssettoServer/
src/PoliceChasePlugin/
    PoliceChasePlugin.csproj
    PoliceChaseModule.cs
    PoliceChaseConfiguration.cs
    PoliceChaseConfigurationValidator.cs
    PoliceChaseService.cs
tests/PoliceChasePlugin.Tests/
    PoliceChasePlugin.Tests.csproj
    PoliceChaseConfigurationTests.cs
    PoliceChaseConfigurationValidatorTests.cs
    PoliceChaseModuleTests.cs
docs/P0.2-plugin-minimo.md
```

Cada arquivo terá uma responsabilidade única. Não serão criados serviços de target ou AI antes das etapas correspondentes.

## Componentes

### `PoliceChaseModule`

Herdará `AssettoServerModule<PoliceChaseConfiguration>`. No método `Load`, registrará `PoliceChaseService` como:

- o próprio tipo;
- `IAssettoServerAutostart`;
- singleton.

Nenhum registro existente do AssettoServer será substituído.

### `PoliceChaseConfiguration`

Implementará `IValidateConfiguration<PoliceChaseConfigurationValidator>` e exporá:

| Propriedade | Padrão |
|---|---:|
| `Enabled` | `true` |
| `PoliceCarModel` | `""` |
| `MaxPoliceSpeedKph` | `280` |
| `FarDistanceMeters` | `800` |
| `MediumDistanceMeters` | `400` |
| `NearDistanceMeters` | `150` |
| `FarSpeedBonusKph` | `60` |
| `MediumSpeedBonusKph` | `40` |
| `NearSpeedBonusKph` | `20` |
| `LostDistanceMeters` | `2000` |
| `Debug` | `true` |
| `DebugIntervalMs` | `1000` |

As propriedades serão mutáveis para desserialização YAML. Nenhuma delas será conectada ao AI nesta etapa.

### `PoliceChaseConfigurationValidator`

Usará FluentValidation e recusará configurações que possam produzir um controlador incoerente nas fases seguintes:

- `MaxPoliceSpeedKph` maior que zero;
- distâncias `Near`, `Medium`, `Far` e `Lost` maiores que zero;
- ordem estrita `Near < Medium < Far < Lost`;
- bônus de velocidade não negativos;
- `DebugIntervalMs` maior que zero.

`PoliceCarModel` poderá permanecer vazio na P0.2, pois o modelo só será necessário na P0.3.

### `PoliceChaseService`

Herdará `CriticalBackgroundService` e implementará `IAssettoServerAutostart`, seguindo os plugins oficiais da versão 0.0.54.

No início:

- se `Enabled=true`, emitirá `"[PoliceChase] Plugin initialized"` em nível Information;
- se `Enabled=false`, emitirá `"[PoliceChase] Plugin disabled by configuration"` em nível Information e encerrará sem iniciar lógica adicional.

No cancelamento, quando habilitado, emitirá `"[PoliceChase] Plugin stopping"`. O serviço não criará loop periódico na P0.2.

## Configuração YAML

O loader da versão 0.0.54 usa um documento YAML adicional dentro de `extra_cfg.yml`. A documentação mostrará este conteúdo para inclusão manual:

```yaml
---
!PoliceChaseConfiguration
Enabled: true
PoliceCarModel: ""
MaxPoliceSpeedKph: 280
FarDistanceMeters: 800
MediumDistanceMeters: 400
NearDistanceMeters: 150
FarSpeedBonusKph: 60
MediumSpeedBonusKph: 40
NearSpeedBonusKph: 20
LostDistanceMeters: 2000
Debug: true
DebugIntervalMs: 1000
```

Também será documentada a inclusão manual de `PoliceChasePlugin` na lista `EnablePlugins`. Nenhum arquivo real do servidor será editado pelo projeto.

## Empacotamento

O build Release para `win-x64` produzirá a DLL do plugin em diretório separado. O pacote instalável conterá somente os arquivos próprios necessários do plugin; assemblies fornecidos pelo AssettoServer serão excluídos.

A documentação indicará copiar o conteúdo para:

```text
<AssettoServer>/plugins/PoliceChasePlugin/
```

O nome da DLL deve ser `PoliceChasePlugin.dll`, pois o loader procura uma DLL com o mesmo nome da pasta.

## Testes

O fluxo seguirá TDD. Antes de cada implementação comportamental, um teste será criado e executado para confirmar a falha esperada.

Os testes cobrirão:

1. valores padrão completos da configuração;
2. aceitação de uma configuração válida;
3. rejeição de velocidade máxima não positiva;
4. rejeição de distâncias fora de ordem;
5. rejeição de bônus negativos;
6. rejeição de intervalo de debug não positivo;
7. registro único de `PoliceChaseService` como `IAssettoServerAutostart` no módulo.

O build completo da solução e todos os testes deverão terminar sem erros antes do commit final da P0.2.

## Validação manual no servidor

Após copiar o plugin e adicionar manualmente sua configuração, o operador deverá iniciar o AssettoServer e procurar:

```text
Loaded plugin PoliceChasePlugin
[PoliceChase] Plugin initialized
```

Com `Enabled: false`, deverá procurar:

```text
Loaded plugin PoliceChasePlugin
[PoliceChase] Plugin disabled by configuration
```

O servidor deverá iniciar normalmente, e os logs do AI Traffic, RandomWeatherPlugin e WeatherFX não deverão apresentar mudança ou erro novo.

## Critério de conclusão da P0.2

A etapa estará concluída quando:

- o submódulo estiver fixado no commit correto;
- a solução compilar contra o AssettoServer 0.0.54;
- todos os testes passarem;
- o pacote tiver o layout esperado pelo loader;
- as instruções de instalação e logs esperados estiverem documentadas;
- nenhuma funcionalidade da P0.3 ou posterior tiver sido implementada;
- todas as alterações estiverem commitadas e enviadas a `origin/main`.
