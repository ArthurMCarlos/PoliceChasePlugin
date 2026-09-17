# Police Chase P0.4 Dedicated AI Reservation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep exactly one native `AiState` permanently reserved for `PoliceCarSessionId` while leaving the ten dynamic traffic slots and two player slots unchanged.

**Architecture:** Add an opt-in minimum overbooking invariant to `EntryCar`, then make `AiBehavior` distribute its dynamic target only across slots without a reservation. The plugin marks only the validated Session ID with minimum and maximum `1`; the native `AiBehavior` remains responsible for finding a safe spawn point and initializing the state.

**Tech Stack:** C# 12 / .NET 8, NUnit 3.14, Autofac 7.1, AssettoServer v0.0.54, Git submodule, GitHub fork.

**Spec:** `docs/superpowers/specs/2026-09-17-police-chase-p0-4-dedicated-ai-reservation-design.md`

## Global Constraints

- Base the core change only on AssettoServer commit `51737e2c2ee892bc7800df479517eaaf38a828fc` (`v0.0.54`).
- Preserve `CAR_0..CAR_9` as the dynamic traffic pool.
- Do not affect player slots `CAR_10` and `CAR_11`.
- Reserve exactly one state for the configured `PoliceCarSessionId`; never fall back to another slot.
- Do not change `AiPerPlayerTargetCount`, `MaxAiTargetCount`, `TrafficDensity`, or `MaxSpeedKph`.
- Do not add pursuit, dynamic speed, spline control, pathfinding, teleport commands, Heat, Wanted, HUD, or siren behavior.
- Do not use reflection or private-member access.
- Do not edit server configuration files automatically.
- Preserve the pre-existing candidate commit `e31fa4a050bd75de4d597cc5a802a0fb28f71f5e` and the existing local plugin edit until they are either validated and incorporated or explicitly rejected.
- Publish every accepted project change to GitHub; a submodule commit must exist in an accessible remote before the parent repository points to it.

---

### Task 1: Prove the core regression and validate the existing candidate

**Files:**
- Existing candidate: `external/AssettoServer/AssettoServer/Server/Ai/AiOverbookingPolicy.cs`
- Existing candidate: `external/AssettoServer/AssettoServer/Server/Ai/AiBehavior.cs:437-468`
- Existing candidate: `external/AssettoServer/AssettoServer/Server/EntryCarAi.cs:36-43,412-441`
- Existing candidate test: `external/AssettoServer/AssettoServer.Tests/Server/Ai/AiOverbookingPolicyTests.cs`

**Interfaces:**
- Produces: `EntryCar.AiMinOverbooking`, default `0`.
- Produces: `AiOverbookingPolicy.ClampTarget(int requested, int minimum, int? maximum)`.
- Produces: `AiOverbookingPolicy.CalculateTargets(int dynamicTargetCount, IReadOnlyList<int> minimums)`.
- Preserves: the original no-reservation distribution.

- [ ] **Step 1: Preserve and identify the existing candidate**

Run:

```powershell
git -C external/AssettoServer status --short --branch
git -C external/AssettoServer rev-parse HEAD
git -C external/AssettoServer diff 51737e2c2ee892bc7800df479517eaaf38a828fc..e31fa4a050bd75de4d597cc5a802a0fb28f71f5e --check
```

Expected: branch `codex/p0.4-dedicated-police-ai`, HEAD `e31fa4a...`, and no whitespace errors. Do not reset, amend, or discard this commit.

- [ ] **Step 2: Reconstruct RED in a disposable submodule worktree**

Create a disposable worktree from the unmodified v0.0.54 commit, apply only the candidate test file, and run that test:

```powershell
$redPath = Join-Path ([System.IO.Path]::GetTempPath()) 'AssettoServer-p0.4-red'
if (Test-Path -LiteralPath $redPath) { throw "Disposable RED path already exists: $redPath" }
git -C external/AssettoServer worktree add $redPath 51737e2c2ee892bc7800df479517eaaf38a828fc
$testPatch = git -C external/AssettoServer diff 51737e2c2ee892bc7800df479517eaaf38a828fc..e31fa4a050bd75de4d597cc5a802a0fb28f71f5e -- AssettoServer.Tests/Server/Ai/AiOverbookingPolicyTests.cs
$testPatch | git -C $redPath apply -
dotnet test "$redPath/AssettoServer.Tests/AssettoServer.Tests.csproj" --filter AiOverbookingPolicyTests
```

