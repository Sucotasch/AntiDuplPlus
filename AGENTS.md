# AntiDuplPlus

GPU-accelerated duplicate/similar image finder (fork of AntiDupl.NET). **Windows x64 only.** Version from `src/version.txt` (currently 2.5.0).

Trust **code** over long notes in `PROJECT_CONTEXT.md` / `IMPLEMENTATION_PLAN.md` if they conflict.

## Architecture

Solution: `src/AntiDupl.sln` (build dependency order matters):

1. **AntiDupl** (C++/CUDA DLL) → `src/AntiDupl/` — image processing, GPU kernels, DB I/O, exports
2. **NvJpegCollector** (C++/CUDA exe) → `src/NvJpegCollector/` — GPU DB collector (`index.adi` + `0000.adi`)
3. **AntiDupl.NET.Core** (C# net8.0) → `src/AntiDupl.NET.Core/` — P/Invoke bindings
4. **AntiDupl.NET.WinForms** (C# net8.0-windows) → `src/AntiDupl.NET.WinForms/` — **main GUI**
5. **AntiDupl.NET.WPF** — exists in solution; less maintained than WinForms

## Repo map

| Path | Role |
|------|------|
| `src/AntiDupl/` | Native core DLL |
| `src/NvJpegCollector/` | Standalone collector utility |
| `src/AntiDupl.NET.Core/` | Managed bindings (`CoreLib`, `Original/CoreDll`) |
| `src/AntiDupl.NET.WinForms/` | Primary UI |
| `src/AntiDupl.NET.WPF/` | Secondary UI |
| `data/resources/` | Runtime strings/resources (copied post-build) |
| `cmd/` | `CopyData.cmd`, packaging scripts |
| `bin/<Config>/` | Shared build output |
| `Audit/` | Audit reports (historical) |
| `.agents/skills/karpathy/` | Engineering discipline for agents |

## Build

### Prerequisites
- Visual Studio 2022 (v143 toolset)
- CUDA Toolkit 12.8+ (`CUDA 12.8.props` / `.targets`)
- vcpkg (triplet: `x64-windows-static`) — deps in `src/vcpkg.json`
- .NET 8.0 SDK

### Commands

```bash
# Full solution (recommended)
msbuild src\AntiDupl.sln /p:Configuration=Release /p:Platform=x64

# Single C++ project (VS Developer Command Prompt)
msbuild src\AntiDupl\AntiDupl.vcxproj /p:Configuration=Release /p:Platform=x64 /m:1

# Single C# project
dotnet build src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj /p:SolutionDir="src\\" -c Release
```

### Configurations & output
- **Debug|x64**, **Release|x64**, **Publish|x64**
- NvJpegCollector: **Release only**
- Output: `bin/<Configuration>/` (shared by C++ and C#)
- Intermediates: `obj/<Configuration>/<ProjectName>/`

### Build quirks
- **`adExternal.h`** (C++) and **`External.cs`** (C#) are **auto-generated** from `src/version.txt` by pre-build scripts. **Never edit by hand.**
- Post-build: `cmd/CopyData.cmd` copies `data/resources/` into the output dir.
- vcpkg installs under `src/vcpkg_installed/x64-windows-static/`. First build can be long.
- CUDA code gen: SM 7.5 / 8.6 / 8.9 (`compute_75,sm_75; compute_86,sm_86; compute_89,sm_89`).
- **vcpkg `simd` path quirk**: MSBuild may look under `.../x64-windows-static/x64-windows-static/include` while headers land in `.../include`. Workaround: copy Simd headers + `.lib` into the nested path, then build with `/p:VcpkgManifestInstall=false` if needed. Details in `PROJECT_CONTEXT.md`.

### CI
`.github/workflows/AntiDupl_CI.yml` — Debug/Release/Publish on `windows-latest`. Publish uses `PublishProfile="AntiDuplPublishSingleFile"`. Version tags create draft GitHub releases.

## Hard invariants (do not break)

1. **Two incompatible `.adi` formats** — keep both code paths and on-disk layouts compatible:

   | | DLL-native | Collector-native |
   |---|---|---|
   | **Writer** | `adImageDataStorage.cpp` (`SaveData`) | `NvJpegCollector/main.cpp` (raw `fwrite`) |
   | **Header** | `"adid"` magic + version | None (first `u32` = ThumbSize) |
   | **Reader** | `LoadData()` / stream path | Auto-detect in `Load()` → `LoadCollectorNative()`; search entry: `TSearcher::LoadDatabase()` |
   | **Created by** | CPU file scan / DLL save | GPU collector utility |

   **Do not change the raw fwrite layout in `main.cpp`** without updating loaders in the DLL.

2. **Portable paths** — runtime data is relative to the exe:
   - `ad_database.xml` next to exe
   - DBs under `databases/<Name>/`
   - Prefer relative paths in registry/storage

3. **Interop layout** — `CoreDll` / exported C API structs must stay binary-compatible with `AntiDupl.dll` (`extern "C" __declspec(dllexport)`).

4. **Surgical changes** — touch only what the task requires; no drive-by refactors. Prefer simplicity (see `.agents/skills/karpathy/SKILL.md`).

## C++ ↔ C# interop

- Low-level P/Invoke: `src/AntiDupl.NET.Core/Original/CoreDll.cs`
- High-level wrapper used by UI: `src/AntiDupl.NET.Core/CoreLib.cs`
- Native exports from `AntiDupl.dll` via `extern "C" __declspec(dllexport)`

## NVJPEG notes

| | DLL (`adNvJpeg.cpp`) | Collector (`main.cpp`) |
|---|---|---|
| Backend | `NVJPEG_BACKEND_DEFAULT` | same (not hardware-only) |
| Output format | `NVJPEG_OUTPUT_BGRI` | `NVJPEG_OUTPUT_RGBI` |
| Pitch | `width * 3` | **aligned**: `((w*3+31)/32)*32` |

- nvJPEG batched decode with `batch > 1` is slower on consumer GPUs — initialize with batch size **1**.
- Wrong pitch → `cudaErrorIllegalAddress` (especially Collector path).

## Tests

No automated test suite in the solution. CI only checks that the build succeeds. Root `benchmark_ssim.*` / `test_ssim.*` are **standalone** utilities, not part of `AntiDupl.sln`.

## Key files

| Task | Files |
|------|-------|
| Search / compare engine | `src/AntiDupl/adEngine.cpp` |
| GPU kernels | `src/AntiDupl/adGPU.cu`, `adGPUManager.*` |
| DB load/save + format detect | `src/AntiDupl/adImageDataStorage.cpp` |
| DB registry (XML) | `src/AntiDupl/adDatabaseRegistry.cpp` |
| Searcher → multi-DB load | `src/AntiDupl/adSearcher.cpp` |
| GPU collector | `src/NvJpegCollector/main.cpp` |
| P/Invoke | `src/AntiDupl.NET.Core/Original/CoreDll.cs` |
| Managed API surface | `src/AntiDupl.NET.Core/CoreLib.cs` |
| Main GUI | `src/AntiDupl.NET.WinForms/Form/MainForm.cs` |
| Database Manager UI | `src/AntiDupl.NET.WinForms/Forms/DatabaseManagerForm.cs` |
| Auto-select | `src/AntiDupl.NET.WinForms/AutoSelector.cs` |
| nvJPEG (DLL) | `src/AntiDupl/adNvJpeg.cpp` |
| Options | `src/AntiDupl/adOptions.h` / `.cpp` |

## Agent workflow notes

- Prefer WinForms for product behavior changes unless WPF is explicitly requested.
- Long product status / known fixed issues: `PROJECT_CONTEXT.md` (verify against code).
- Historical plans: `IMPLEMENTATION_PLAN.md`. Audits: `Audit/`.
- Do not commit secrets, local DBs under `bin/`, or `vcpkg/` tree changes unless asked.
- `dotnet publish` does **not** copy native P/Invoke DLLs (`AntiDupl.dll`, `nvjpeg64_12.dll`, `cudart64_12.dll`). Copy them manually after publish.
- Release process: update `src/version.txt` → build C++ → copy DLL → build C# → `dotnet publish` → manual copy native deps → zip → `git tag` + `gh release create` (creates draft).
