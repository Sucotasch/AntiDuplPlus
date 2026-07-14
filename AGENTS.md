# AntiDuplPlus

GPU-accelerated duplicate/similar image finder. Windows x64 only.

## Architecture

Four projects in `src/AntiDupl.sln` (build dependency order matters):

1. **AntiDupl** (C++/CUDA DLL) → `src/AntiDupl/` → core image processing, GPU kernels, database I/O
2. **NvJpegCollector** (C++/CUDA exe) → `src/NvJpegCollector/` → standalone GPU JPEG decoder utility
3. **AntiDupl.NET.Core** (C# library) → `src/AntiDupl.NET.Core/` → P/Invoke bindings to AntiDupl.dll
4. **AntiDupl.NET.WinForms** (C# GUI) → `src/AntiDupl.NET.WinForms/` → main application

AntiDupl.NET.WPF exists but is less maintained.

## Build

### Prerequisites
- Visual Studio 2022 (v143 toolset)
- CUDA Toolkit 12.8+ (imports `CUDA 12.8.props` / `.targets`)
- vcpkg (triplet: `x64-windows-static`)
- .NET 8.0 SDK

### Build commands (via Visual Studio or MSBuild)

```bash
# Full solution (recommended)
msbuild src\AntiDupl.sln /p:Configuration=Release /p:Platform=x64

# Single C++ project (requires MSBuild from VS Developer Command Prompt)
msbuild src\AntiDupl\AntiDupl.vcxproj /p:Configuration=Release /p:Platform=x64 /m:1

# Single C# project
dotnet build src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj /p:SolutionDir="src\\" -c Release
```

### Build configurations
- **Debug|x64**, **Release|x64**, **Publish|x64**
- NvJpegCollector only supports Release
- All output goes to `bin/<Configuration>/` (shared across C# and C++ projects)
- Intermediate files go to `obj/<Configuration>/<ProjectName>/`

### Key build quirks
- **`adExternal.h`** (C++) and **`External.cs`** (C#) are **auto-generated** from `src/version.txt` by pre-build scripts. Never edit these files manually.
- **Post-build**: `cmd/CopyData.cmd` copies `data/resources/` to the output directory automatically.
- **vcpkg dependencies** install to `src/vcpkg_installed/x64-windows-static/`. First build may take a while.
- **CUDA code generation targets**: SM 7.5 (Turing), SM 8.6 (Ampere), SM 8.9 (Ada Lovelace).

### CI
GitHub Actions (`.github/workflows/AntiDupl_CI.yml`): builds all 3 configurations on `windows-latest`. Publish config uses `PublishProfile="AntiDuplPublishSingleFile"`. Release tags create draft GitHub releases with VirusTotal scans.

## Two .adi formats (don't confuse them)

The project has **two incompatible** `.adi` file formats with separate code paths:

| | DLL-native | Collector-native |
|---|---|---|
| **Writer** | `adImageDataStorage.cpp` (SaveData) | `NvJpegCollector/main.cpp` (raw fwrite) |
| **Header** | `"adid"` magic bytes + version | None (raw ThumbSize u32) |
| **Reader** | `LoadData()` via `TInputFileStream` | `LoadDatabase()` in `adSearcher.cpp` via fread |
| **Created by** | File scanning (CPU) | GPU collector utility |

**Don't change the raw fwrite format in `main.cpp`** — it must stay compatible with `LoadDatabase()` in the DLL.

## Portability

All runtime data is portable relative to the exe:
- `ad_database.xml` (database registry) lives next to the exe
- Database files go to `databases/<Name>/` relative to the exe
- All paths stored as relative paths for portability

## C++ ↔ C# interop

- P/Invoke bindings: `src/AntiDupl.NET.Core/CoreDll.cs`
- Functions exported via `extern "C" __declspec(dllexport)` from `AntiDupl.dll`
- Structures passed as `ref` or through `IntPtr`

## NVJPEG notes

- `NVJPEG_BACKEND_DEFAULT` (software decoder on GPU) is used — NOT `NVJPEG_BACKEND_HARDWARE` (server GPUs only)
- `NVJPEG_OUTPUT_BGRI` (not RGBI or RGB) works on consumer GPUs
- Batch decode with `batch_size > 1` is **slower** than individual decode on consumer GPUs — always use `batch_size=1`
- Pitch alignment is critical: `((width*3+31)/32)*32` — wrong pitch causes `cudaErrorIllegalAddress`

## No test suite

There are no automated tests, linting, or type-checking configured. CI only verifies the build succeeds. `benchmark_ssim.csproj` and `test_ssim.csproj` exist at the root but are standalone benchmark/test utilities, not part of the solution build.

## Key files for common tasks

| Task | Files |
|------|-------|
| Search/comparison engine | `src/AntiDupl/adEngine.cpp` |
| GPU kernels | `src/AntiDupl/adGPU.cu` |
| Database load/save | `src/AntiDupl/adImageDataStorage.cpp` |
| Database registry (XML) | `src/AntiDupl/adDatabaseRegistry.cpp` |
| GPU collector utility | `src/NvJpegCollector/main.cpp` |
| P/Invoke bindings | `src/AntiDupl.NET.Core/CoreDll.cs` |
| Main GUI form | `src/AntiDupl.NET.WinForms/Form/MainForm.cs` |
| Database Manager UI | `src/AntiDupl.NET.WinForms/Forms/DatabaseManagerForm.cs` |
| nvJPEG integration (DLL) | `src/AntiDupl/adNvJpeg.cpp` |
| Options/settings | `src/AntiDupl/adOptions.h` / `.cpp` |
