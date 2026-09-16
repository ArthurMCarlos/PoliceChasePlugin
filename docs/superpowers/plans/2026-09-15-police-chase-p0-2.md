# Police Chase P0.2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a minimal, loadable PoliceChasePlugin for AssettoServer `0.0.54+51737e2c2e` with validated YAML configuration, lifecycle logs, tests, and an installable DLL.

**Architecture:** Pin the official AssettoServer source as a Git submodule and compile a .NET 8 class library against its exact `AssettoServer` and `AssettoServer.Shared` projects. Register one `CriticalBackgroundService` through `AssettoServerModule<PoliceChaseConfiguration>`; the service only logs enabled/disabled lifecycle state in P0.2.

**Tech Stack:** C# 12 / .NET 8, Autofac 7.1, FluentValidation 11.8, Serilog 3.1, NUnit 3.14, AssettoServer v0.0.54.

**Spec:** `docs/superpowers/specs/2026-09-15-police-chase-p0-2-design.md`

## Global Constraints

- Compile against AssettoServer commit `51737e2c2ee892bc7800df479517eaaf38a828fc` only.
- Target `net8.0`; do not use APIs introduced after AssettoServer 0.0.54.
- Do not modify AssettoServer core in P0.2.
- Do not read players or select/control AI in P0.2.
- Do not modify `server_cfg.ini`, `entry_list.ini`, `extra_cfg.yml`, or `csp_extra_options.ini`.
- Do not alter AI Traffic, Overtake, WeatherFX, RandomWeatherPlugin, RainFX, or time progression.
- Use tests first for each production behavior and observe the expected RED failure before implementing GREEN.
- Push every completed commit to `origin/main`.

---

### Task 1: Reproducible solution and pinned AssettoServer dependency

**Files:**
- Create: `.gitmodules` through `git submodule add`
- Create: `external/AssettoServer` gitlink pinned to the required commit
- Create: `.gitignore`
- Create: `global.json`
- Create: `PoliceChasePlugin.sln`
- Create: `src/PoliceChasePlugin/PoliceChasePlugin.csproj`
- Create: `tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj`
- Create: `tests/PoliceChasePlugin.Tests/GlobalUsings.cs`

**Interfaces:**
- Consumes: official AssettoServer projects at the pinned gitlink.
- Produces: a buildable empty plugin library and NUnit test project used by every later task.

- [ ] **Step 1: Confirm repository and SDK baseline**

Run:

```powershell
git status --short --branch
dotnet --version
```

Expected: clean `main...origin/main`; installed SDK capable of targeting .NET 8.

- [ ] **Step 2: Add and pin the official submodule**

Run:

```powershell
git submodule add https://github.com/compujuckel/AssettoServer.git external/AssettoServer
git -C external/AssettoServer checkout 51737e2c2ee892bc7800df479517eaaf38a828fc
git -C external/AssettoServer describe --tags --always
git -C external/AssettoServer rev-parse HEAD
```

Expected: `v0.0.54` and full hash `51737e2c2ee892bc7800df479517eaaf38a828fc`.

- [ ] **Step 3: Add minimal repository build settings**

Create `.gitignore`:

```gitignore
**/bin/
**/obj/
.vs/
TestResults/
artifacts/
```

Create `global.json`:

```json
{
  "sdk": {
    "version": "8.0.0",
    "rollForward": "major",
    "allowPrerelease": true
  }
}
```

- [ ] **Step 4: Create the plugin project**

Create `src/PoliceChasePlugin/PoliceChasePlugin.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <SelfContained>false</SelfContained>
    <DebugType>embedded</DebugType>
    <PathMap>$(MSBuildProjectDirectory)=$(MSBuildProjectName)</PathMap>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\external\AssettoServer\AssettoServer.Shared\AssettoServer.Shared.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
    <ProjectReference Include="..\..\external\AssettoServer\AssettoServer\AssettoServer.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
  </ItemGroup>

  <Target Name="PackagePlugin" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
    <PropertyGroup>
      <PluginPackageDirectory>$(MSBuildProjectDirectory)\..\..\artifacts\PoliceChasePlugin</PluginPackageDirectory>
    </PropertyGroup>
    <MakeDir Directories="$(PluginPackageDirectory)" />
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(PluginPackageDirectory)" />
  </Target>
</Project>
```

- [ ] **Step 5: Create the test project**