Expected RED: compilation fails because `AiOverbookingPolicy` and the minimum-reservation behavior do not exist in v0.0.54.

After recording the RED evidence, remove only the verified disposable worktree:

```powershell
git -C external/AssettoServer worktree remove $redPath
```

- [ ] **Step 3: Inspect the minimal core implementation**

Confirm the candidate contains exactly these behaviors:

```csharp
public int AiMinOverbooking { get; set; }
```

```csharp
count = AiOverbookingPolicy.ClampTarget(
    count,
    AiMinOverbooking,
    AiMaxOverbooking);
```

`CalculateTargets` must assign each positive minimum directly and distribute `dynamicTargetCount` only among entries whose minimum is zero. `AiBehavior.AdjustOverbooking()` must calculate its per-player cap using only that dynamic-slot count.

- [ ] **Step 4: Add guard-rail coverage for the inherited candidate**

Append these tests to `AiOverbookingPolicyTests.cs` so every validation requirement in the spec is executable:

```csharp
[TestCase(-1, 0, null)]
[TestCase(0, -1, null)]
[TestCase(0, 0, -1)]
public void ClampTargetRejectsNegativeInputs(int requested, int minimum, int? maximum)
{
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        AiOverbookingPolicy.ClampTarget(requested, minimum, maximum));
}

[Test]
public void CalculateTargetsRejectsNegativeDynamicTarget()
{
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        AiOverbookingPolicy.CalculateTargets(-1, new[] { 0 }));
}

[Test]
public void CalculateTargetsRejectsNegativeMinimum()
{
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        AiOverbookingPolicy.CalculateTargets(0, new[] { -1 }));
}

[Test]
public void ReservedOnlyAllocationSurvivesWithoutPlayers()
{
    var targets = AiOverbookingPolicy.CalculateTargets(0, new[] { 1 });

    Assert.That(targets, Is.EqualTo(new[] { 1 }));
}
```

These tests verify already-present candidate behavior; they do not authorize new production behavior beyond the approved spec.

- [ ] **Step 5: Run GREEN for the policy**

Run:

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --filter AiOverbookingPolicyTests
```

Expected: all policy tests pass, including slot-position independence, unchanged legacy distribution, negative-input rejection, and reservation without players.

- [ ] **Step 6: Commit the additional core tests**

Run:

```powershell
git -C external/AssettoServer add AssettoServer.Tests/Server/Ai/AiOverbookingPolicyTests.cs
git -C external/AssettoServer commit -m "test(ai): cover dedicated reservation guards"
```

Expected: a follow-up commit on `codex/p0.4-dedicated-police-ai`; do not amend the preserved candidate commit.

- [ ] **Step 7: Run the full AssettoServer test project**

Run:

```powershell
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
```

Expected: all AssettoServer tests pass. If any failure is caused by the candidate, stop and create a new failing regression test before modifying production code.

- [ ] **Step 8: Verify the core scope boundary**

Run:

```powershell
git -C external/AssettoServer diff --name-only 51737e2c2ee892bc7800df479517eaaf38a828fc..HEAD
git -C external/AssettoServer diff 51737e2c2ee892bc7800df479517eaaf38a828fc..HEAD | rg -n 'TargetSpeed|MaxSpeedKph|WorldToSpline|JunctionEvaluator|Wanted|Heat|SetAiControl\(false\)'
```

Expected: only the policy, overbooking lifecycle, and policy tests changed; the forbidden scan produces no matches.

---

### Task 2: Reserve exactly one state from the plugin

**Files:**
- Modify: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs:31-46`
- Test: `tests/PoliceChasePlugin.Tests/Ai/PoliceAiServiceTests.cs`
- Test utility: `tests/PoliceChasePlugin.Tests/Ai/FakePoliceAiSlotSource.cs`

**Interfaces:**
- Consumes: `EntryCar.AiMinOverbooking` from Task 1.
- Preserves: `IPoliceAiSlotSource.PrepareSingleState(byte sessionId)`.
- Produces: exact runtime bounds `AiMinOverbooking = 1` and `AiMaxOverbooking = 1` on the selected slot.

- [ ] **Step 1: Verify the existing service regression coverage**

