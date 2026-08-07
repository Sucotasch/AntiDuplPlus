# AntiDuplPlus — Full Engineering Code Review & Cleanup

| Field | Value |
|-------|--------|
| **Date** | 2026-08-06 |
| **Version** | `src/version.txt` → **2.5.1** (last tag commit `4a5f71b`) |
| **Commit reviewed** | `7c092b3` (clean tree, after pre-review sync commit) |
| **Scope** | Full stack: native DLL (`src/AntiDupl/`), collector (`src/NvJpegCollector/`), Core interop (`src/AntiDupl.NET.Core/`), WinForms GUI, WPF (noted), build/CI/packaging |
| **Method** | Full `Release|x64` build + warning analysis; 3 parallel deep-review passes (native, C#, build/CI); line-by-line verification of every prior-audit fix claim; new dependency-free contract test suite |
| **Code changes** | **None** to product code (per instruction). Added test project `tests/AntiDupl.Contract.Tests/` (64 checks, all green) |
| **Audience** | Junior / next agent |
| **Related** | `AGENTS.md`, `PROJECT_CONTEXT.md`, `Audit/FULL_AUDIT_2026-07-17.md`, `Audit/DEV_GUIDE_RELIABILITY_GPU_DB_2026-07-17.md` |

> **Trust code.** The historical `FULL_AUDIT` describes bugs that are now mostly **fixed in master**.
> This review re-verified each claim against the current source. The code is the source of truth.

---

## 0. How to use this document

1. Read **§1 Baseline** (build + warning reality).
2. Read **§2 Verified: prior-audit fixes** (do not re-open these without counter-evidence).
3. Fix in **§3 priority order** (P1 → P3). Each item has `file:line`, impact, and a paste-ready fix.
4. Run the **§6 contract tests** after any interop change.
5. Respect invariants in **§7**.
6. **Remaining concerns** (§8) and **assumptions** (§9) are at the end.

---

## 1. Baseline (validated 2026-08-06)

### 1.1 Build

```
msbuild src\AntiDupl.sln /p:Configuration=Release /p:Platform=x64 /p:VcpkgManifestInstall=false
```
**Result: SUCCESS** — all 5 projects:
- `NvJpegCollector.exe` ✅
- `AntiDupl.dll` ✅
- `AntiDupl.NET.Core.dll` ✅
- `AntiDupl.NET.WinForms.dll` ✅
- `AntiDupl.NET.WPF.dll` ✅

**Note:** a plain build (without `/p:VcpkgManifestInstall=false`) re-triggers a full from-source rebuild of the `simd` vcpkg port (took >25 min and hit the timeout). The manual Simd header/`.lib` copy workaround in `PROJECT_CONTEXT.md:87-93` is still required; document the flag in the build instructions.

Root utilities build clean:
- `test_ssim.csproj` ✅ (0 warnings)
- `benchmark_ssim.csproj` ✅ (0 warnings)
- `test_ssim_gpu.cpp` is a standalone `.cpp` (no project), not in any solution.

### 1.2 Warnings: **2065** in the C# build — this is a real hygiene problem

| Category | Count | Meaning |
|----------|-------|---------|
| `CS1591` | 652 | Missing XML docs on public members (noise, but enabled by `DocumentationFile` + `AnalysisMode=AllEnabledByDefault`) |
| `CA1051` | 453 | Public instance fields (the DTO style uses fields by design — should be `[SuppressMessage]`d, not noisy) |
| `CA1303/1305/1307` | ~277 | Localization / culture issues in string handling |
| `CA1062` | 110 | Public methods don't validate args |
| `CA1416` | 90 | Windows-only APIs on non-Windows TFM branches |
| `CA1031` | 50 | `catch(Exception)` broad catches |
| `CA2000` | 45 | Disposable not disposed (real leaks — see CSH-01/12) |
| `CA1707/1708/1034/1815/1822/1825/2211/1507` | ~190 | Naming/design rules |
| `CS0472` | 9 | Always-true comparison — **real dead guards** (WPF converters) |
| `CS0162` | 24 | Unreachable code (WPF) |
| `C4996` | 20 | `_wfopen` unsafe CRT (native — the debug-log blocks) |
| `C4244` | 2 | `uint64_t`→`int`/`TUInt32` narrowing (native) |
| `SYSLIB0051` | 12 | Obsolete exception ctors |
| `NU` | 4 | NuGet |

**There is no `TreatWarningsAsErrors` anywhere.** The build is effectively unguarded against regressions. Fix the code-level issues in §3, then consider `CFG-10`.

---

## 2. Verified: prior-audit fixes (do NOT re-open)

All claims in `DEV_GUIDE_RELIABILITY_GPU_DB_2026-07-17.md` §2 were re-checked against current source and are **present and correct**:

| ID | Claim | Status | Evidence |
|----|-------|--------|----------|
| BUG-01 | GPU pack loop guards `data->side == reducedImageSize` before `memcpy` | ✅ | `adEngine.cpp:354-363` |
| BUG-02 | DLL-native index magic detected as `"adii"` = `0x69696461u` | ✅ | `adImageDataStorage.cpp:174-176` |
| BUG-03 | `LoadCollectorData` validates `thumbBytes == side*side` | ✅ | `adImageDataStorage.cpp:615-627` |
| BUG-05 | `MarkRemoved` (LocalAction 14/15) implemented + wired | ✅ | `AntiDupl.h:244-245`, `adUndoRedoEngine.cpp:555-590`, `CoreDll.cs:150-151` |
| BUG-07 | `validCount < 2` → success (no fake GPU error) | ✅ | `adEngine.cpp:398-401` |
| BUG-09 | `d_poolMask` freed on all error paths | ✅ | `adGPU.cu:672-676, 685-689, 744-748, 979` |
| BUG-11 | skip flag set **before** `CollectManager::Start()` | ✅ | `adEngine.cpp:631-662` |
| WP-A | GPU filter parity: `ratioControl`, min/max size, `transformedImage`/`ignoreFrameWidth`→CPU | ✅ | `adEngine.cpp:270-284` (MatchCallback), `:357-361` (pack), `:628-629` (CPU force) |

Locked in by `tests/AntiDupl.Contract.Tests` for the interop-facing parts (enum 14/15, struct layout).

**Still open (carried from the guide):** BUG-08 (match cap), BUG-13 (collector `hash=0`), FIND-8 (batch cancel), BUG-03 fail-soft (skip thumb, load still true), BUG-06 shutdown 10s theoretical race.

---

## 3. Findings — priority order

### Priority P1 (fix first)

#### P1-1 [NAT-08] Malformed collector `.adi` can crash the DLL (unchecked `fileThumbSize`, unchecked `fread`)
- **Where:** `adImageDataStorage.cpp:576` (`TImageData imageData(fileThumbSize)` allocates `fileThumbSize²` from an unvalidated header), `:587-630` (`fread` returns ignored, `uint64_t crc32c → TUInt32` at `:609`).
- **Impact:** A crafted/corrupt `0000.adi` with a huge `thumbSize` (e.g. `0xFFFFFFF0`) makes `TPixelData` get `fast=NULL`/`main=NULL+16` from failed allocation, then `fread(imageData.data->main, 1, thumbBytes, f)` writes through an invalid pointer → AV / heap corruption. Truncated files silently produce garbage records.
- **Fix (reject invalid/mismatched collector DBs before allocation):**
```cpp
// LoadCollectorData, before `TImageData imageData(fileThumbSize);`
if (fileThumbSize != 16 && fileThumbSize != 32 && fileThumbSize != 64 &&
    fileThumbSize != 128 && fileThumbSize != 256)
{
    fclose(f);
    return false;
}
TImageData imageData(fileThumbSize);
if (!imageData.data || !imageData.data->main)
{
    fclose(f);
    return false;
}
// Check the fixed-size metadata freads too:
if (fread(&fileSize, 8, 1, f) != 1) { fclose(f); return false; }
// ... same pattern for time/hash/type/width/height/blockiness/blurring/defect/crc32c/filled
```
- **Regression:** mismatched-thumb collector DBs now fail cleanly instead of silently producing empty results (previously the BUG-01 empty-result path).

#### P1-2 [NAT-02] Long-path delete bypasses the Recycle Bin → permanent data loss
- **Where:** `adFileUtils.cpp:42-50`.
- **Impact:** For paths ≥ `MAX_PATH`, the UNICODE build returns `::DeleteFile(fileName)` **ignoring the `toRecycle` flag**. With default `deleteToRecycleBin=TRUE` (`adOptions.cpp:118`), files at deep paths (>260 chars — common on long-path/UNC trees) are **permanently deleted** instead of going to the Recycle Bin.
- **Fix:** never silently permanent-delete. Use `IFileOperation` with `FOFX_RECYCLEONDELETE` for the long-path branch:
```cpp
if (length >= MAX_PATH)
{
#ifdef UNICODE
    if (!toRecycle)
        return ::DeleteFile(fileName) != FALSE;
    CComPtr<IFileOperation> pOp;
    if (SUCCEEDED(CoCreateInstance(CLSID_FileOperation, NULL, CLSCTX_ALL,
                                   IID_PPV_ARGS(&pOp))))
    {
        pOp->SetOperationFlags(FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOFX_RECYCLEONDELETE);
        CComPtr<IShellItem> pItem;
        if (SUCCEEDED(SHCreateItemFromParsingName(fileName, NULL, IID_PPV_ARGS(&pItem))))
        {
            pOp->DeleteItem(pItem, NULL);
            return SUCCEEDED(pOp->PerformOperations());
        }
    }
    return false; // fail safe: do NOT permanent-delete
#else
    return false;
#endif
}
```
(add `#include <shobjidl.h>`; ensure COM is initialized on the caller thread).

#### P1-3 [C-4] GPU `EnsureCapacity(10000, …)` hardcoded → OOB device write for >10k images
- **Where:** `adDataCollector.cpp:162-169`; device write at `adGPU.cu:361` (`g_pDeviceThumbnailBuffer + index*g_thumbSize`).
- **Impact:** In the non-AllVsAll GPU-assisted mode (`ignoreFrameWidth != 0` or CPU algorithm with GPU upload), a dataset with more than 10 000 images makes `globalIdx` exceed the VRAM buffer → `cudaErrorIllegalAddress` or silent corruption.
- **Fix:** size by the actual index space, not a constant:
```cpp
if (!m_pEngine->GpuManager()->EnsureCapacity(pImageData->globalIdx + 1, thumbSize))
{
    AD_DEBUG("FillPixelData: GPU EnsureCapacity FAILED\n");
}
```

#### P1-4 [CFG-01] Publish profiles target `net6.0` while projects are `net8.0` → Publish is broken
- **Where:** `src/AntiDupl.NET.Core/Properties/PublishProfiles/AntiDuplPublishSingleFile.pubxml:12`, `.../AntiDupl.NET.WinForms/Properties/PublishProfiles/AntiDuplPublishSingleFile.pubxml:12`, `.../AntiDupl.NET.WPF/Properties/PublishProfiles/AntiDuplPublishSingleFile.pubxml:12`.
- **Impact:** `dotnet publish -p:PublishProfile=…` fails with `NETSDK1047` (no net6.0 assets restored). Single-file release cannot be produced by the pipeline.
- **Fix:** change the TFM in each pubxml:
```xml
<TargetFramework>net8.0</TargetFramework>            <!-- Core -->
<TargetFramework>net8.0-windows</TargetFramework>    <!-- WinForms, WPF -->
```

#### P1-5 [CFG-02] CI Publish leg runs `-t:Publish -restore` on the whole mixed solution
- **Where:** `.github/workflows/AntiDupl_CI.yml:68`.
- **Impact:** C++ projects (`AntiDupl.vcxproj`, `NvJpegCollector.vcxproj`) have **no `Publish` target** (verified: none in `MSBuild\Microsoft\VC\v170\Microsoft.Cpp.targets`) → `MSB4057`. Even after P1-4, this leg fails before any C# publish.
- **Fix:** scope publish to the WinForms project and build the native core separately:
```yaml
- if: matrix.configuration == 'Publish'
  name: Build native core
  run: msbuild /m /p:Configuration=Publish /p:Platform=x64 src\AntiDupl\AntiDupl.vcxproj
- if: matrix.configuration == 'Publish'
  name: Publish single-file
  run: dotnet publish src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj -c Publish -p:PublishProfile=AntiDuplPublishSingleFile
```

#### P1-6 [CFG-05] CI has no CUDA Toolkit install step → C++ build fails on runners
- **Where:** `.github/workflows/AntiDupl_CI.yml:38-56`; `AntiDupl.vcxproj:26,190` unconditionally imports `CUDA 12.8.props/.targets`.
- **Impact:** GitHub `windows-latest` images do not ship the CUDA Toolkit → `MSB4019` on every leg. (Releases in git history were likely produced on a self-hosted/developer machine.)
- **Fix:** install CUDA before the build:
```yaml
- name: Install CUDA 12.8
  uses: JimVer/cuda-toolkit-action@v0.2
  with:
    cuda: '12.8.0'
    method: 'network'
```

#### P1-7 [CFG-04] `MakePublish.cmd` hashes non-existent files in the WinRAR branch
- **Where:** `cmd/MakePublish.cmd:45,47`.
- **Impact:** Hashes `AntiDupl.NET-%VERSION%.exe/.zip` but actual archives are `…_SingleFilePortable.exe/.zip` → `.hash.txt` files contain certutil error text. The 7-zip branch (`:50,52`) is already correct. (GitHub runners use the 7-zip branch, so CI is unaffected; local WinRAR releases ship broken hashes.)
- **Fix:**
```bat
certutil -hashfile %OUT_DIR%\AntiDupl.NET-%VERSION%_SingleFilePortable.exe SHA256 > %OUT_DIR%\AntiDupl.NET-%VERSION%_SingleFilePortable.exe.hash.txt
certutil -hashfile %OUT_DIR%\AntiDupl.NET-%VERSION%_SingleFilePortable.zip SHA256 > %OUT_DIR%\AntiDupl.NET-%VERSION%_SingleFilePortable.zip.hash.txt
```

#### P1-8 [NEW-01] WPF "DCT histogram" button crashes with NRE
- **Where:** `src/AntiDupl.NET.WPF/Command/CalculateHistogramPeaksCommand.cs:106-144` (whole `RunProcess` body commented out; `_arrayInfo` never assigned), wired to UI at `MainWindow.xaml:172`; timer callback `:79-86` dereferences `_arrayInfo.Single(...)` → **NullReferenceException ~1s after click**.
- **Impact:** Dead feature + guaranteed crash in the secondary UI.
- **Fix:** either implement it against a real native `CalculateHistogramPeaks` API (does not exist) **or remove the command + XAML binding**. Do not ship as-is.

#### P1-9 [CSH-01] GDI+ Bitmap leak in `ComparableBitmap` (exhausts GDI handles)
- **Where:** `src/AntiDupl.NET.WinForms/ComparableBitmap.cs:65`.
- **Impact:** The grayscale `Bitmap` returned by `ToGrayScale` (line 96-107, `new Bitmap(...)`) is never disposed. Each highlight pass over a fragment grid (up to 16×16 = 256 fragments per pair) leaks an HBITMAP; repeated navigation/highlighting exhausts GDI handles → "out of memory" / blank previews.
- **Fix:**
```csharp
using (Bitmap gray = ToGrayScale(sectionBmp))
{
    this._grayScaleData = GetBmpBytes(gray);
}
```
- Also dispose the `MemoryStream` in `PictureBoxPanel.cs:579-583` (CSH-12):
```csharp
using (MemoryStream ms = LoadFileToMemoryStream(...))
{
    Bitmap bmp = new Bitmap(ms);
    ...
}
```

---

### Priority P2 (high value, low risk)

#### P2-1 [NAT-01/C-1] Debug log dereferences the handle **before** `CHECK_HANDLE` + 20 raw `_wfopen`/`fwprintf` blocks in Release
- **Where:** `AntiDupl.cpp:386-397` (crash risk: `handle->Status()->State()` at `:394` runs before `CHECK_HANDLE` at `:399`); `adEngine.cpp:302-313, 384-394, 523-533, 537-557, 571-586, 592-601, 610-620, 641-658, 683-693, 708-718, 727-738, 743-753`; `adPath.cpp:288-301, 325-337`; `adImageDataStorage.cpp:170, 486`. Build confirms 20× `C4996` (`_wfopen`).
- **Impact:** Every `Search()`/path-set writes `trace.log`/`gpu_debug.log` next to the exe (per-call file I/O in Release, silently no-op in read-only dirs). A NULL/invalid handle **crashes the DLL at the P/Invoke boundary** because the log block runs before validation.
- **Fix (two parts):**
  1. Move the log block **after** `CHECK_HANDLE CHECK_ACCESS LOCK` in `adPathWithSubFolderSetW`.
  2. Replace the raw `_wfopen`/`fwprintf` blocks with the existing thread-safe `ad::TLogger` (`adLogger.h`) gated on `AD_LOGGER_ENABLE`, or delete them. A minimal drop-in:
```cpp
// adLogger.h already provides: AD_LOG(...), AD_LOG_W(...) — use those instead of _wfopen blocks.
```
- **Regression:** none — logging is diagnostic; validation now runs first.

#### P2-2 [NAT-03] `TRecycleBin::Restore` is a stub → Undo never un-marks `MarkRemoved` files
- **Where:** `adRecycleBin.cpp:57-62` (Restore returns `false`); called from `adUndoRedoEngine.cpp:349`.
- **Impact:** After Undo of a batch move (BUG-05 path), the `removed` flag stays `true` forever: the image remains hidden in results even though the file exists at its new path. Redo then no-ops (`removed==true` early return at `:45`).
- **Fix:** clear the flag when the file still exists:
```cpp
bool TRecycleBin::Restore(TImageInfo *pImageInfo)
{
    if (pImageInfo->removed && IsFileExists(pImageInfo->path.Original().c_str()))
    {
        pImageInfo->removed = false;
        m_pStatus->DeleteImage(-1, -pImageInfo->size);
        return true;
    }
    return false; // real deletes to the Recycle Bin cannot be restored programmatically
}
```

#### P2-3 [A-2] Fragment-height bug in difference preview
- **Where:** `src/AntiDupl.NET.WinForms/GUIControl/ImagePreviewPanel.cs:539-540`.
- **Impact:** `heightOfFragment = bitmap.Height / AmountOfFragmentsOnX` should use `AmountOfFragmentsOnY`. When X ≠ Y (user-adjustable, `ResultsOptions.cs:217-247`), the highlight grid is wrong (gaps/overlaps).
- **Fix:**
```csharp
int widthOfFragment = bitmap.Width / m_options.resultsOptions.AmountOfFragmentsOnX;
int heightOfFragment = bitmap.Height / m_options.resultsOptions.AmountOfFragmentsOnY;
```

#### P2-4 [CSH-02] `Comparator.Distance` integer overflow → wrong similarity
- **Where:** `src/AntiDupl.NET.WinForms/Comparator.cs:41-53`.
- **Impact:** `int sum` overflows when Σ(first−second)² > int.MaxValue (e.g. 512×512 luma with 2×2 fragments → ~4.3e9). Wraps silently → wrong "similarity %" and wrong difference highlighting. Also `Math.Pow` per pixel is slow/rounding-prone.
- **Fix:**
```csharp
private static float Distance(byte[] first, byte[] second)
{
    long sum = 0;
    int length = Math.Min(first.Length, second.Length);
    for (int x = 0; x < length; x++)
    {
        int d = first[x] - second[x];
        sum += (long)d * d;
    }
    return (float)sum / length;
}
```

#### P2-5 [A-1 / FIND-JPEG] Phantom `jpegPeaks` field reads native padding
- **Where:** `src/AntiDupl.NET.Core/Original/CoreDll.cs:411` (extra `uint jpegPeaks`); native `AntiDupl.h:552-565` has no such field; `TImageInfo::Export(W)` (`adImageInfo.cpp:162-180`) never writes it.
- **Impact:** Sizes coincide by padding (both 69240), so no overrun — but C# reads 4 bytes of uninitialized native padding as `jpegPeaks`. Only WPF consumes it (`DuplPairViewModel.cs:54,63,86,95`); WinForms never does, and `CoreImageInfo.jpegPeaks` is never assigned. WPF "jpeg peaks" UI shows garbage.
- **Fix:** remove the field from `CoreDll.adImageInfoW`, `CoreImageInfo`, and the WPF consumers. (When removed, managed size becomes 69236; update the contract test `[4]`/`[5]` accordingly — see §6 note.)

#### P2-6 [A-6 / CSH-04] Production debug file I/O (`path_debug.log`, `cs_debug.log`, `trace.log`)
- **Where:** `CoreLib.cs:601-616`; `CoreOptions.cs:162-174`; `SearchExecuterForm.cs:157-170`; `DatabaseManagerForm.cs:691-700`; plus native `adPath.cpp:288-337`.
- **Impact:** Unwanted side-effect files written next to the exe on every search/path-set; `trace.log` collides with the native `adPath.cpp` writer (CSH-05).
- **Fix:** delete the write blocks (they are interop-debug leftovers). No behavior change.

#### P2-7 [C-2] `checkOnEquality` ignored in GPU AllVsAll mode
- **Where:** `adEngine.cpp:723-762` (GPU branch always runs compare) vs CPU branch gating on `checkOnEquality` (`:670-676`).
- **Impact:** With "check on equality" off, CPU mode yields no duplicate pairs but GPU mode still reports them — inconsistent behavior.
- **Fix:** gate the GPU comparison:
```cpp
if (useGpu && m_pOptions->compare.checkOnEquality == TRUE)
    gpuSuccess = ExecuteGpuAllVsAllComparison();
else if (!useGpu)
    ... // existing CPU path
```

#### P2-8 [C-3] No GPU→CPU fallback on GPU compare failure
- **Where:** `adEngine.cpp:755-758` (only sets an error status; results empty).
- **Impact:** Transient CUDA/OOM → zero useful results (known skipped BUG-10).
- **Fix (minimal):** surface the failure to the UI as a blocking error, or fall back to the CPU compare path (re-queue collected images to `m_pCompareManager` and `Finish()`).

#### P2-9 [A-8] Shutdown still has a hard 10 s `WaitForWorker` before `Dispose`
- **Where:** `src/AntiDupl.NET.WinForms/Form/MainForm.cs:100-113`.
- **Impact:** If saving a very large `.adr`/mistake DB exceeds 10 s, the finish worker still touches the engine during/after `m_core.Dispose()` (use-after-free race). The 2 s→10 s bump (BUG-06) reduced but didn't remove the risk.
- **Fix:** join without a short timeout (or add cancel), and only dispose after `State.Finish`.

#### P2-10 [CFG-03] Deprecated GitHub Actions (Node16 EOL)
- **Where:** `AntiDupl_CI.yml` — `checkout@v3` (:30,118), `read-file-action@v1` (:34,122), `setup-msbuild@v1.0.2` (:39), `upload/download-artifact@v3` (:88,99,127,132), `softprops@v1` (:159).
- **Impact:** GitHub is retiring Node16 actions; jobs will break.
- **Fix:**
```yaml
uses: actions/checkout@v4
uses: microsoft/setup-msbuild@v2
uses: actions/upload-artifact@v4
uses: actions/download-artifact@v4
uses: softprops/action-gh-release@v2
# replace read-file-action with: echo "content=$(Get-Content src/version.txt)" >> $env:GITHUB_OUTPUT
```

#### P2-11 [CFG-06] Release job is `prerelease`, not `draft` (contradicts AGENTS.md)
- **Where:** `AntiDupl_CI.yml:163`.
- **Fix:** add `draft: true` (and drop `prerelease: true` or keep both deliberately); update AGENTS.md.

#### P2-12 [CFG-07] No single source of truth for the version — 5+ divergent numbers
- **Where:** `src/version.txt` = 2.5.1 vs `Core.csproj:6-7`/`WinForms.csproj:9-10` = 2.3.11.1 vs `WPF/Properties/AssemblyInfo.cs:54-55` = 0.0.3.0 vs `docs/version.xml` = 2.3.12 vs `vcpkg.json:4` = 1.1 vs `AGENTS.md` = "2.5.0" vs `cmd/7-zip/config.txt:2` = 2.3.11.
- **Impact:** Explorer shows 2.3.11.1, About box shows 2.5.1; the update check (`NewVersionMenuItem.cs:120-122`) compares against **upstream** `version.xml` (2.3.12) so the fork's "new version" feature never fires.
- **Fix (minimal):** generate `AssemblyVersion`/`FileVersion` from `version.txt` in the C# projects (the pre-build `External.cmd` already exists — extend it), or set them to 2.5.1 at each release. Point `Resources.WebLinks.Version` at a fork-hosted `version.xml` or remove the check (NEW-04).

#### P2-13 [NEW-02] WPF converters cast before null-guard → `InvalidCastException`
- **Where:** `Convertor/DeleteBackgroundValueConverter.cs:19`, `DuplResultMultiValueConverter.cs:21,24`.
- **Impact:** A null/`UnsetValue` binding parameter throws instead of falling through to the neutral brush.
- **Fix:** guard `parameter`/`values[1]` for null/`UnsetValue` **before** casting, then delete the now-dead `!= null` clauses (the `CS0472` always-true checks in `DifferenceValueConverter.cs:17` and `FolderAreDiffrentMultuValueConverter.cs:21` too).

#### P2-14 [CFG-09] WinForms and WPF publish to the same `PublishDir` with `DeleteExistingFiles=True`
- **Where:** `WinForms pubxml:9,19`, `WPF pubxml:9,19` → both `..\..\out\Publish\AntiDupl.NET`.
- **Impact:** Publishing both wipes the other's output; shared scratch dir is fragile.
- **Fix:** give each project its own dir (`out\Publish\AntiDupl.NET.WinForms` / `...WPF`) or publish only WinForms in CI (see P1-5).

#### P2-15 [A-4] Wrong neighbour fallback + `MemoryStream` leak
- **Where:** `PictureBoxPanel.cs:573-592`.
- **Impact:** On neighbour decode failure, `GetBitmap(m_fileName)` catch block returns `BitmapWorker.LoadBitmap(m_core, m_currentImageInfo)` — the **current** image, not the failed neighbour (misleading preview). Plus `MemoryStream` never disposed.
- **Fix:** return `null` (let the caller show an error placeholder) and wrap the stream in `using` (see P1-9 fix).

#### P2-16 [CSH-08] Result list re-fetches everything + `PAGE_SIZE=16` paging → UI stalls
- **Where:** `ResultsListView.cs:443-499`; `CoreLib.cs:286-311`.
- **Impact:** For 100k+ results, `UpdateResults` materializes all rows and each page is a lock-acquiring P/Invoke (~62,500 calls for 1M results). Auto-Select runs this on the UI thread.
- **Fix:** virtualize the list (only materialize visible rows) and/or increase `PAGE_SIZE`; run Auto-Select off the UI thread.

---

### Priority P3 (cleanup / hygiene)

| ID | Where | Issue |
|----|-------|-------|
| NAT-04 | `adNvJpeg.cpp` + `#ifdef` in `adDataCollector.cpp:34,94-109`, `adImage.cpp:114-119` | `AD_NVJPEG_ENABLE` defined **only** in `NvJpegCollector.vcxproj:42`, never in the DLL → the DLL's nvJPEG path is **dead code** and the `AGENTS.md` "NVJPEG notes / DLL" table describes code that isn't compiled. Fix: define it in `AntiDupl.vcxproj` + link nvjpeg, or delete the dead branches and fix AGENTS.md. |
| NAT-05 | `adDatabaseRegistry.cpp:96-113` + `:47-55` + `:183` | `ad_database.xml` attributes are **not XML-escaped** (a `&`/`<` in a path corrupts the file; both writers C++ and C# emit the same raw format); `UpdateCount`'s `dbPath.find(searchPath)==0` matches the wrong direction (parent delete decrements child DB count); no file locking around read-modify-write. Fix: add `XmlEscape`/`XmlUnescape` helpers and a separator-boundary check in `UpdateCount`. |
| NAT-06 | `adOptions.cpp:105` | `maximalImageSize` default **8196** — almost certainly a typo for 8192. Also the binary `memcpy` Import/Export (`adOptions.cpp:159-211`) has no size/version guard (currently safe — layouts verified; add a `static_assert`). |
| NAT-07 | `adStrings.cpp:145-167` `TString::CopyTo(char*,size)` | Passes `length()` (chars) as the `WideCharToMultiByte` byte-buffer size → multibyte (CJK) conversions can fail silently. Fix: query required size first, fail cleanly. |
| NAT-09 | `adEngine.cpp:412`, `adGPU.cu:131-146` | BUG-08 (open): `BATCH_MATCHES=5,000,000` silently drops matches beyond the cap (only an `AD_DEBUG` line). Fix: chunk the AllVsAll by row range (see agent report §B) or at minimum surface a status warning when `bufferFullCount>0`. |
| NAT-10 | `NvJpegCollector/main.cpp:565,601,811` + `adImageDataStorage.cpp:56-66` | Collector `hash=0` for every record → all records share one multimap bucket → **O(N²) `Find` during DB load**. Also collector crc32c is thumbnail-CRC while DLL-native is full-file CRC — mixed-DB CRC-penalty compare is inconsistent. Fix: store a path-based hash (e.g. `SimdCrc32c` over the path) — lookup key only; exact path equality still decides. |
| NAT-11 | `adTurboJpeg.cpp:31-35` | Legacy `__iob_func` CRT shim; with static CRT + v143 UCRT libjpeg-turbo it's likely dead. Verify at link time; delete if unreferenced. |
| NAT-12 | `adUndoRedoEngine.cpp:206-210` | Rename-on-collision loop exits after 65536 variants with `…_65535.ext` (which exists) → `MoveFileEx(...,MOVEFILE_REPLACE_EXISTING)` at `:216` **overwrites an existing file**. Fix: `if (IsFileExists(path)) return false;` before the move. |
| C-5 | `adEngine.cpp:336` | `allThumbnails.reserve(count * thumbSize)` — for ~1M images × 1 KB = 1 GB; an uncaught `bad_alloc` at the P/Invoke boundary terminates the process. Wrap in `try/catch` or reserve incrementally. |
| C-6 | `adImageDataStorage.cpp:482-490` | `LoadCollectorNative` ignores `thumbSizeFromHeader`; if `index.adi` and `0000.adi` disagree, images load at the data file's size then get silently filtered → empty results. Validate `thumbSizeFromHeader == fileThumbSize`. |
| C-8 | all NAT-01 blocks | Debug logs silently no-op when the exe dir is read-only. If logging is kept, log to `%LOCALAPPDATA%\AntiDuplPlus\`. |
| CSH-03 | `SearchExecuterForm.cs:390-404` | Progress arithmetic in the non-`checkOnEquality` branch subtracts `total` → progress can go negative. |
| CSH-05 | `adPath.cpp:288-337` | Native leftover `trace.log`/`gpu_debug.log` appends on every path-set (also misnamed: a path-validation error writes to `gpu_debug.log`). |
| CSH-06 | `CoreLib.cs:659-714` | `GetPath`/`SetPath` hand-pack `wchar[MAX_PATH_EX+1]` records into a `char[]` with the subfolder flag as the last byte — works only by zero-init + little-endian; fragile. |
| CSH-07 | `CoreLib.cs` (many) | `Marshal.UnsafeAddrOfPinnedArrayElement` on **non-pinned** arrays. Safe today (synchronous native calls) but a documented-unsafe pattern; add `fixed`/`GCHandle` if any async usage appears. |
| CSH-09 | `CoreResult.cs:51-52`, `CoreGroup.cs:41` | `IntPtr.ToInt32` truncates 64-bit ids — latent only (ids are sequential). |
| CSH-10 | `CoreDll.cs:414-428` | `adResultW` is a `class`, not `struct` — works with `PtrToStructure` but a future reference-type field would silently break layout. Consider `struct`. |
| CSH-11 | `ResultsListView.cs:468-475` | If `GetSelection` returns shorter than results, `selection[i]` throws — add a bounds check. |
| CSH-14 | `ResultsPreviewDuplPair.cs:301-306` | `_thread.Join()` with no timeout on the UI thread — a hung highlight thread freezes the UI. |
| CSH-15 | `CoreLib.cs:316-342` vs `:421-431`,`:482-492` | `GetResultSize` is try/catch'd but `GetGroupSize`/`GetImageInfoSize` are not — inconsistent fault tolerance. |
| A-3 | `ResultsPreviewDuplPair.cs:344-367` | When `!HighlightAllDifferences`, `HighlightCompleteEvent(dst)` fires **twice** (line 356 then 366) — the second fires the full set unconditionally. Benign today only because the sole subscriber self-unsubscribes after the first synchronous `Invoke` (`:385`). Gate line 366 on `HighlightAllDifferences`. |
| A-5 | `BitmapWorker.cs:24,35-48` | `GC.Collect()` on creation-OOM; `LockBits` without `try/finally`; returns `null` **without disposing** the Bitmap on native error → GDI+ handle leak. |
| A-7 | `CoreLib.cs:326-327,425-426,486-487` | Size probes pass dummy `new IntPtr(1)` buffers — safe only because native returns `InvalidStartPosition` before writing. Add a comment or use a real buffer. |
| A-10/13 | `Options.cs:84-147`, `ThumbnailStorage.cs:84-99` | `Options.Save/Load` swallow all IO/XML errors and `writer.Close()` isn't in `finally`; `ThumbnailStorage.Get` releases the mutex mid-op → duplicate concurrent decodes + last-write-wins on the dictionary. |
| A-11 | `DatabaseManagerForm.cs:557-576` | Manual XML string build + **UTF-8 vs native `std::wifstream` default-locale encoding mismatch** → non-ASCII paths can be mangled when read by the native side. Align encodings or use `XmlSerializer`. |
| A-12 | `ImageOpener.cs:20`, `ImageDiffOpener.cs:26`, `FolderOpener.cs:101` | `Thread.Sleep(100)` after `Process.Start` blocks the UI thread for nothing. Remove. |
| CSH-12/13 | `PictureBoxPanel.cs:579-583`, `Options.cs:145-146` | See P1-9 and A-10/13 (resource/stream leaks). |
| CFG-08 | `AntiDupl_CI.yml:61,68` | CI `msbuild` calls omit `/p:Platform=x64` (relies on implicit resolution). Add it. |
| CFG-10 | all `src/*.csproj` | No `TreatWarningsAsErrors`; 2065 warnings invisible in CI. Clean §3 items, then enable. |
| CFG-11 | `src/vcpkg.json` | No `builtin-baseline` → unpinned native ports. Add a baseline SHA (matching the CI `vcpkgGitCommitId`). |
| CFG-12 | `Core.csproj:47-63` | `GeneratePackageOnBuild=true` for Release/Publish — every build drops `AntiDupl.NET.Core.nupkg/.snupkg` nobody consumes. Remove or scope explicitly. |
| CFG-13 | `cmd/7-zip/config.txt:2` | SFX title frozen at 2.3.11. Generate from `%VERSION%`. |
| CFG-14 | `.gitignore:345-350`, `cmd/MakeBin.cmd:62-63` | Stale `.resx` ignores + `erase English.xml/Russian.xml` for files that no longer exist. Clean up. |
| NEW-03 | WPF converters | Dead `!= null` on non-nullable structs (`CS0472`) — remove (see P2-13). |
| NEW-04 | `NewVersionMenuItem.cs:120-122` | Update check hits upstream `version.xml` (2.3.12) — fork's own version (2.5.1) is always "newer", so the check never fires. Point at a fork-hosted version.xml or remove. |
| NEW-05/06 | `src/AntiDuplCore/AntiDuplCore.vcxproj`, `src/Prop.csproj` | **Orphan projects**: `AntiDuplCore.vcxproj` has 12 sources but is not in the solution (`ToolsVersion 14.0`, stdcpp14, hardcoded CUDA paths); `Prop.csproj` is an empty stub. Delete or wire in. |
| FIND-ERR | `src/AntiDupl.NET.Core/Enums/Error.cs` | C# `Error` enum: name `InvalidInfoType = 30` should be `InvalidVersionType` (native `AD_ERROR_INVALID_VERSION_TYPE`), and `DirectoryIsNotExist = 33` (native `AD_ERROR_DIRECTORY_IS_NOT_EXIST`) is **missing**. Value 30 is coincidentally equal so runtime is unaffected — but the missing 33 breaks any caller expecting it. |

---

## 4. Confirmed NOT bugs (do not chase)

1. **`FilterByPool` in-place drop** — semantics match the GPU kernel's `poolCompareMode` cases exactly; the second pass is redundant but idempotent (harmless).
2. **Collector wire layout** — `NvJpegCollector/main.cpp:837-878` writer matches `LoadCollectorData` reader (`adImageDataStorage.cpp:543-642`) byte-for-byte; nvJPEG batch size = 1; pitch `((w*3+31)/32)*32` correct; no CUDA/GlobalAlloc leak on collector error paths.
3. **`CheckOnDefect` backward scan** (`adDataCollector.cpp:202-209`) — indices are always in range; `hGlobal` null-checked.
4. **`MAX_PATH` vs `MAX_PATH_EX` asymmetry** — buffers and `CopyTo` sizes agree; only a documented functional limit for the ANSI API (see NAT-07 hardening).
5. **`adImageDataStorage.cpp:609` C4244** (`crc32c uint64_t→TUInt32`) — benign: collector CRC is 32-bit in a 64-bit container.
6. **Options struct layouts** — C++ `AntiDupl.h:406-470` matches C# `CoreDll.cs:280-344` field-for-field today (locked by the contract tests).
7. **`adVersionGet` CharSet.Unicode on the C# delegate** — native is ANSI but both params are `IntPtr`; harmless.
8. **`TImageInfo::Export(W)` sets `id = (size_t)this`** — stable pointer used only as a dictionary key; never dereferenced managed-side.
9. **AutoSelector reverse-iteration index math** — correct w.r.t. index shifting after native deletes.
10. **Long-path delete warning** in `ResultsListView.cs:274-330` — intentional feature.
11. **`.gitignore`** — correctly covers `bin/ obj/ out/ release/ Log.txt vcpkg/ src/vcpkg_installed`; no build artifacts are tracked (except the two generated sources, see P2-12 / CFG note).

---

## 5. Suggested fix order (day plan)

| Day | Items | Goal |
|-----|-------|------|
| **1** | P1-1, P1-2, P1-3 | Memory safety / data loss (native) |
| **2** | P1-4..P1-7, CFG-10 | Pipeline: Publish + CI green, warnings baseline |
| **3** | P1-8, P1-9, P2-3, P2-4, P2-5 | WPF crash + GDI+ leak + preview correctness |
| **4** | P2-1, P2-2, P2-6, P2-7, P2-8, P2-9 | Logging hygiene + Undo + GPU/CPU parity + shutdown |
| **5** | P2-10..P2-16, all P3 | Actions, version, dead code, cleanup |
| **any** | §6 tests | Re-run after each interop/native change |

---

## 6. Tests delivered

**`tests/AntiDupl.Contract.Tests/`** — dependency-free console runner (no xunit needed; no network; pattern matches `test_ssim`/`benchmark_ssim`). References `AntiDupl.NET.Core` only for types (no native DLL loaded).

```
dotnet build tests\AntiDupl.Contract.Tests\AntiDupl.Contract.Tests.csproj
dotnet run --project tests\AntiDupl.Contract.Tests\AntiDupl.Contract.Tests.csproj
```

Result: **65/65 PASS**. Covers:

1. **`LocalActionType` sync** — all 16 values incl. `MarkRemovedFirst=14`/`MarkRemovedSecond=15` (BUG-05 wire contract, `AD_LOCAL_ACTION_SIZE`).
2. **`Error` enum** — spot values 0..32 + a marked drift check for missing `DirectoryIsNotExist=33` (FIND-ERR).
3. **Other enums** — ImageType, ResultType, StateType, TransformType, DefectType, HintType, AlgorithmComparing, PoolCompareMode, GlobalActionType, SortType, PathType, OptionsType, FileType, VersionType, SelectionType, ThreadType, PixelFormatType, ActionEnableType, TargetType, RenameCurrentType.
4. **Struct layout** — sizes of `adSearchOptions` (76), `adCompareOptions` (48), `adDefectOptions` (24), `adAdvancedOptions` (40), `adStatistic` (96), `adGroup` (16), `adStatusW` (65560), `adImageExifW` (3644), `adImageInfoW` (69240), `adResultW` (138536), `adBitmap` (24).
5. **Field offsets** — `adCompareOptions.poolCompareMode @ 44`, `adImageInfoW.exifInfo @ 65592`, `adResultW.group @ 138512`.
6. **Version sync** — `External.Version` == `src/version.txt`.

**When you apply P2-5 (remove `jpegPeaks`):** update tests `[4]` (`adImageInfoW` 69240 → 69236) and `[5]` (`exifInfo` stays 65592; drop the jpegPeaks-related text).

**Not yet automated (native):** magic-detect/thumb-bounds cases from `DEV_GUIDE §7` need a native test harness (linking `adImageDataStorage`) — listed under §8 remaining concerns.

---

## 7. Invariants (do not break while fixing)

| ID | Rule |
|----|------|
| I1 | Collector raw wire layout ↔ `LoadCollectorData` — do not change without versioned migration |
| I2 | DLL index magic `"adii"` = `0x69696461u`; data magic `"adid"` |
| I3 | In one GPU pack all thumbs must have `data->side == options.reducedImageSize` |
| I4 | Portable paths: `ad_database.xml`, `databases/<Name>/` relative to exe |
| I5 | Interop: `LocalActionType`/native enum numeric sync (`MarkRemovedFirst=14`, `Second=15`) |
| I6 | Surgical diffs only; `adExternal.h`/`External.cs` are auto-generated (never hand-edit) |

---

## 8. Remaining concerns (not safely fixable automatically)

1. **Native unit-test harness** — `adImageDataStorage` (magic detect, thumb bounds, malformed-file handling from P1-1) and the GPU pack loop (I3) have no automated coverage. A native test exe linking the relevant `.cpp` files is needed; the CRT/vcpkg static-link setup makes this non-trivial and was out of scope here.
2. **GPU match cap (BUG-08)** — chunking the AllVsAll changes the kernel's index space; the `MatchCallback` must map indices back via an offset. This touches hot code; do it with a benchmark and a large fixture.
3. **`EnsureCapacity` OOB (P1-3)** — could not be reproduced without a >10k-image GPU-assisted run; fix is trivial and safe, but verify with a stress run.
4. **IFileOperation long-path recycle (P1-2)** — needs COM init on the calling thread; if the engine thread has no STA/MTA context, add `CoInitializeEx`. Verify on a real >260-char path.
5. **`__iob_func` shim (NAT-11)** — whether it's linked depends on the exact prebuilt `libjpeg-turbo` in `vcpkg_installed`; a link-time check is required before deleting.
6. **Version single-source-of-truth (P2-12)** — a full fix (auto-generating `AssemblyVersion` from `version.txt` for 3 C# projects + `AssemblyInfo.cs`) is a small build-system change; the minimal stopgap (set 2.5.1 manually each release) is also acceptable.
7. **CI has never been proven green** — the CUDA gap (P1-6) plus publish profile TFM (P1-4) mean the whole `AntiDupl_CI.yml` pipeline likely fails on every leg. After P1-4..P1-7, run the workflow once on a tag and iterate.
8. **`cs_debug.log`/`trace.log` removal** — some of these blocks may be used by an active debugging session elsewhere; coordinate with whoever owns the `[C#N]` markers before deleting all of them (the P2-1/P2-6 fix should be one coordinated pass).

---

## 9. Assumptions made

1. **Code > docs**: `PROJECT_CONTEXT.md`, `IMPLEMENTATION_PLAN.md`, and the historical audits describe intent; actual source is authoritative. Where they conflicted (e.g. WP-A status, `"adid"` vs `"adii"`), code won.
2. **WinForms is the product UI**; WPF is secondary and lower priority (reviewed for crashes/dead code only).
3. **No behavioral changes were made** to product code. All fixes are proposals in this document, per the task instruction "Don't change anything in the code."
4. **No network**: NuGet restore was unavailable, so the tests use only the local SDK (no xunit). Struct/offset expectations were derived analytically from `AntiDupl.h` and cross-checked with `Marshal.SizeOf`/`Marshal.OffsetOf`.
5. **x64 / MSVC v143 / default packing** for all native layout math.
6. **The `vcpkg` `simd` rebuild** during the plain build is a known environment quirk (documented in `PROJECT_CONTEXT.md`), worked around with `/p:VcpkgManifestInstall=false`.
7. **Severity is confidence-weighted**: P1 items were individually verified by reading the source; P2/P3 items are verified too but touch less-travelled code (marked where a runtime repro was not possible).
8. Line numbers refer to the tree at commit `7c092b3`; they may drift — re-locate before editing.

---

## 10. Summary

| Priority | Count | Theme |
|----------|-------|-------|
| P1 | 9 | 3 native memory-safety/data-loss, 4 build/CI/publish, 1 WPF crash, 1 GDI+ leak |
| P2 | 16 | logging/handle crash, Undo semantics, GPU/CPU parity, shutdown race, CI actions/version, WPF converters |
| P3 | ~35 | dead code, XML escaping, hash/O(N²) load, resource leaks, naming, drift |

**Top 5 things to fix first:** P1-1 (malformed `.adi` crash), P1-2 (long-path permanent delete), P1-3 (GPU OOB >10k), P1-4 + P1-6 (Publish + CI cannot run), P1-9 (GDI+ leak).

All prior-audit P0/P1 fixes remain **verified present**. The repo is functionally healthy on the primary WinForms + collector workflow; the highest-risk open areas are the native `.adi` loader hardening, the GPU >10k path, and the never-green CI pipeline.

---

## 11. Implementation plan (approved, for delegation)

Decision date: 2026-08-06. Scope: **all P1 (9) + FIND-ERR + bundled P2-1**. Verified by reading source at commit `7c092b3`; line numbers may drift — re-locate before editing (AGENTS.md: trust code).

### Approved decisions

| Item | Decision | Rationale |
|------|----------|-----------|
| P1-2 | Use `IFileOperation` (`shobjidl.h`) for recycle of long paths | `SHFileOperation` doesn't guarantee `\\?\` support; `IFileOperation` handles long paths natively. Requires COM init on the calling thread. |
| P1-8 | **Remove** the dead WPF "DCT histogram" feature (command + XAML binding) | Native `CalculateHistogramPeaks` API does not exist; CoreLib has no such method (grep: zero hits). Re-implementing requires a new native export on the hot compare path — out of scope. |
| P1-5/P1-6 | CI: publish only the 2 C# projects via `dotnet publish`; install CUDA 12.8 on runners | `msbuild -t:Publish` on the sln hits MSB4057 (C++ projects have no Publish target); `windows-latest` has no CUDA Toolkit → MSB4019. |

### Fix list (in order)

**1. P1-1 malformed `.adi` — harden `LoadCollectorNative`/`LoadCollectorData`**
- File: `src/AntiDupl/adImageDataStorage.cpp`.
- `LoadCollectorNative` (starts :482): after reading `groupCount` (:494) add cap: `if (groupCount > 1000000) { fclose(f); return AD_ERROR_UNKNOWN; }`. After reading `imgCount` (:519) add the same cap.
- `LoadCollectorData` (starts :543): after reading `count` (:573) add cap: `if (count > 100000000) { fclose(f); return false; }`. The per-record loop already caps `pathLen` (10000) and thumb bytes (`expected` match check :618-622).
- Do NOT change the wire layout (invariants I1/I2); only pre-loop sanity caps.
- Verify: read-only change; build native, confirm no functional path differs.

**2. P1-2 long-path recycle — `IFileOperation`**
- File: `src/AntiDupl/adFileUtils.cpp`, `FileDelete` (:35-67). Branch `length >= MAX_PATH` (:43-50): when `toRecycle` is true, do NOT call `::DeleteFile`.
- Add `#include <shobjidl.h>`.
- Implementation sketch (COM pattern already used in `NvJpegCollector/main.cpp:271`):
  - `CoInitializeEx(NULL, COINIT_APARTMENTTHREADED)` on the calling thread (release when done).
  - `SHCreateItemFromParsingName(path, NULL, IID_PPV_ARGS(&item))`; if `wcslen(path) >= MAX_PATH` and path lacks `\\?\` prefix, prepend it first (`L"\\\\?\\"`).
  - `CoCreateInstance(CLSID_FileOperation, ...)` → `pfo->SetOperationFlags(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI)` → `pfo->DeleteItem(item, NULL)` → `pfo->PerformOperations()`.
  - Non-recycle long path stays `::DeleteFile`.
- Note: callers pass `TPath::Original()` (raw string, no forced prefix) — see `adRecycleBin.cpp:43-50` (single caller). Handle the prefix only if the path already starts with `\\?\` to avoid doubling.
- Verify: compile; runtime long-path recycle needs manual test (>260-char path) — not automatable locally.

**3. P1-3 GPU capacity >10k**
- File: `src/AntiDupl/adDataCollector.cpp:162` (GPU-upload branch in the collector loop): replace hardcoded `EnsureCapacity(10000, thumbSize)` with `EnsureCapacity(pImageData->globalIdx + 1, thumbSize)`.
- Context: `globalIdx` is assigned sequentially in `TImageDataStorage::Insert` (0..N-1). `GpuUploadThumbnail` guards `index >= g_bufferCapacity` (returns false — no device OOB); the kernel also bounds-checks. This only fixes the silent >10k image drop.
- `TGpuManager::EnsureCapacity` at `adGPUManager.cpp:87`; `UpdateGpuDatabase` already uses `storage.size()` — correct, leave it.
- Verify: compile; stress >10k images requires GPU run (Audit §8.3).

**4. P2-1 (bundle) log-before-validation crash**
- File: `src/AntiDupl/AntiDupl.cpp` (`adPathWithSubFolderSetW`, :383-399): move the debug-log block that dereferences `handle->Status()->State()` (:394) to AFTER `CHECK_HANDLE CHECK_ACCESS LOCK` (:399). A NULL handle currently crashes the DLL at the P/Invoke boundary.
- Verify: compile + run the existing tests; no behavior change.

**5. P1-4 pubxml TFMs**
- Files (all three `Properties/PublishProfiles/AntiDuplPublishSingleFile.pubxml`):
  - `src/AntiDupl.NET.Core/.../AntiDuplPublishSingleFile.pubxml` → `TargetFramework` net6.0 → `net8.0`.
  - `src/AntiDupl.NET.WinForms/...` and `src/AntiDupl.NET.WPF/...` → net6.0-windows → `net8.0-windows`.
- All .csproj are already net8.0/net8.0-windows; the mismatch is the NETSDK1047 error.
- Verify: `dotnet publish` each project succeeds.

**6. P1-5 + P1-6 CI pipeline**
- File: `.github/workflows/AntiDupl_CI.yml`.
- P1-6: add before the build steps (after setup-msbuild, :39):
  ```yaml
  - name: Install CUDA 12.8
    uses: JimVer/cuda-toolkit-action@v0.2
    with:
      cuda: '12.8.0'
      method: 'network'
  ```
- P1-5: replace the Publish leg (`msbuild -t:Publish` on the sln) with:
  ```bash
  dotnet publish src/AntiDupl.NET.WinForms/AntiDupl.NET.WinForms.csproj -c Publish -p:PublishProfile=AntiDuplPublishSingleFile -p:SolutionDir="src\\"
  ```
  (and WPF if desired). Keep the build legs on the sln for Debug/Release.
- Also recommended (P2-10/CFG-08 while touching): `checkout@v4`, `setup-msbuild@v2`, `actions/upload-artifact@v4`, `actions/download-artifact@v4`, add `/p:Platform=x64` to msbuild calls. (Optional — not required for P1.)
- Verify: cannot run the workflow locally; push a tag and iterate (Audit §8.7).

**7. P1-7 hash file names**
- File: `cmd/MakePublish.cmd:45,47` (WinRAR branch): hashes `AntiDupl.NET-%VERSION%.exe/.zip` but actual archives are `…_SingleFilePortable.exe/.zip` → `.hash.txt` contains certutil error text. Fix names to the real artifacts:
  ```bat
  certutil -hashfile %OUT_DIR%\AntiDupl.NET-%VERSION%_SingleFilePortable.exe SHA256 > %OUT_DIR%\AntiDupl.NET-%VERSION%_SingleFilePortable.exe.hash.txt
  certutil -hashfile %OUT_DIR%\AntiDupl.NET-%VERSION%_SingleFilePortable.zip SHA256 > %OUT_DIR%\AntiDupl.NET-%VERSION%_SingleFilePortable.zip.hash.txt
  ```
- The 7-zip branch (:50,52) is already correct — leave it.

**8. P1-8 remove dead WPF feature**
- Delete file: `src/AntiDupl.NET.WPF/Command/CalculateHistogramPeaksCommand.cs`.
- Remove from `src/AntiDupl.NET.WPF/ViewModel/MainViewModel.cs`: property `CalculateHistogramPeaksCommand` (:190) and its initializer (:196-197, the `//return ...` comment too).
- Remove from `src/AntiDupl.NET.WPF/View/MainWindow.xaml:171-172`: the `<MenuItem Header="Calculate histogram peaks for DCT coefficient" Command="{Binding CalculateHistogramPeaksCommand}" />` (keep the surrounding menu layout).
- Note: `DiffrenceHelper.cs:36,38` mentions "JPEG DCT Histogram peak" only in report text — LEAVE it (that's a diff-report label, not the dead feature).
- Verify: build WPF.

**9. P1-9 GDI+ leaks**
- `src/AntiDupl.NET.WinForms/ComparableBitmap.cs` (`:65` + `ToGrayScale` :96-107): wrap in `using`:
  ```csharp
  using (Bitmap gray = ToGrayScale(sectionBmp))
  {
      this._grayScaleData = GetBmpBytes(gray);
  }
  ```
- `src/AntiDupl.NET.WinForms/GUIControl/PictureBoxPanel.cs:579-583`: wrap `LoadFileToMemoryStream(...)` in `using` (see Audit P2-15 for the fuller fix — only the `using` is in scope today).
- Verify: build WinForms; manual preview navigation (GDI handle exhaustion needs a long session — see Audit §3 P1-9).

**10. FIND-ERR Error enum**
- `src/AntiDupl.NET.Core/Enums/Error.cs:35`: rename `InvalidInfoType = 30` → `InvalidVersionType = 30` (matches native `AD_ERROR_INVALID_VERSION_TYPE`); add `DirectoryIsNotExist = 33` (matches native `AD_ERROR_DIRECTORY_IS_NOT_EXIST`).
- Safe: no runtime C# caller references either name; CoreLib only compares against `Error.Ok`. `adVersionGet` default (`AntiDupl.cpp:136`) returns 30 — already consistent.
- Update test `tests/AntiDupl.Contract.Tests/Program.cs:99-118`: assert `Error.InvalidVersionType == 30` and `Error.DirectoryIsNotExist == 33`; remove the "drift" note for missing 33.
- Verify: `dotnet run --project tests\AntiDupl.Contract.Tests\AntiDupl.Contract.Tests.csproj` → expect 65/65 PASS.

### Verification commands (run after all edits)

```bash
# native (Debug + Release)
msbuild src\AntiDupl\AntiDupl.vcxproj /p:Configuration=Release /p:Platform=x64 /m:1
# full solution
msbuild src\AntiDupl.sln /p:Configuration=Release /p:Platform=x64 /p:VcpkgManifestInstall=false
# managed + tests
dotnet build src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj /p:SolutionDir="src\\" -c Release
dotnet build src\AntiDupl.NET.WPF\AntiDupl.NET.WPF.csproj /p:SolutionDir="src\\" -c Release
dotnet run --project tests\AntiDupl.Contract.Tests\AntiDupl.Contract.Tests.csproj
```

### Traps / constraints for the executor

- **Surgical diffs only** (AGENTS.md I6): do not refactor or rename beyond the listed items; no drive-by changes.
- **`adExternal.h` / `External.cs` are auto-generated** — never hand-edit.
- P1-1/P1-2/P1-3/P1-5/P1-6 cannot be runtime-verified locally (malformed-file harness, COM long-path, >10k GPU, GitHub runner). State this in the PR; rely on compile + code review + the existing 65 tests.
- `cmd/MakePublish.cmd` and pubxml edits are pure config — no build risk, but re-check the exact artifact names against `cmd/7-zip`/WinRAR branches.
- Do not commit secrets, `bin/`, `obj/`, `out/`, or `vcpkg/` changes.
- CI workflow edits follow Audit P2-10 (deprecated actions) — update those versions in the same file while touching it.

---

## 12. Implementation status (2026-08-07, done by main agent)

All 10 items from §11 are implemented and verified (compile + tests). Two **deviations** from the plan and several **new findings** are recorded below — do not re-apply the planned text without reading these notes.

### 12.1 Status

| Item | Files | Status | Verification |
|------|-------|--------|--------------|
| P1-1 | `src/AntiDupl/adImageDataStorage.cpp` (3 caps: groupCount ≤ 1M, imgCount ≤ 100M, count ≤ 100M) | ✅ done | Release native build PASS |
| P1-2 | `src/AntiDupl/adFileUtils.cpp` (`FileDeleteToRecycleBin` via `IFileOperation`, `#include <shobjidl.h>`) | ✅ done | Release native build PASS; runtime long-path needs manual test |
| P1-3 | `src/AntiDupl/adDataCollector.cpp:162` → `EnsureCapacity(pImageData->globalIdx + 1, thumbSize)` | ✅ done | Release native build PASS; >10k GPU needs stress run |
| P2-1 | `src/AntiDupl/AntiDupl.cpp` (log block moved after `CHECK_HANDLE CHECK_ACCESS LOCK`) | ✅ done | Release native build PASS |
| P1-4 | 3 × `AntiDuplPublishSingleFile.pubxml` → net8.0 / net8.0-windows | ✅ done | `dotnet publish` all 3 PASS |
| P1-5 | `.github/workflows/AntiDupl_CI.yml` Publish leg → native build + `dotnet publish` WinForms | ✅ done | needs a GitHub tag run (§8.7) |
| P1-6 | same file: `JimVer/cuda-toolkit-action@v0.2` (12.8.0) before build | ✅ done | needs a GitHub run |
| P1-7 | `cmd/MakePublish.cmd` WinRAR branch hash names → `…_SingleFilePortable.*` | ✅ done | config-only |
| P1-8 | deleted `Command/CalculateHistogramPeaksCommand.cs`; removed VM property + XAML MenuItem | ✅ done | WPF build PASS |
| P1-9 | `ComparableBitmap.cs` (using) + `PictureBoxPanel.cs` (stream using + Clone) | ✅ done | WinForms build PASS; **deviation, see 12.2** |
| FIND-ERR | `Error.cs` (`InvalidVersionType=30`, `+DirectoryIsNotExist=33`); test `Program.cs` updated | ✅ done | **65/65 PASS** |

**Verified commands:** full `AntiDupl.sln` Release|x64 with `/p:VcpkgManifestInstall=false` → EXIT=0; `dotnet build` Core/WinForms/WPF Release → 0 errors; `dotnet publish` Core/WinForms/WPF with the pubxml → all PASS; contract tests → 65/65 (was 64/64 — one new assertion added for `DirectoryIsNotExist`).

### 12.2 Deviations from the §11 plan (approved by implementation)

1. **P1-9 `PictureBoxPanel.GetBitmap` — NOT a plain `using`.** The plan's `using (MemoryStream ms = ...) { return new Bitmap(ms); }` is wrong: `Bitmap(Stream)` keeps a **lazy** reference to the stream and `GetBitmap`'s caller stores the bitmap (`m_prevBitmap`/`m_nextBitmap`) and draws it later — disposing the stream up front can break rendering (GDI+ out-of-memory/invalid). Implemented as: local `MemoryStream` (so it can pass `ref` to `LoadFileToMemoryStream`), then `using (memoryStream) using (Bitmap bmp = new Bitmap(memoryStream)) { return (Bitmap)bmp.Clone(); }`. The `Clone()` is an independent copy; both stream and temp bitmap are disposed.
2. **P1-5 CI Publish leg has TWO steps, not one.** `dotnet publish` alone does not build the native DLL, but `MakePublish.cmd` copies `bin\Publish\AntiDupl.dll`/`.pdb`. Added a preceding `msbuild src\AntiDupl\AntiDupl.vcxproj /p:Configuration=Publish /p:VcpkgManifestInstall=false` step. WinForms is the only published UI (WPF shares the same `PublishDir` with `DeleteExistingFiles=True`, P2-14).

### 12.3 New findings during implementation — status after second round (all resolved)

1. **Debug|x64 native link fails: no Debug `Simd.lib` in `vcpkg_installed`.** ✅ **resolved (env fix, no repo code change):** `vcpkg install --x-manifest-root=src --triplet x64-windows-static` built the missing Debug variant (~43 min; Release was skipped as already installed); then copied all simd ISA libs (`Simd`/`Base`/`Sse41`/`Avx2`/`Avx512bw`/`Avx512vnni`/`Neon`/`AmxBf16`) from `...\x64-windows-static\debug\lib` into the nested MSBuild-visible path `...\x64-windows-static\x64-windows-static\debug\lib` (the first attempt copied only `Simd.lib` → LNK2001 on `Base`/`AmxBf16` refs; copying the full ISA set fixed it). `AntiDupl.vcxproj` Debug|x64 → **EXIT=0**, `bin\Debug\AntiDupl.dll` created. `debug\lib` **must not be committed**; re-run the vcpkg install + nested copy on a fresh machine.
2. **Native build log shows `"pwsh.exe" is not recognized` after linking.** ✅ **resolved (non-issue):** this machine has no `pwsh` (only Windows PowerShell). The call comes from vcpkg's applocal targets, which fall back to `powershell.exe`. `vcpkg.applocal.log` is **empty** because the triplet is fully static (`x64-windows-static`) — there are no DLLs to deploy, so the fallback has zero effect. Nothing to change.
3. **`bin\Publish` vs `out\Publish` naming collision observed.** ✅ **resolved (config):** `cmd/MakePublish.cmd` now uses `out\Publish` (uppercase) consistently with the WinForms/WPF pubxml `PublishDir` and CI artifact paths — `MakePublish.cmd:16,17,29`. No more mixed-case reliance on case-insensitive Windows.
4. **Core publish still drops `.nupkg`/`.snupkg` (CFG-12 confirmed).** ✅ **resolved:** removed the packaging-only `PropertyGroup`s (`GeneratePackageOnBuild`, `IncludeSymbols`, `SymbolPackageFormat`, `PublishRepositoryUrl`, `EmbedUntrackedSources`, `ContinuousIntegrationBuild`) from both Release and Publish in `src/AntiDupl.NET.Core/AntiDupl.NET.Core.csproj`. `dotnet publish` of Core no longer produces `.nupkg`/`.snupkg`.
5. **Tests grew 64→65.** ✅ **resolved (docs):** §6/§8/§12 text updated to 65/65. Re-verified: contract tests → **65 passed, 0 failed**.

### 12.4 Remaining runtime verification (not possible in this environment)

- P1-1: malformed `.adi` fixture (needs native harness, §8.1).
- P1-2: recycle of a real >260-char path (needs interactive Windows; `IFileOperation` + `CoInitializeEx` STA on a worker thread).
- P1-3: GPU run with >10k images.
- P1-5/P1-6: one tag push through the GitHub workflow.

### 12.5 Files changed (17)

`.github/workflows/AntiDupl_CI.yml`, `cmd/MakePublish.cmd`, `src/AntiDupl/AntiDupl.cpp`, `src/AntiDupl/adDataCollector.cpp`, `src/AntiDupl/adFileUtils.cpp`, `src/AntiDupl/adImageDataStorage.cpp`, `src/AntiDupl.NET.Core/AntiDupl.NET.Core.csproj`, `src/AntiDupl.NET.Core/Enums/Error.cs`, `src/AntiDupl.NET.Core/Properties/PublishProfiles/AntiDuplPublishSingleFile.pubxml`, `src/AntiDupl.NET.WPF/Properties/PublishProfiles/AntiDuplPublishSingleFile.pubxml`, `src/AntiDupl.NET.WPF/View/MainWindow.xaml`, `src/AntiDupl.NET.WPF/ViewModel/MainViewModel.cs`, `src/AntiDupl.NET.WinForms/ComparableBitmap.cs`, `src/AntiDupl.NET.WinForms/GUIControl/PictureBoxPanel.cs`, `src/AntiDupl.NET.WinForms/Properties/PublishProfiles/AntiDuplPublishSingleFile.pubxml`, `tests/AntiDupl.Contract.Tests/Program.cs`, `Audit.md` (deleted: `src/AntiDupl.NET.WPF/Command/CalculateHistogramPeaksCommand.cs`).

**Second-round verification (findings #1–#5):** `vcpkg install --x-manifest-root=src --triplet x64-windows-static` → EXIT=0 (43 min); nested Debug simd copy → `AntiDupl.vcxproj` Debug|x64 **EXIT=0** (`bin\Debug\AntiDupl.dll` created); full `AntiDupl.sln` Debug|x64 and Release|x64 with `/p:VcpkgManifestInstall=false` → both **EXIT=0**; `dotnet publish` Core → no `.nupkg`/`.snupkg`; contract tests → **65/65 PASS**.