Create `tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
    <PackageReference Include="NUnit.Analyzers" Version="3.9.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.0">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\PoliceChasePlugin\PoliceChasePlugin.csproj" />
  </ItemGroup>
</Project>
```

Create `tests/PoliceChasePlugin.Tests/GlobalUsings.cs`:

```csharp
global using NUnit.Framework;
global using PoliceChasePlugin;
```

- [ ] **Step 6: Create the solution and add projects**

Run:

```powershell
dotnet new sln --name PoliceChasePlugin
dotnet sln PoliceChasePlugin.sln add src/PoliceChasePlugin/PoliceChasePlugin.csproj
dotnet sln PoliceChasePlugin.sln add tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj
dotnet restore PoliceChasePlugin.sln
dotnet build PoliceChasePlugin.sln --no-restore
```

Expected: restore and empty solution build succeed with 0 errors.

- [ ] **Step 7: Verify dependency and package metadata**

Run:

```powershell
git -C external/AssettoServer rev-parse HEAD
dotnet build src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release --no-restore
Get-ChildItem artifacts/PoliceChasePlugin
```

Expected: pinned hash is exact; Release build creates only `PoliceChasePlugin.dll` in the package directory.

- [ ] **Step 8: Commit and push infrastructure**

Run:

```powershell
git add .gitmodules external/AssettoServer .gitignore global.json PoliceChasePlugin.sln src/PoliceChasePlugin/PoliceChasePlugin.csproj tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj tests/PoliceChasePlugin.Tests/GlobalUsings.cs
git commit -m "build: scaffold plugin against AssettoServer 0.0.54"
git push origin main
```

---

### Task 2: Configuration defaults and validation

**Files:**
- Create: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs`
- Create: `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs`
- Create: `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`
- Create: `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs`

**Interfaces:**
- Consumes: `IValidateConfiguration<TValidator>` and FluentValidation supplied by AssettoServer 0.0.54.
- Produces: `PoliceChaseConfiguration` with the exact P0 defaults and `PoliceChaseConfigurationValidator.Validate(...)`.

- [ ] **Step 1: Write the failing defaults test**

Create `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs`:

```csharp
namespace PoliceChasePlugin.Tests;

[TestFixture]
public class PoliceChaseConfigurationTests
{
    [Test]
    public void NewConfigurationUsesP0Defaults()
    {
        var configuration = new PoliceChaseConfiguration();

        Assert.Multiple(() =>
        {
            Assert.That(configuration.Enabled, Is.True);
            Assert.That(configuration.PoliceCarModel, Is.Empty);
            Assert.That(configuration.MaxPoliceSpeedKph, Is.EqualTo(280));
            Assert.That(configuration.FarDistanceMeters, Is.EqualTo(800));
            Assert.That(configuration.MediumDistanceMeters, Is.EqualTo(400));
            Assert.That(configuration.NearDistanceMeters, Is.EqualTo(150));
            Assert.That(configuration.FarSpeedBonusKph, Is.EqualTo(60));
            Assert.That(configuration.MediumSpeedBonusKph, Is.EqualTo(40));
            Assert.That(configuration.NearSpeedBonusKph, Is.EqualTo(20));
            Assert.That(configuration.LostDistanceMeters, Is.EqualTo(2000));
            Assert.That(configuration.Debug, Is.True);
            Assert.That(configuration.DebugIntervalMs, Is.EqualTo(1000));
        });
    }
}
```

- [ ] **Step 2: Run RED for the missing configuration**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter NewConfigurationUsesP0Defaults
```

Expected: compile failure because `PoliceChaseConfiguration` does not exist.

- [ ] **Step 3: Implement the minimal configuration**

Create `src/PoliceChasePlugin/PoliceChaseConfiguration.cs`:

```csharp
using AssettoServer.Server.Configuration;
using JetBrains.Annotations;

namespace PoliceChasePlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class PoliceChaseConfiguration : IValidateConfiguration<PoliceChaseConfigurationValidator>
{
    public bool Enabled { get; set; } = true;
    public string PoliceCarModel { get; set; } = "";
    public float MaxPoliceSpeedKph { get; set; } = 280;
    public float FarDistanceMeters { get; set; } = 800;
    public float MediumDistanceMeters { get; set; } = 400;
    public float NearDistanceMeters { get; set; } = 150;
    public float FarSpeedBonusKph { get; set; } = 60;
    public float MediumSpeedBonusKph { get; set; } = 40;
    public float NearSpeedBonusKph { get; set; } = 20;
    public float LostDistanceMeters { get; set; } = 2000;
    public bool Debug { get; set; } = true;
    public int DebugIntervalMs { get; set; } = 1000;
}
```