Run the focused suite before accepting the adapter edit:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter PoliceAiServiceTests
```

Expected: the test `PreparesOnlyConfiguredFixedSlot` proves that only the configured Session ID is passed to `PrepareSingleState`; missing, non-`FIXED`, mismatched-model, zero-state, and multi-state cases remain rejected.

- [ ] **Step 2: Apply the minimum reservation only to the resolved slot**

The real adapter must contain this exact order after resolving a single Session ID:

```csharp
var slot = _entryCarManager.EntryCars.Single(car => car.SessionId == sessionId);
slot.AiMinOverbooking = 1;
slot.AiMaxOverbooking = 1;
slot.SetAiControl(true);
slot.SetAiOverbooking(1);
```

Do not set the minimum through `CarSpecificOverrides`, because that configuration is model-wide rather than Session-ID-specific.

- [ ] **Step 3: Run GREEN for plugin AI policy and lifecycle**

Run:

```powershell
dotnet test tests/PoliceChasePlugin.Tests/PoliceChasePlugin.Tests.csproj --no-restore --filter "FullyQualifiedName~PoliceAiServiceTests|FullyQualifiedName~PoliceChaseServiceTests|FullyQualifiedName~PoliceChaseModuleTests"
```

Expected: all AI selection and lifecycle tests pass.

- [ ] **Step 4: Run the complete plugin suite**

Run:

```powershell
dotnet test PoliceChasePlugin.sln --no-restore
```

Expected: all existing 35 tests pass with no regressions.

---

### Task 3: Publish the modified core and pin the accessible commit

**Files:**
- Modify: `.gitmodules`
- Modify gitlink: `external/AssettoServer`
- Modify: `src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs`

**Interfaces:**
- Publishes: `ArthurMCarlos/AssettoServer` as the accessible source for the modified core.
- Publishes: branch `codex/p0.4-dedicated-police-ai` containing `e31fa4a...` or a tested follow-up commit.
- Pins: the parent repository to the exact published core commit.

- [ ] **Step 1: Create the GitHub fork**

The repository `https://github.com/ArthurMCarlos/AssettoServer` does not currently exist. Create it as a fork of `compujuckel/AssettoServer`:

```powershell
gh repo fork compujuckel/AssettoServer --clone=false --remote=false
```

Expected: GitHub reports the fork at `https://github.com/ArthurMCarlos/AssettoServer`.

- [ ] **Step 2: Publish the tested core branch**

Run:

```powershell
git -C external/AssettoServer remote add arthur https://github.com/ArthurMCarlos/AssettoServer.git
git -C external/AssettoServer push -u arthur codex/p0.4-dedicated-police-ai
git ls-remote https://github.com/ArthurMCarlos/AssettoServer.git refs/heads/codex/p0.4-dedicated-police-ai
```

If the `arthur` remote already exists, verify that its URL is exactly the fork URL instead of adding a duplicate. Expected: the remote branch resolves to the tested core HEAD.

- [ ] **Step 3: Point future submodule clones to the fork**

Change `.gitmodules` to:

```ini
[submodule "external/AssettoServer"]
	path = external/AssettoServer
	url = https://github.com/ArthurMCarlos/AssettoServer.git
```

Synchronize the local metadata without changing the checked-out commit:

```powershell
git submodule sync -- external/AssettoServer
```

- [ ] **Step 4: Verify the parent points to the published commit**

Run:

```powershell
$coreCommit = git -C external/AssettoServer rev-parse HEAD
$remoteCommit = (git ls-remote https://github.com/ArthurMCarlos/AssettoServer.git refs/heads/codex/p0.4-dedicated-police-ai).Split("`t")[0]
if ($coreCommit -ne $remoteCommit) { throw "Core commit is not published: local=$coreCommit remote=$remoteCommit" }
git diff --submodule=log -- external/AssettoServer
```

Expected: local and remote hashes match; the parent diff advances the gitlink from `51737e2...` to the tested P0.4 core commit.

- [ ] **Step 5: Commit and push the parent integration**

Stage only the intended integration files:

```powershell
git add .gitmodules external/AssettoServer src/PoliceChasePlugin/Ai/AssettoServerPoliceAiSlotSource.cs
git commit -m "feat: reserve dedicated police AI state"
git push origin main
```

Expected: `origin/main` contains the exact plugin edit, fork URL, and published submodule pointer.

---

### Task 4: Document deployment and real-server validation

**Files:**
- Create: `docs/P0.4-reserva-ai-dedicada.md`
- Modify: `README.md`

**Interfaces:**
- Produces: exact build/deployment distinction between the modified AssettoServer core and `PoliceChasePlugin.dll`.
- Produces: expected P0.4 log and server acceptance checklist.

- [ ] **Step 1: Write the operator guide**

Create `docs/P0.4-reserva-ai-dedicada.md` with these required facts:

```markdown
# Police Chase P0.4 — reserva AI dedicada

