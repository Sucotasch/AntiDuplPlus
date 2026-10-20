# AntiDuplPlus

[![CI](https://github.com/Sucotasch/AntiDuplPlus/actions/workflows/AntiDupl_CI.yml/badge.svg)](https://github.com/Sucotasch/AntiDuplPlus/actions/workflows/AntiDupl_CI.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078d4)

**English** · [Русский](README.ru.md)

AntiDuplPlus finds duplicate and similar images in large photo collections. It indexes
your pictures into a database once, compares every image with every other image on the
NVIDIA GPU, and shows the matches side by side with a similarity score — you decide which
copies to keep. It is a fork of [AntiDupl.NET](https://github.com/ermig1979/AntiDupl)
with a GPU pipeline (nvJPEG decoding, CUDA comparison) and support for multiple
databases.

![AntiDuplPlus main window](docs/images/main-window.png)

*Search results for a small demo library: each row is a pair of images with its
similarity score and the thumbnails of both files.*

## Contents

- [What it does](#what-it-does)
- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Databases](#databases)
- [Search presets and algorithms](#search-presets-and-algorithms)
- [Working with results](#working-with-results)
- [Supported formats](#supported-formats)
- [Command line](#command-line)
- [Files and settings](#files-and-settings)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [Differences from the original AntiDupl.NET](#differences-from-the-original-antiduplnet)
- [Limitations](#limitations)
- [License](#license)
- [Acknowledgements](#acknowledgements)

## What it does

Photo libraries grow: copies of the same picture end up in different folders,
screenshots get re-saved, images are resized for sharing, and burst shots produce several
nearly identical frames. Finding this by hand does not scale beyond a few thousand files.

AntiDuplPlus looks for four kinds of matches:

- **exact copies** — the same image stored twice;
- **re-compressed or resized copies** — the same picture saved with another JPEG quality
  or scaled to another size;
- **transformed copies** — rotated or mirrored versions of the same picture;
- **similar images** — different shots of the same scene (bursts, variants, retouches).

Matches are shown as pairs with a similarity score and both previews. Nothing is deleted,
moved or renamed without an explicit command, and deletion goes to the Recycle Bin (or to
a folder you choose).

The application is designed for large libraries: the image index is built once and then
reused, so repeated searches do not read your photo files again.

## Features

**Search**

- Three comparison algorithms: *Squared Sum* (fast), *SSIM* (more perceptually accurate)
  and *Perceptual hash* (a 256-bit DCT hash per image for a very fast near-duplicate
  pass; the tolerance is set in percent, 33 % by default).
- Adjustable tolerance in the toolbar.
- Five one-click **search presets**: *Exact copies*, *Compression and resize*,
  *Transformations*, *Similar: strict*, *Similar: wider*.
- Optional restrictions: compare images only inside one folder or one search path, size
  control, file-type control, aspect-ratio control, minimal/maximal image size.
- Comparison of several databases at once, including *Pool 1 vs Pool 2* (see
  [Databases](#databases)).

**Databases**

- Any number of independent databases, each built from one source folder.
- A database is created and updated by the GPU collector, which stores small thumbnails,
  file metadata and image-quality metrics — that is what makes searches fast.
- Incremental update of an existing database; changing the source folder of a database
  without rebuilding it (remap).

**Results**

- Results table with thumbnails, paths, file sizes and similarity; the *Target* column
  selects which image of a pair an action applies to.
- Delete first / second / both pictures, replace one picture by the other, rename them —
  individually or in batch, from the toolbar, the context menu or the preview.
- **Auto-Select** marks the image to keep in every pair: quick criteria (older, newer,
  smaller …) or an advanced combination of criteria (resolution, size, date, quality,
  pool) evaluated with AND logic.
- **Keep Only This One** marks the other images of a group as worse without deleting
  anything.
- **Processed marks**: hide already reviewed pairs, count them in the status bar, and mark
  pairs automatically after batch operations. Marks are stored in `user\processed.tsv`.
- Filters: hide processed pairs, show only unmarked ones, filter by path text.
- **Do not delete** paths: a protected list that batch operations and Auto-Select skip,
  with a warning that reports how many files were skipped.
- **Defect search**: blockiness and blurring are measured when the database is built, so
  blurry or over-compressed pictures can be found and deleted on their own.

**Performance and interface**

- JPEG is decoded on the GPU (nvJPEG); all image comparisons run as CUDA kernels.
- Thumbnail size 32×32 / 64×64 / 128×128. The smaller the thumbnails, the more images fit
  into one search — about 6 M images at 32×32 with 8 GB of VRAM, 1.6 M at 64×64 and 0.4 M
  at 128×128, proportionally more with more VRAM; *Options* shows the estimates.
- Seven interface languages: English, Russian, French, German, Greek, Simplified Chinese,
  Spanish.

## Requirements

**To run**

- Windows 10 or 11, 64-bit.
- An NVIDIA GPU of the **Turing generation or newer** (GTX 16xx, RTX 20xx and later).
  Comparison runs on the GPU only — this fork has no CPU fallback — and the database
  builder decodes JPEG with nvJPEG.
- A current NVIDIA driver. The CUDA runtime libraries ship with the application; you do
  not need to install the CUDA Toolkit.
- **.NET 8 Desktop Runtime** — only for the zip/self-extracting package (link in
  [Installation](#installation)). The single-file portable build does not need it.
- Free disk space for the databases, which are stored next to the application.

**To build** — see [Building from source](#building-from-source).

## Installation

Every release provides four files (plus SHA-256 checksums in `*.hash.txt`):

**If you are not sure which one to take, download
`AntiDupl.NET-<version>_SingleFilePortable.exe`** — it needs no .NET installation.

| File | What is inside | Needs .NET 8 |
|---|---|---|
| `AntiDupl.NET-<version>.zip` | The complete application: GUI, `AntiDupl.dll`, database builder `NvJpegCollector.exe`, CUDA runtime libraries, resources | yes |
| `AntiDupl.NET-<version>.exe` | The same package as a self-extracting archive | yes |
| `AntiDupl.NET-<version>_SingleFilePortable.exe` | The complete application as a self-contained build — no .NET installation needed | no |
| `AntiDupl.NET-<version>_SingleFilePortable.zip` | The same portable package in a zip | no |

Both packages can create, update and search databases: the database builder is a native
executable and ships in both. Choose the zip package when .NET 8 is already installed or
can be installed; choose the portable build for a machine where the .NET runtime cannot be
installed.

Installation steps:

1. Download the release from the [releases page](https://github.com/Sucotasch/AntiDuplPlus/releases).
2. The `.exe` files are self-extracting archives: run one, choose an empty folder and press
   *Extract*. The `.zip` files contain the same folder in a normal archive.
3. Unpack into a folder you can write to (the application keeps its databases, settings and
   logs inside that folder — do not put it into `Program Files`).
4. If the application does not start, install the
   [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
5. Start `AntiDupl.NET.WinForms.exe`.

Nothing is installed system-wide: the application runs from its folder. Only a few
interface preferences (window layout, pool comparison mode) are kept in the current user's
registry.

## Quick start

1. **Create a database.** Click **New Database** in the toolbar (or *Tools → New
   Database*), choose the folder with your images and wait for the collector to finish.
   The database is created under `databases\<name>\`.
2. **Check the database.** Open **Database Manager** and make sure the new database is
   switched **On**. If you want to compare two libraries with each other, assign them to
   *Pool 1* and *Pool 2* and pick the comparison mode.
3. **Search.** Press **Search**. The GPU compares all indexed images with each other and
   the pairs appear in the results table.
4. **Review.** Sort out the pairs: use the **Search presets** to change what counts as a
   duplicate, **Auto-Select** to mark the copies to delete, *Keep Only This One* to mark a
   whole group, and **Mark as Processed** for pairs you have already reviewed.
5. **Clean up.** Delete the marked files to the **Recycle Bin** or move them to a separate
   folder, then rebuild or update the database when your library changes.

## How it works

```
source folder ──► NvJpegCollector (GPU) ──► database: thumbnails + metadata + quality
                                                   │
                                            search (CUDA): every image vs every image
                                                   │
                                            results table: pairs + similarity
```

- **Indexing.** The collector walks the source folder, decodes each image (JPEG on the
  GPU, other formats on the CPU), scales it down to a thumbnail (32×32 by default,
  configurable from 16 to 128), measures blockiness and blurring, and writes the records
  into `databases\<name>\`.
- **Searching.** The engine loads the enabled databases, keeps the thumbnails in GPU
  memory and compares every image with every other image. Your original files are not
  read during a search, which is why repeated searches are fast.
- **Change detection.** A record is considered current when its path, size and timestamp
  still match; an update adds new files, refreshes changed ones and drops missing ones.
- **Similarity** is computed on thumbnails, so it is an estimate by design; the tolerance
  (or the preset) controls how close two images must be to be reported.

## Databases

A database is a compact index of one source folder:

- `databases\<name>\index.adi` — the index;
- `databases\<name>\0000.adi` — thumbnails, file metadata and quality metrics;
- `ad_database.xml` next to the executable — the list of known databases, their source
  folders, pools and enabled state.

The **Database Manager** (toolbar button or *Tools → Database Manager*) lets you:

- switch databases **On/Off** for the next search;
- assign databases to **Pool 1** and **Pool 2** and choose what is compared:

  | Pool comparison mode | Compared |
  |---|---|
  | None | pool comparison off |
  | Pool 1 internal | images of Pool 1 with each other |
  | Pool 2 internal | images of Pool 2 with each other |
  | Cross | Pool 1 against Pool 2 |
  | All | both pools internally and against each other |

- **Update** a database after files were added, changed or deleted (incremental — only
  differences are processed);
- **Path…** — point a database to a new source folder after the folder was moved or
  renamed. The thumbnails stay valid, so no rebuild is needed; the *Moved* column shows
  `yes` while the stored paths differ from the configured folder. Run **Update** to
  rewrite the stored paths.

Databases can be large; keep them on a fast local disk, and remember that the number of
images compared in one search is limited by GPU memory (see the capacity hints in
*Options*).

## Search presets and algorithms

The toolbar contains the algorithm selector, the tolerance field and the **Search
presets** dropdown that configures both in one click:

| Preset | Finds |
|---|---|
| Exact copies | images with identical content |
| Compression and resize | the same picture re-saved with another quality or resized |
| Transformations | rotated and mirrored copies of the same picture |
| Similar: strict | visually similar shots with a tight tolerance |
| Similar: wider | visually similar shots with a looser tolerance |

The algorithms behind them:

- **Squared Sum** — pixel-wise difference of the thumbnails; the fastest mode, good for
  exact and near-exact copies.
- **SSIM** — structural similarity; slower, but agrees better with human perception, so it
  is the better choice for similar shots.
- **Perceptual hash** — compares 256-bit hashes; extremely fast, well suited for finding
  re-compressed or slightly edited copies. The tolerance is a percentage (default 33 %).

After choosing a preset you can still adjust the algorithm and the tolerance manually.

## Working with results

Each row shows a pair of images: thumbnails, paths, sizes and the similarity score. The
**Target** column marks the image of the pair that the actions apply to; click it to
switch between the first and the second image.

- **Delete** the first, the second or both images — to the Recycle Bin by default (this
  can be changed in *Options*), or move them to a chosen folder.
- **Replace** one picture by the other, or **rename** one like the other, without changing
  the content.
- **Auto-Select** marks the copies to remove according to quick criteria (older, newer,
  smaller …) or an advanced rule built from several criteria with AND logic.
- **Keep Only This One** marks every other image of a group as worse.
- **Mark as Processed** hides a pair without touching the files; **Unmark Processed** and
  **Clear Processed Marks** undo this. With *Mark Processed After Operations* the pairs
  are marked automatically after a batch action.
- **Filters** hide processed pairs, show only unmarked ones or filter by a path fragment;
  the status bar shows how many pairs are processed and how many are currently shown.
- **Do not delete** (*Search → Paths*) protects folders: batch operations and Auto-Select
  skip those files and report how many were skipped.
- **Show neighbours** (Ctrl+Q) lists all images that are similar to the selected one, not
  only the pair it belongs to.

> The application has no undo for deletions and moves: deleted files go to the Recycle Bin
> (or the target folder), so restore them from there or with Explorer. **Ctrl+U** only
> clears the Auto-Select marking; it does not bring files back.

## Supported formats

The database builder indexes these formats:

| Format | Extensions | Decoding |
|---|---|---|
| JPEG | `.jpg` `.jpeg` `.jpe` `.jif` `.jfi` `.jfif` | GPU (nvJPEG) |
| PNG | `.png` | CPU (Windows Imaging Component) |
| BMP | `.bmp` `.dib` | CPU (Windows Imaging Component) |
| GIF | `.gif` | CPU (Windows Imaging Component) |
| TIFF | `.tif` `.tiff` | CPU (Windows Imaging Component) |
| WebP | `.webp` | CPU (libwebp) |

The image core can additionally decode AVIF, HEIF, JPEG XL, JPEG 2000, PSD, TGA, DDS,
ICO and EMF/WMF, but the database builder does not index those formats yet, so they are
not searched.

## Command line

The database builder can be used on its own — the GUI simply launches it with the same
options:

```bat
NvJpegCollector.exe --input "D:\Photos"
NvJpegCollector.exe --input "D:\Photos" --name "MyPhotos" --size 64
NvJpegCollector.exe --input "D:\Photos" --update
```

| Option | Meaning |
|---|---|
| `--input`, `-i` | Source folder with images (required) |
| `--output`, `-o` | Root folder for databases (default: `databases` next to the executable) |
| `--name`, `-n` | Database name (default: the source folder name) |
| `--size`, `-s` | Thumbnail size in pixels, 16–128 (default 32) |
| `--workers`, `-w` | Number of CPU worker threads (default: detected automatically) |
| `--update`, `-u` | Incremental update of an existing database |
| `--force-empty` | Allow clearing a database when the source folder is empty or unavailable |
| `--no-pause` | Do not wait for a key press at the end (for scripts) |
| `--help`, `-h` | Show usage |

Run the collector from the application folder so that the database lands in the
`databases\` folder the GUI reads.

## Files and settings

Everything is stored next to the executable, so the whole folder can be moved or copied:

| Path | Contents |
|---|---|
| `databases\<name>\` | Databases (`index.adi`, `0000.adi`) |
| `ad_database.xml` | Registered databases, their source folders, pools and state |
| `user\options.xml` | Application settings |
| `user\processed.tsv` | Processed marks |
| `user\mistakes.adm` | Remembered "mistake" pairs |
| `user\profiles\default.xml` | Default profile of the search engine |
| `trace.log`, `gpu_debug.log` | Diagnostic logs (only created when the engine writes them) |

## Keyboard shortcuts

Default keys (all of them can be changed in *View → Hot Keys*):

| Key | Action |
|---|---|
| NumPad 1 | Delete the first picture of the pair (or the defect picture) |
| NumPad 2 | Delete the second picture |
| NumPad 3 | Delete both pictures |
| NumPad 4 | Replace the second picture by the first |
| NumPad 6 | Replace the first picture by the second |
| NumPad 5 | Remember the pair as a mistake |
| Ctrl+Q | Show all neighbours of the selected image |
| Ctrl+U | Clear the Auto-Select marking |

## Troubleshooting

**The search finds nothing**

- Check that the database is switched **On** in *Database Manager* and is not empty.
- If the source folder was moved, the *Moved* column shows `yes` — set the new folder
  with **Path…** or run **Update**.
- Check the result filters: processed pairs can be hidden, and the path filter may be
  active.

**The application reports that the GPU is not available, or searches fail**

- Update the NVIDIA driver.
- The GPU must be Turing or newer (GTX 16xx / RTX 20xx and up); this fork does not fall
  back to the CPU.
- See `gpu_debug.log` next to the executable for details.

**The application does not start**

- The zip package needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0);
  the single-file portable build does not.

**A search is very slow or runs out of GPU memory**

- Reduce the thumbnail size in *Options* (32×32 allows the largest databases), or split
  the library into several databases.

**I deleted a file by mistake**

- Restore it from the Recycle Bin or from the folder you moved it to; the application
  itself has no undo.

## Building from source

Prerequisites:

- Visual Studio 2022 with the *.NET Desktop development* and *Desktop development with
  C++* workloads;
- CUDA Toolkit 12.8 (for `cudart64_12.dll` and the CUDA build rules) and 13.1 (for
  `nvjpeg64_13.dll` used by the collector);
- vcpkg with the `x64-windows-static` triplet (dependencies are listed in
  `src/vcpkg.json`);
- .NET 8 SDK.

```bat
git clone https://github.com/Sucotasch/AntiDuplPlus.git
cd AntiDuplPlus
cmd\Deploy.cmd
```

`Deploy.cmd` builds the C++ projects and the WinForms GUI, copies the CUDA runtime
libraries and the resources into `bin\Release\`, and verifies the result. Run the
application from `bin\Release\AntiDupl.NET.WinForms.exe` — it is the only folder the GUI
works from, because it looks for `NvJpegCollector.exe` and `databases\` next to itself.

Other useful commands:

```bat
:: whole solution
msbuild src\AntiDupl.sln /p:Configuration=Release /p:Platform=x64

:: interface/contract tests (exit code 0 = ok)
dotnet run --project tests\AntiDupl.Contract.Tests\AntiDupl.Contract.Tests.csproj
```

Engineering notes (build quirks, database formats, CI) are in [AGENTS.md](AGENTS.md).

## Differences from the original AntiDupl.NET

| Area | Original AntiDupl.NET | AntiDuplPlus |
|---|---|---|
| JPEG decoding | CPU (libjpeg-turbo) | GPU (nvJPEG) |
| Comparison | CPU | CUDA kernels (Squared Sum, SSIM, perceptual hash) |
| Databases | one built-in database | any number of databases, created by the GPU collector |
| Pool comparison | — | Pool 1 vs Pool 2 with five modes |
| Source folder moved | rebuild | remap the database to the new folder |
| Search presets | — | five one-click presets |
| Processed marks, protected paths | — | stored in `user\processed.tsv` and in the options |
| Undo/Redo in the UI | yes | no — restore files from the Recycle Bin; Ctrl+U clears the Auto-Select marking |

## Limitations

- **Windows x64 only.** There are no Linux or ARM builds.
- **NVIDIA GPU required.** The comparison has no CPU fallback; without a Turing-class (or
  newer) GPU the search cannot run.
- **No undo** for delete/move/rename operations.
- **The database builder indexes JPEG, PNG, BMP, GIF, TIFF and WebP**, so AVIF, HEIF and
  JPEG XL files are not found by a search yet.

## License

MIT — see [LICENSE](LICENSE). The license is inherited from the original AntiDupl.NET.

## Acknowledgements

- [AntiDupl.NET](https://github.com/ermig1979/AntiDupl) — the original project.
- [NVIDIA nvJPEG](https://developer.nvidia.com/nvjpeg) and the
  [CUDA Toolkit](https://developer.nvidia.com/cuda-toolkit) — GPU JPEG decoding and
  comparison.
- [libjpeg-turbo](https://github.com/libjpeg-turbo/libjpeg-turbo),
  [libwebp](https://chromium.googlesource.com/webm/libwebp),
  [libheif](https://github.com/strukturag/libheif),
  [libavif](https://github.com/AOMediaCodec/libavif),
  [libjxl](https://github.com/libjxl/libjxl),
  [OpenJPEG](https://github.com/uclouvain/openjpeg),
  [Simd](https://github.com/ermig1979/Simd) — image codecs and SIMD primitives.
- [vcpkg](https://github.com/microsoft/vcpkg) — C++ dependency management.