Create a compile-only stub at `src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs` so the generic type resolves:

```csharp
using FluentValidation;
using JetBrains.Annotations;

namespace PoliceChasePlugin;

[UsedImplicitly]
public class PoliceChaseConfigurationValidator : AbstractValidator<PoliceChaseConfiguration>
{
}
```

- [ ] **Step 4: Run GREEN for defaults**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter NewConfigurationUsesP0Defaults
```

Expected: 1 passed, 0 failed.

- [ ] **Step 5: Write failing validator tests**

Create `tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs`:

```csharp
namespace PoliceChasePlugin.Tests;

[TestFixture]
public class PoliceChaseConfigurationValidatorTests
{
    private PoliceChaseConfigurationValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new PoliceChaseConfigurationValidator();
    }

    [Test]
    public void AcceptsValidP0Configuration()
    {
        var result = _validator.Validate(new PoliceChaseConfiguration());

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RejectsNonPositiveMaximumPoliceSpeed(float speed)
    {
        var configuration = new PoliceChaseConfiguration { MaxPoliceSpeedKph = speed };

        var result = _validator.Validate(configuration);

        Assert.That(result.Errors, Has.Some.Property("PropertyName").EqualTo(nameof(PoliceChaseConfiguration.MaxPoliceSpeedKph)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RejectsNonPositiveDebugInterval(int interval)
    {
        var configuration = new PoliceChaseConfiguration { DebugIntervalMs = interval };

        var result = _validator.Validate(configuration);

        Assert.That(result.Errors, Has.Some.Property("PropertyName").EqualTo(nameof(PoliceChaseConfiguration.DebugIntervalMs)));
    }

    [TestCase(150, 150, 800, 2000, "NearDistanceMeters")]
    [TestCase(150, 800, 800, 2000, "MediumDistanceMeters")]
    [TestCase(150, 400, 2000, 2000, "FarDistanceMeters")]
    [TestCase(150, 400, 800, 0, "LostDistanceMeters")]
    [TestCase(0, 400, 800, 2000, "NearDistanceMeters")]
    public void RejectsInvalidDistanceOrder(float near, float medium, float far, float lost, string expectedProperty)
    {
        var configuration = new PoliceChaseConfiguration
        {
            NearDistanceMeters = near,
            MediumDistanceMeters = medium,
            FarDistanceMeters = far,
            LostDistanceMeters = lost
        };

        var result = _validator.Validate(configuration);

        Assert.That(result.Errors, Has.Some.Property("PropertyName").EqualTo(expectedProperty));
    }

    [TestCase(-1, 40, 20, "FarSpeedBonusKph")]
    [TestCase(60, -1, 20, "MediumSpeedBonusKph")]
    [TestCase(60, 40, -1, "NearSpeedBonusKph")]
    public void RejectsNegativeSpeedBonus(float far, float medium, float near, string expectedProperty)
    {
        var configuration = new PoliceChaseConfiguration
        {
            FarSpeedBonusKph = far,
            MediumSpeedBonusKph = medium,
            NearSpeedBonusKph = near
        };

        var result = _validator.Validate(configuration);

        Assert.That(result.Errors, Has.Some.Property("PropertyName").EqualTo(expectedProperty));
    }
}
```

- [ ] **Step 6: Run RED for missing validation rules**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter PoliceChaseConfigurationValidatorTests
```

Expected: `AcceptsValidP0Configuration` passes; all invalid-input cases fail because the stub validator accepts them.

- [ ] **Step 7: Implement validation rules**

Replace `PoliceChaseConfigurationValidator.cs` with:

```csharp
using FluentValidation;
using JetBrains.Annotations;

namespace PoliceChasePlugin;

[UsedImplicitly]
public class PoliceChaseConfigurationValidator : AbstractValidator<PoliceChaseConfiguration>
{
    public PoliceChaseConfigurationValidator()
    {
        RuleFor(configuration => configuration.MaxPoliceSpeedKph).GreaterThan(0);
        RuleFor(configuration => configuration.NearDistanceMeters)
            .GreaterThan(0)
            .LessThan(configuration => configuration.MediumDistanceMeters);
        RuleFor(configuration => configuration.MediumDistanceMeters)
            .GreaterThan(0)
            .LessThan(configuration => configuration.FarDistanceMeters);
        RuleFor(configuration => configuration.FarDistanceMeters)
            .GreaterThan(0)
            .LessThan(configuration => configuration.LostDistanceMeters);
        RuleFor(configuration => configuration.LostDistanceMeters).GreaterThan(0);
        RuleFor(configuration => configuration.FarSpeedBonusKph).GreaterThanOrEqualTo(0);
        RuleFor(configuration => configuration.MediumSpeedBonusKph).GreaterThanOrEqualTo(0);
        RuleFor(configuration => configuration.NearSpeedBonusKph).GreaterThanOrEqualTo(0);
        RuleFor(configuration => configuration.DebugIntervalMs).GreaterThan(0);
    }
}
```

- [ ] **Step 8: Run GREEN for all configuration tests**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter "FullyQualifiedName~PoliceChaseConfigurationTests|FullyQualifiedName~PoliceChaseConfigurationValidatorTests"
```

Expected: all configuration tests pass with 0 failures.

- [ ] **Step 9: Commit and push configuration**

Run:

```powershell
git add src/PoliceChasePlugin/PoliceChaseConfiguration.cs src/PoliceChasePlugin/PoliceChaseConfigurationValidator.cs tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationTests.cs tests/PoliceChasePlugin.Tests/PoliceChaseConfigurationValidatorTests.cs
git commit -m "feat: add P0 police chase configuration"
git push origin main
```

---

### Task 3: Plugin lifecycle service and Autofac module

**Files:**
- Create: `tests/PoliceChasePlugin.Tests/TestHostApplicationLifetime.cs`
- Create: `tests/PoliceChasePlugin.Tests/CollectingLogSink.cs`
- Create: `tests/PoliceChasePlugin.Tests/PoliceChaseServiceTests.cs`
- Create: `tests/PoliceChasePlugin.Tests/PoliceChaseModuleTests.cs`
- Create: `src/PoliceChasePlugin/PoliceChaseService.cs`
- Create: `src/PoliceChasePlugin/PoliceChaseModule.cs`

**Interfaces:**
- Consumes: `PoliceChaseConfiguration`, `IHostApplicationLifetime`, Serilog `Log`, Autofac, and `IAssettoServerAutostart`.
- Produces: `PoliceChaseService.StartAsync/StopAsync` behavior and `PoliceChaseModule` registration as a singleton autostart service.

- [ ] **Step 1: Add real test support for host lifetime and captured Serilog events**

Create `tests/PoliceChasePlugin.Tests/TestHostApplicationLifetime.cs`:

```csharp
using Microsoft.Extensions.Hosting;

namespace PoliceChasePlugin.Tests;

internal sealed class TestHostApplicationLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();

    public CancellationToken ApplicationStarted => _started.Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => _stopped.Token;

    public void StopApplication() => _stopping.Cancel();

    public void Dispose()
    {
        _started.Dispose();
        _stopping.Dispose();
        _stopped.Dispose();
    }
}
```

Create `tests/PoliceChasePlugin.Tests/CollectingLogSink.cs`:

```csharp
using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace PoliceChasePlugin.Tests;

internal sealed class CollectingLogSink : ILogEventSink
{
    public ConcurrentQueue<LogEvent> Events { get; } = new();

    public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);

    public bool ContainsMessage(string message) =>
        Events.Any(logEvent => logEvent.RenderMessage() == message);
}
```

- [ ] **Step 2: Write failing lifecycle log tests**

Create `tests/PoliceChasePlugin.Tests/PoliceChaseServiceTests.cs`:

```csharp
using Serilog;

namespace PoliceChasePlugin.Tests;

[TestFixture]
[NonParallelizable]
public class PoliceChaseServiceTests
{
    private CollectingLogSink _sink = null!;
    private TestHostApplicationLifetime _lifetime = null!;

    [SetUp]
    public void SetUp()
    {
        _sink = new CollectingLogSink();
        _lifetime = new TestHostApplicationLifetime();
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(_sink).CreateLogger();
    }

    [TearDown]
    public void TearDown()
    {
        Log.CloseAndFlush();
        _lifetime.Dispose();
    }

    [Test]
    public async Task EnabledServiceLogsInitializationAndShutdown()
    {
        using var service = new PoliceChaseService(
            new PoliceChaseConfiguration { Enabled = true },
            _lifetime);

        await service.StartAsync(CancellationToken.None);
        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await service.StopAsync(stopTimeout.Token);

        Assert.Multiple(() =>
        {
            Assert.That(_sink.ContainsMessage("[PoliceChase] Plugin initialized"), Is.True);
            Assert.That(_sink.ContainsMessage("[PoliceChase] Plugin stopping"), Is.True);
        });
    }

    [Test]
    public async Task DisabledServiceLogsDisabledStateWithoutInitialization()
    {
        using var service = new PoliceChaseService(
            new PoliceChaseConfiguration { Enabled = false },
            _lifetime);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_sink.ContainsMessage("[PoliceChase] Plugin disabled by configuration"), Is.True);
            Assert.That(_sink.ContainsMessage("[PoliceChase] Plugin initialized"), Is.False);
        });
    }
}
```

- [ ] **Step 3: Run RED for missing service**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter PoliceChaseServiceTests
```