## O que muda

A P0.4 requer o core AssettoServer apontado pelo submodule deste projeto. Atualizar somente `PoliceChasePlugin.dll` não instala a nova política de reserva.

O `CAR_12` passa a manter exatamente um `AiState` fora da distribuição dinâmica. O spawn ainda é nativo: ele ocorre depois que um jogador termina de carregar e o `AiBehavior` encontra um ponto seguro.

## Resultado esperado do cálculo

Com dez slots de tráfego, um policial reservado e um jogador:

```text
No. AI Slots: 10
Reserved AI Slots: 1
Target AI count: 10
Overbooking: 1
Rest: 0
```

O resultado final é dez instâncias de tráfego mais uma instância policial.

## Validação

1. Confirme que o servidor executa o commit do core fixado no submodule.
2. Confirme `PoliceCarSessionId: 12` e `AI=FIXED` no `CAR_12`.
3. Inicie o servidor e conecte um jogador.
4. Aguarde o jogador terminar de carregar e alguns ciclos do `AiBehavior`.
5. Verifique que `CAR_0..CAR_9` continuam ativos.
6. Verifique que o `CAR_12` aparece e não desaparece após novos recálculos de overbooking.
7. Verifique que `CAR_10` e `CAR_11` continuam disponíveis para jogadores.
8. Verifique RandomWeatherPlugin, AI Traffic e colisões normais.

A P0.4 ainda não implementa perseguição, velocidade dinâmica, sirene, HUD, Heat, Wanted ou pathfinding próprio.
```

- [ ] **Step 2: Link the guide from README**

Add:

```markdown
- [P0.4 — Reserva AI dedicada](docs/P0.4-reserva-ai-dedicada.md)
```

- [ ] **Step 3: Run fresh full verification**

Run:

```powershell
dotnet restore external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj
dotnet test external/AssettoServer/AssettoServer.Tests/AssettoServer.Tests.csproj --no-restore
dotnet build external/AssettoServer/AssettoServer/AssettoServer.csproj -c Release --no-restore
dotnet restore PoliceChasePlugin.sln
dotnet test PoliceChasePlugin.sln --no-restore
dotnet build PoliceChasePlugin.sln -c Release --no-restore
git diff --check
```

Expected: both complete test suites pass; Release build has zero errors; diff check is clean.

- [ ] **Step 4: Verify package and scope**

Run:

```powershell
$packageFiles = @(Get-ChildItem artifacts/PoliceChasePlugin -File)
if ($packageFiles.Count -ne 1 -or $packageFiles[0].Name -ne 'PoliceChasePlugin.dll') {
    throw "Unexpected plugin package contents: $($packageFiles.Name -join ', ')"
}

$forbidden = @(git -C external/AssettoServer diff 51737e2c2ee892bc7800df479517eaaf38a828fc..HEAD -- AssettoServer/Server/Ai AssettoServer/Server/EntryCarAi.cs | rg -n 'TargetSpeed|MaxSpeedKph|WorldToSpline|JunctionEvaluator|Wanted|Heat')
if ($forbidden.Count -gt 0) { throw "P0.5+ scope detected: $($forbidden -join '; ')" }
```

Expected: the plugin package contains only `PoliceChasePlugin.dll`; no pursuit or speed scope appears in the P0.4 patch.

- [ ] **Step 5: Commit and push documentation**

Run:

```powershell
git add README.md docs/P0.4-reserva-ai-dedicada.md
git commit -m "docs: add P0.4 server validation guide"
git push origin main
```

- [ ] **Step 6: Verify all remote synchronization**

Run:

```powershell
$parentLocal = git rev-parse HEAD
$parentRemote = (git ls-remote origin refs/heads/main).Split("`t")[0]
$coreLocal = git -C external/AssettoServer rev-parse HEAD
$coreRemote = (git ls-remote https://github.com/ArthurMCarlos/AssettoServer.git refs/heads/codex/p0.4-dedicated-police-ai).Split("`t")[0]
if ($parentLocal -ne $parentRemote) { throw "Parent repository is not synchronized" }
if ($coreLocal -ne $coreRemote) { throw "Core fork is not synchronized" }
git status --short --branch
```

Expected: both repositories match their remotes and the parent worktree is clean. Stop before pursuit behavior and request the real-server P0.4 validation.
