# AntiDuplPlus

GPU-accelerated duplicate/similar image finder (fork of AntiDupl.NET). **Windows x64 only.** Version comes from `src/version.txt` (currently **2.6.0**); do not hardcode it elsewhere.

Trust **code** over long notes in `PROJECT_CONTEXT.md` / `IMPLEMENTATION_PLAN.md` if they conflict.

## Architecture

Solution: `src/AntiDupl.sln` — 5 projects (solution-level build order derived from `.sln` dependency sections):

1. **AntiDupl** (C++/CUDA DLL) → `src/AntiDupl/` — image processing, GPU kernels, DB I/O, exports
2. **NvJpegCollector** (C++/CUDA exe, **Release|x64 only** — no Debug/Publish configs) → `src/NvJpegCollector/` — GPU DB collector (`index.adi` + `0000.adi`)
3. **AntiDupl.NET.Core** (C# net8.0) → `src/AntiDupl.NET.Core/` — P/Invoke bindings
4. **AntiDupl.NET.WinForms** (C# net8.0-windows) → `src/AntiDupl.NET.WinForms/` — **main GUI**
5. **AntiDupl.NET.WPF** (net8.0-windows) — exists in solution; less maintained than WinForms

`.sln` dependency graph: `Core → AntiDupl`, `WinForms → Core`, `WPF → AntiDupl+Core`; `NvJpegCollector` independent. `src/AntiDuplCore/` is an **orphan static-lib project not in the solution** (experimental shared-core attempt; references CUDA 12.8 paths); nothing consumes it — do not build or extend it for product tasks.

**Fork product model** (differs from original AntiDupl.NET): databases are **not** one built-in store. Any number of attachable DBs live under `databases/<Name>/`, created/updated by the **standalone `NvJpegCollector.exe`** (GPU decode) which the GUI launches and controls (`MainMenu.cs` → `Application.StartupPath + "NvJpegCollector.exe"`; Database Manager UI). The DLL reads these DBs (`TSearcher::LoadDatabase`, format auto-detect) and can compare **across multiple DBs** using pool modes (`adCompareOptions.poolCompareMode`: `Pool1Internal`/`Pool2Internal`/`Cross`/`All`). Moved image folders are re-mapped to an existing DB without rebuilding via remap (`RemapPath`, case-insensitive prefix + separator boundary; exposed in Database Manager UI). Consequence: the DLL itself does **no** GPU JPEG decoding (see NVJPEG notes).

## Repo map

| Path | Role |
|------|------|
| `src/AntiDupl/` | Native core DLL |
| `src/NvJpegCollector/` | Standalone collector utility |
| `src/AntiDupl.NET.Core/` | Managed bindings (`CoreLib`, `Original/CoreDll`) |
| `src/AntiDupl.NET.WinForms/` | Primary UI |
| `src/AntiDupl.NET.WPF/` | Secondary UI |
| `src/AntiDuplCore/` | **Orphan** project, not in solution (see above) |
| `tests/AntiDupl.Contract.Tests/` | Interop contract regression tests (console runner, not in solution) |
| `data/resources/` | Runtime strings/resources (copied post-build) |
| `docs/` | Original website + help files; `docs/data/resources/` is **also copied post-build** by `CopyData.cmd` |
| `cmd/` | `Deploy.cmd`, `CopyData.cmd`, packaging (`MakeBin.cmd`, `MakePublish.cmd`, `MakeSrc.cmd`) |
| `bin/<Config>/` | Shared build output (gitignored) |
| `obj/<Config>/<ProjectName>/` | C++ intermediates |
| `out/` | Packaging output (gitignored) |
| `Audit/` | Audit reports (historical); root `Audit.md` = full review 2026-08-16 with 2026-08-18 fix status |
| `.agents/skills/karpathy/` | Engineering discipline for agents |

## Build

### Prerequisites
- Visual Studio 2022 (v143 toolset)
- CUDA Toolkit 13.1 (provides `nvjpeg64_13.dll` for NvJpegCollector link via `$(CUDA_PATH)`); 12.8 also installed — provides `cudart64_12.dll` (see “Binary imports” below for which DLL needs what)
- vcpkg (triplet: `x64-windows-static`) — deps in `src/vcpkg.json`
- .NET 8.0 SDK
- vcpkg **user-wide MSBuild integration** must be registered from this repo's location: run `vcpkg\vcpkg.exe integrate install` after moving the repo (see “Relocation quirks”)

### Commands

```bash
# DEPLOY for testing (recommended - does everything below + copies CUDA deps + verifies)
cmd\Deploy.cmd

# Full solution (slow, may trigger vcpkg manifest install)
msbuild src\AntiDupl.sln /p:Configuration=Release /p:Platform=x64

# Single C++ project (VS Developer Command Prompt)
msbuild src\AntiDupl\AntiDupl.vcxproj /p:Configuration=Release /p:Platform=x64 /m:1

# Single C# project (platform defaults to x64 via csproj Platforms)
dotnet build src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj /p:SolutionDir="src\\" -c Release
```

### Deploying for testing (MANDATORY)
After ANY code change, run **`cmd\Deploy.cmd`** and verify it ends with `[OK] Deploy complete.` before reporting the task done. It:
1. Builds `AntiDupl.dll` + `NvJpegCollector.exe` (C++) and `AntiDupl.NET.WinForms` (C#).
2. Copies CUDA runtime deps (`nvjpeg64_13.dll`, `cudart64_12.dll`; `cudart64_13.dll` if present) next to the exe.
3. Runs `cmd/CopyData.cmd` (resources, incl. `docs/data/resources/`).
4. Verifies all artifacts exist in `bin/Release/`.

**Rule: a task is NOT done until `cmd\Deploy.cmd` passes.** Test the GUI from `bin/Release/AntiDupl.NET.WinForms.exe` — that is the ONLY folder the GUI runs from and launches `NvJpegCollector.exe` from (`MainMenu.cs` uses `Application.StartupPath`).

### Configurations & output
- **Debug|x64**, **Release|x64**, **Publish|x64** (AntiDupl DLL and C# projects); NvJpegCollector: **Release only** (`.sln` maps its Debug/Publish to Release)
- Output: `bin/<Configuration>/` (shared by C++ and C#; gitignored). All C++ `OutDir`/`IntDir` are `$(ProjectDir)..\..\bin\` — independent of `$(SolutionDir)`, so single-project builds land in the same `bin/` as solution builds.
- Intermediates: `obj/<Configuration>/<ProjectName>/` (top-level `obj/`; note stray `src/bin/`, `src/obj/` from older runs — not used by current builds)
- vcpkg installs under `src/vcpkg_installed/x64-windows-static/`. First build can be long.
- CUDA code gen: SM 7.5 / 8.6 / 8.9 (`compute_75,sm_75; compute_86,sm_86; compute_89,sm_89`) via CUDA **12.8** BuildCustomizations (`adGPU.cu`).
- **vcpkg `simd` path quirk**: MSBuild may look under `.../x64-windows-static/x64-windows-static/include` while headers land in `.../include`. The repo already contains the nested `include/`+`lib/` copies (Simd headers + `.lib` duplicated into the nested path). If headers are missing, copy them into the nested path, then build with `/p:VcpkgManifestInstall=false` (as `Deploy.cmd` does). Details in `PROJECT_CONTEXT.md`.

### Relocation quirks (repo moved to a path containing spaces — verified fixed 2026-09-05)
Several call sites originally relied on a space-free path; they are now quote-safe — do not regress:
- `AntiDupl.vcxproj` PreBuild: `"$(ProjectDir)adExternal.cmd"` (was `"$(ProjectDir)".\adExternal.cmd` → pre-build exit 1)
- `AntiDupl.NET.Core.csproj` `GenerateSources` Exec: `&quot;$(MSBuildProjectDirectory)\External.cmd&quot; ...` (unquoted → MSB3073, cmd exit 9009)
- `AntiDupl.NET.WinForms.csproj` PostBuild CopyData Exec: same quoting fix
- `cmd/CopyData.cmd`: `set "SRC_DIR=%~1"` (bare `%1` → doubled quotes → robocopy exit 16)
- vcpkg MSBuild integration files in `%LOCALAPPDATA%\vcpkg\` store **absolute** paths (`vcpkg.path.txt`, `vcpkg.user.targets/props`). After any repo move, re-run `vcpkg\vcpkg.exe integrate install` — otherwise `AntiDupl.vcxproj` silently gets **no** vcpkg include paths and fails with `Simd/SimdLib.hpp: No such file or directory` (its `AdditionalIncludeDirectories` is empty; it fully relies on the integration).

### Binary imports (verified on built artifacts, 2026-09-05)
- `AntiDupl.dll` imports **only `cudart64_12.dll`** (CUDA 12.8 BuildCustomizations + `cudart.lib`)
- `NvJpegCollector.exe` imports **only `nvjpeg64_13.dll`** (linked via `$(CUDA_PATH)` = 13.1)
- Not needed: `nvjpeg64_12.dll`, `cudart64_13.dll`
- `dotnet publish` does **not** copy native P/Invoke DLLs — copy `AntiDupl.dll` + `cudart64_12.dll` + `nvjpeg64_13.dll` manually.

### CI
`.github/workflows/AntiDupl_CI.yml` — Debug/Release/Publish matrix on `windows-latest` with CUDA 12.8, vcpkg (pinned commit), solution build; Release job also runs `MakeBin.cmd`, Publish job runs native vcxproj build + WinForms single-file publish (`AntiDuplPublishSingleFile` profile) + `MakePublish.cmd`. Version tags (`v*`) create **draft** GitHub releases with VirusTotal scan. CI does **not** run `Deploy.cmd` or the contract tests.

## Hard invariants (do not break)

1. **Two incompatible `.adi` formats** — keep both code paths and on-disk layouts compatible:

   | | DLL-native | Collector-native |
   |---|---|---|
   | **Writers** | `adImageDataStorage.cpp` `SaveIndex` (`index.adi`, magic `"adii"`) + `SaveData` (data files, magic `"adid"`, name = hex of high 12 bits of key + `.adi`) | `NvJpegCollector/main.cpp` (raw `fwrite`, both full-save ~L954-1011 and `--update` path ~L804-857) |
   | **Header** | `"adii"` magic + version, then reducedImageSize | None (first `u32` = ThumbSize) |
   | **Reader** | `LoadIndex`/`LoadData` in `adImageDataStorage.cpp` | Auto-detect in `Load()` (L181-239) → `LoadCollectorNative()`; search entry: `TSearcher::LoadDatabase()` (`adSearcher.cpp:195`) |
   | **Created by** | CPU file scan / DLL save | GPU collector utility |

   **Do not change the raw fwrite layout in `main.cpp`** without updating loaders in the DLL. Collector clamps thumb size to [16..128] (C13 fix); both save paths write even when the record set is empty (C4 fix).

2. **Portable paths** — runtime data is relative to the exe:
   - `ad_database.xml` next to exe (registry code strips any directory part)
   - DBs under `databases/<Name>/` (`MainMenu.cs` combines `Application.StartupPath + "databases"`)
   - Prefer relative paths in registry/storage

3. **Interop layout** — `CoreDll` / exported C API structs must stay binary-compatible with `AntiDupl.dll` (`extern "C" __declspec(dllexport)`). Enforced by `tests/AntiDupl.Contract.Tests` — if you change a native struct/enum in `AntiDupl.h`, update the header **and** the tests in the same change.

4. **Surgical changes** — touch only what the task requires; no drive-by refactors. Prefer simplicity (see `.agents/skills/karpathy/SKILL.md`).

## C++ ↔ C# interop

- Low-level P/Invoke: `src/AntiDupl.NET.Core/Original/CoreDll.cs`
- High-level wrapper used by UI: `src/AntiDupl.NET.Core/CoreLib.cs`
- Native exports from `AntiDupl.dll` via `extern "C" __declspec(dllexport)`

## NVJPEG notes

| | DLL (`adNvJpeg.cpp`) | Collector (`main.cpp`) |
|---|---|---|
| **Compiled?** | **No** — `AD_NVJPEG_ENABLE` is defined only in `NvJpegCollector.vcxproj`, and even there it is unused (the collector's sources are `main.cpp` + `QualityDetectors.cpp`, which use `<nvjpeg.h>` directly and never include `adNvJpeg.h`); in `AntiDupl.dll` the whole path is `#ifdef`-ed out — dead reference code, never enabled in this fork's history (would have dynamic-loaded `nvjpeg64_12.dll`) | Yes (`nvjpeg.lib` direct link) |
| Backend | (unused) `NVJPEG_BACKEND_DEFAULT` | `NVJPEG_BACKEND_DEFAULT` (`nvjpegCreateEx`) |
| Output | (unused) `NVJPEG_OUTPUT_BGRI`, pitch `width*3` | **`NVJPEG_OUTPUT_Y`** (grayscale Y-only, since “Y-decode” speedup commit `c326490`) |
| Pitch | (unused) | **width-only, 32-byte aligned**: `((srcW + 31) / 32) * 32` bytes/row |
| Batch | (unused) | batch size **1** (`nvjpegDecodeBatchedInitialize(handle, state, 1, DetectPhysicalCores(), NVJPEG_OUTPUT_Y)` — entropy decode parallelized on CPU threads instead) |

- nvJPEG batched decode with `batch > 1` is slower on consumer GPUs — keep batch size **1**.
- Wrong pitch → `cudaErrorIllegalAddress` (especially Collector path).
- Fork design: GPU JPEG decoding lives **only** in the standalone collector exe (the GUI launches/controls it for DB create/update; `src/AntiDupl/adNvJpeg.cpp` is dead reference code). The DLL decodes JPEG on CPU (`adTurboJpeg.cpp` / libjpeg-turbo) and uses CUDA only for compare kernels (`adGPU.cu`, enabled via `ENABLE_CUDA` — a different macro from `AD_NVJPEG_ENABLE`).

## Tests

**No automated test suite in the solution.** CI only checks that builds succeed.

Standalone: `tests/AntiDupl.Contract.Tests/` — dependency-free console runner (not in `.sln`), verifies managed P/Invoke enums/struct layouts/offsets stay in lock-step with `src/AntiDupl/AntiDupl.h`:

```bash
dotnet run --project tests\AntiDupl.Contract.Tests\AntiDupl.Contract.Tests.csproj   # exit 0 = OK, 1 = contract drift
```

Run it after any change to `AntiDupl.h`, `adOptions.h` or `CoreDll.cs` (65 checks, last verified passing 2026-09-05). Legacy root comparison utilities (`test_ssim.cs`, `benchmark_ssim.cs`) were removed in this fork; the csproj comment referencing them is stale.

## Key files

| Task | Files |
|------|-------|
| Search / compare engine | `src/AntiDupl/adEngine.cpp` |
| GPU kernels | `src/AntiDupl/adGPU.cu`, `adGPUManager.*` |
| DB load/save + format detect | `src/AntiDupl/adImageDataStorage.cpp` |
| DB registry (XML) | `src/AntiDupl/adDatabaseRegistry.cpp` |
| Searcher → multi-DB load | `src/AntiDupl/adSearcher.cpp` |
| GPU collector | `src/NvJpegCollector/main.cpp` (+ `QualityDetectors.cpp`) |
| P/Invoke | `src/AntiDupl.NET.Core/Original/CoreDll.cs` |
| Managed API surface | `src/AntiDupl.NET.Core/CoreLib.cs` |
| Main GUI | `src/AntiDupl.NET.WinForms/Form/MainForm.cs` |
| Database Manager UI | `src/AntiDupl.NET.WinForms/Forms/DatabaseManagerForm.cs` |
| Auto-select | `src/AntiDupl.NET.WinForms/AutoSelector.cs` |
| nvJPEG (DLL, dead code) | `src/AntiDupl/adNvJpeg.cpp` |
| Options | `src/AntiDupl/adOptions.h` / `.cpp` |

## Agent workflow notes

- Prefer WinForms for product behavior changes unless WPF is explicitly requested.
- Long product status / known fixed issues: `PROJECT_CONTEXT.md` (verify against code; its version claim predates 2.6.0).
- Historical plans: `IMPLEMENTATION_PLAN.md`. Audits: `Audit/`, root `Audit.md`.
- Do not commit secrets, local DBs under `bin/`, or `vcpkg/` tree changes unless asked. `bin/`, `out/`, `src/vcpkg_installed/`, generated `adExternal.h`/`External.cs` are gitignored (never present in git).
- Release process: update `src/version.txt` → build C++ → copy DLL → build C# → `dotnet publish` → manual copy native deps (see Binary imports) → zip → `git tag` + `gh release create` (creates draft).

## Build events & generated files

- **`adExternal.h`** (C++) and **`External.cs`** (C#) are auto-generated from `src/version.txt` by pre-build scripts (`adExternal.cmd`, `External.cmd`). **Never edit by hand.** They are gitignored; a build regenerates them locally.
- Post-build: `cmd/CopyData.cmd` (called from `Deploy.cmd` and WinForms PostBuild target) copies `data/resources/` (mirror) + `docs/data/resources/` (additive) into the output dir.
- Packaging: `cmd/MakeBin.cmd` builds SFX exe+zip into `out/bin/`, `MakePublish.cmd` → `out/Publish/` (single-file portable), `MakeSrc.cmd` → `out/src/`. They use WinRAR if installed, else bundled `cmd/7-zip/7za_2201.exe`.