Expected: compile failure because `PoliceChaseService` does not exist.

- [ ] **Step 4: Implement minimal lifecycle service**

Create `src/PoliceChasePlugin/PoliceChaseService.cs`:

```csharp
using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Services;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace PoliceChasePlugin;

public sealed class PoliceChaseService : CriticalBackgroundService, IAssettoServerAutostart
{
    private readonly PoliceChaseConfiguration _configuration;

    public PoliceChaseService(
        PoliceChaseConfiguration configuration,
        IHostApplicationLifetime applicationLifetime) : base(applicationLifetime)
    {
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.Enabled)
        {
            Log.Information("[PoliceChase] Plugin disabled by configuration");
            return;
        }

        Log.Information("[PoliceChase] Plugin initialized");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Log.Information("[PoliceChase] Plugin stopping");
        }
    }
}
```

- [ ] **Step 5: Run GREEN for lifecycle logs**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter PoliceChaseServiceTests
```

Expected: 2 passed, 0 failed.

- [ ] **Step 6: Write failing module registration test**

Create `tests/PoliceChasePlugin.Tests/PoliceChaseModuleTests.cs`:

```csharp
using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;

namespace PoliceChasePlugin.Tests;

[TestFixture]
public class PoliceChaseModuleTests
{
    [Test]
    public void RegistersOneSharedAutostartService()
    {
        using var lifetime = new TestHostApplicationLifetime();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(new PoliceChaseConfiguration());
        builder.RegisterInstance<IHostApplicationLifetime>(lifetime);
        builder.RegisterModule(new PoliceChaseModule());

        using var container = builder.Build();
        var byType = container.Resolve<PoliceChaseService>();
        var autostartServices = container.Resolve<IEnumerable<IAssettoServerAutostart>>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(autostartServices, Has.Length.EqualTo(1));
            Assert.That(autostartServices[0], Is.SameAs(byType));
        });
    }
}
```

- [ ] **Step 7: Run RED for missing module**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter PoliceChaseModuleTests
```

Expected: compile failure because `PoliceChaseModule` does not exist.

- [ ] **Step 8: Implement module registration**

Create `src/PoliceChasePlugin/PoliceChaseModule.cs`:

```csharp
using AssettoServer.Server.Plugin;
using Autofac;

namespace PoliceChasePlugin;

public sealed class PoliceChaseModule : AssettoServerModule<PoliceChaseConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<PoliceChaseService>()
            .AsSelf()
            .As<IAssettoServerAutostart>()
            .SingleInstance();
    }
}
```

- [ ] **Step 9: Run GREEN for all service/module tests**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter "FullyQualifiedName~PoliceChaseServiceTests|FullyQualifiedName~PoliceChaseModuleTests"
dotnet test PoliceChasePlugin.sln --no-restore
```

Expected: lifecycle/module tests pass, then the complete suite passes with 0 failures.

- [ ] **Step 10: Commit and push lifecycle**

Run:

```powershell
git add src/PoliceChasePlugin/PoliceChaseService.cs src/PoliceChasePlugin/PoliceChaseModule.cs tests/PoliceChasePlugin.Tests/TestHostApplicationLifetime.cs tests/PoliceChasePlugin.Tests/CollectingLogSink.cs tests/PoliceChasePlugin.Tests/PoliceChaseServiceTests.cs tests/PoliceChasePlugin.Tests/PoliceChaseModuleTests.cs
git commit -m "feat: add minimal plugin lifecycle"
git push origin main
```

---

### Task 4: Installation documentation, package verification, and P0.2 gate

**Files:**
- Create: `docs/P0.2-plugin-minimo.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: Release artifact `artifacts/PoliceChasePlugin/PoliceChasePlugin.dll`.
- Produces: exact operator instructions and final evidence that P0.2 meets its build/test/package boundary.

- [ ] **Step 1: Write the operator documentation**

Create `docs/P0.2-plugin-minimo.md` with these complete sections and commands:

````markdown
# Police Chase P0.2 — plugin mínimo

## Build

```powershell
git submodule update --init --recursive
dotnet restore PoliceChasePlugin.sln
dotnet test PoliceChasePlugin.sln --no-restore
dotnet build src/PoliceChasePlugin/PoliceChasePlugin.csproj -c Release --no-restore
```

O artefato instalável será criado em `artifacts/PoliceChasePlugin/PoliceChasePlugin.dll`.

## Instalação manual

1. Crie `<AssettoServer>/plugins/PoliceChasePlugin/`.
2. Copie `PoliceChasePlugin.dll` para essa pasta.
3. Em `extra_cfg.yml`, acrescente `PoliceChasePlugin` à lista existente sem remover os plugins atuais:

```yaml
EnablePlugins:
  - PoliceChasePlugin
```

4. No final do mesmo arquivo, acrescente outro documento YAML:

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

Não substitua a lista `EnablePlugins` existente e não remova `RandomWeatherPlugin` nem outros plugins.

## Validação no servidor

Com `Enabled: true`, procure:

```text
Loaded plugin PoliceChasePlugin
[PoliceChase] Plugin initialized
```

Ao encerrar normalmente o servidor, procure:

```text
[PoliceChase] Plugin stopping
```

Com `Enabled: false`, procure:

```text
Loaded plugin PoliceChasePlugin
[PoliceChase] Plugin disabled by configuration
```

Confirme também que AI Traffic, RandomWeatherPlugin, WeatherFX, RainFX, Overtake e o ciclo de tempo continuam sem novos erros. A P0.2 não detecta jogadores e não controla nenhuma AI.
````

- [ ] **Step 2: Link P0.2 from the README**

Add under `## Prova de conceito P0` in `README.md`:

```markdown
- [P0.2 — Plugin mínimo, build e instalação](docs/P0.2-plugin-minimo.md)
```

- [ ] **Step 3: Run full automated verification**

Run:

```powershell
dotnet restore PoliceChasePlugin.sln
dotnet test PoliceChasePlugin.sln --no-restore
dotnet build PoliceChasePlugin.sln -c Release --no-restore
git diff --check
```

Expected: restore succeeds; all tests pass; Release build succeeds with 0 errors; diff check emits no errors.

- [ ] **Step 4: Verify exact dependency and package contents**

Run:

```powershell
$expectedCommit = '51737e2c2ee892bc7800df479517eaaf38a828fc'
$actualCommit = git -C external/AssettoServer rev-parse HEAD
if ($actualCommit -ne $expectedCommit) { throw "AssettoServer commit mismatch: $actualCommit" }

$packageFiles = @(Get-ChildItem artifacts/PoliceChasePlugin -File)
if ($packageFiles.Count -ne 1 -or $packageFiles[0].Name -ne 'PoliceChasePlugin.dll') {
    throw "Unexpected plugin package contents: $($packageFiles.Name -join ', ')"
}
```

Expected: command exits successfully with no exception.

- [ ] **Step 5: Verify scope boundary**

Run:

```powershell
rg -n "EntryCarManager|AiState|AiSpline|ClientConnected|CHASE|Wanted|Heat" src tests
```

Expected: no matches. P0.3+ behavior has not leaked into P0.2.

- [ ] **Step 6: Commit and push documentation**

Run:

```powershell
git add README.md docs/P0.2-plugin-minimo.md
git commit -m "docs: add P0.2 build and installation guide"
git push origin main
```

- [ ] **Step 7: Final repository verification**

Run:

```powershell
git status --short --branch
git rev-parse HEAD
git ls-remote origin refs/heads/main
```

Expected: clean `main...origin/main`; local and remote hashes are identical. Stop and request operator validation before beginning P0.3.
