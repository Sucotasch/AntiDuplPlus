# AntiDuplPlus

[![CI](https://github.com/Sucotasch/AntiDuplPlus/actions/workflows/AntiDupl_CI.yml/badge.svg)](https://github.com/Sucotasch/AntiDuplPlus/actions/workflows/AntiDupl_CI.yml)

> **EN:** GPU-accelerated duplicate/similar image finder for Windows x64. A fork of [AntiDupl.NET](https://github.com/ermig1979/AntiDupl) with CUDA/nvJPEG acceleration.
>
> **RU:** GPU-ускоренный поисковик дубликатов/похожих изображений для Windows x64. Форк [AntiDupl.NET](https://github.com/ermig1979/AntiDupl) с CUDA/nvJPEG ускорением.

![AntiDupl screenshot](https://ermig1979.github.io/AntiDupl/data/help/english/files/MainForm.png)

---

## Features — Возможности

| Component / Компонент | Original / Оригинал AntiDupl.NET | AntiDuplPlus |
|---|---|---|
| JPEG decoding / Декодирование JPEG | CPU (libjpeg-turbo) | **GPU (nvJPEG)** — 5-10x faster |
| Image comparison / Сравнение изображений | CPU (AllVsAll + OneVsList) | **GPU AllVsAll** (Squared Sum + SSIM) |
| Database creation / Создание баз данных | N/A | **NvJpegCollector** — GPU decode + thumbnails |
| Incremental update / Инкрементальное обновление | N/A | **`--update`** — add/remove changed files |
| Database management / Управление базами | Search Paths (legacy) | **Database Manager** — create, update, delete, pools |
| **Source folder remap** / **Смена папки-источника** | N/A | **Change Source Folder** — re-point a DB after its folder is moved |
| Pool comparison / Сравнение пулов | N/A | **Pool1 vs Pool2** — cross-pool comparison |
| Auto-Select / Автовыбор | Basic | **Extended** — time, size, quality, resolution, pools (AND-logic) |
| Delete files / Удаление файлов | Recycle Bin only | **Recycle Bin + move** to a chosen folder |
| Image quality / Качество изображений | Basic | **blockiness + blurring** computed at DB creation |
| SSIM | No | **GPU SSIM** — full algorithm |

---

## Requirements — Требования

### To run — Для запуска
- **Windows** 10/11 x64
- **.NET 8.0 Runtime** — [Download / Скачать](https://dotnet.microsoft.com/download/dotnet/8.0)
- **NVIDIA GPU** with CUDA support (RTX 20xx or newer) — for GPU acceleration
- Without a GPU the app runs on CPU (slower) / Если GPU нет — работает на CPU (медленнее)

### To build — Для сборки
- **Visual Studio 2022** (Community or higher, v143 toolset)
- Workloads: `.NET Desktop development` + `Desktop development with C++`
- **CUDA Toolkit 13.1** (builds `NvJpegCollector.exe` against `nvjpeg64_13.dll`; 12.8 also needed for `cudart64_12.dll`)
- **vcpkg** (triplet `x64-windows-static`) — dependencies in `src/vcpkg.json`
- **.NET 8.0 SDK**

---

## Quick start — Быстрый старт

1. Download the [latest release](https://github.com/Sucotasch/AntiDuplPlus/releases) / Скачайте [последний релиз](https://github.com/Sucotasch/AntiDuplPlus/releases)
2. Unpack the archive / Распакуйте архив
3. Install [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) / Установите .NET 8.0 Runtime
4. Run `AntiDupl.NET.WinForms.exe` / Запустите `AntiDupl.NET.WinForms.exe`

---

## Workflow — Рабочий процесс

### Step 1: Create a database — Шаг 1: Создание базы данных

1. Open **Tools → GPU Collector** (or the toolbar button) / Откройте **Tools → GPU Collector**
2. Select a folder with images / Выберите папку с изображениями
3. NvJpegCollector decodes via GPU (JPEG) or CPU (PNG/BMP/TIFF/WebP/GIF)
4. Computes **blockiness** and **blurring** for each image
5. Creates `index.adi` + `0000.adi` in `databases/<name>/`
6. Database is auto-registered in `ad_database.xml`

### Step 2: Manage databases — Шаг 2: Управление базами

1. Open **Tools → Database Manager** / Откройте **Tools → Database Manager**
2. Enable/disable databases for search (check **On**)
3. Assign databases to **Pool1** (Reference) and **Pool2** (Target) for cross-pool comparison
4. Choose **Pool Comparison Mode**: None / Pool1 Internal / Pool2 Internal / Cross / All Pools

### Step 2b: Change source folder — Шаг 2б: Смена папки-источника

If the source folder was moved, you don't need to rebuild the database:

1. In **Database Manager** click **Path...** next to the database
2. Select the new folder location
3. The DB keeps its thumbnails and gets remapped at load time (`Moved` column shows `yes`)
4. Optionally run **Update** to rewrite stored paths to the new folder

### Step 3: Search — Шаг 3: Поиск

1. Press **Search** (or F5) / Нажмите **Search** (или F5)
2. The app loads enabled databases through the DLL
3. GPU compares all images (AllVsAll)
4. Results appear in the table and are saved for the next run

### Step 4: Handle results — Шаг 4: Обработка результатов

- Context menu → Delete First / Delete Second / Delete Both
- Context menu → Move First → Second / Move Second → First
- **Auto-Select**: Edit → Auto-Select → quick presets (Older, Newer, Smaller...)
- **Auto-Select Advanced**: combined criteria (AND-logic)
- Edit → Delete Selected / Move Selected to Folder
- Manual: click **Target** column to switch 1st / 2nd / (empty)

### Step 5: Update a database — Шаг 5: Обновление базы

When files in the source folder changed:

```
NvJpegCollector.exe --input "D:\photos" --update
```

Or via **Database Manager → Update** button / или кнопка **Update** в Database Manager.

---

## Project structure — Структура проекта

```
AntiDuplPlus/
├── src/
│   ├── AntiDupl/                    # C++/CUDA core (AntiDupl.dll)
│   │   ├── adEngine.cpp             # Search engine
│   │   ├── adImageDataStorage.cpp   # DB load/save (.adi, .adr)
│   │   ├── adGPU.cu                 # CUDA kernels (Squared Sum + SSIM)
│   │   ├── adSearcher.cpp           # Multi-DB load + file scan
│   │   ├── adNvJpeg.cpp             # GPU JPEG decoding
│   │   └── adResultStorage.cpp      # Result storage
│   ├── NvJpegCollector/             # GPU database collector
│   │   └── main.cpp                 # nvJPEG decode + blockiness/blurring + .adi
│   ├── AntiDupl.NET.Core/           # C# bindings (P/Invoke)
│   ├── AntiDupl.NET.WinForms/       # Main GUI
│   │   ├── Form/MainForm.cs         # Main form
│   │   └── Forms/DatabaseManagerForm.cs  # Database Manager (pools, remap)
│   └── AntiDupl.NET.WPF/            # Secondary GUI (less maintained)
├── bin/Release/                     # Run-ready files (build output)
├── cmd/                             # Deploy.cmd, CopyData.cmd, packaging
└── Audit/                           # Code audit reports
```

---

## Database formats — Формат баз данных

### Two .adi formats — Два формата .adi (don't confuse!)

| | DLL-native | Collector-native |
|---|---|---|
| Writer | `adImageDataStorage.cpp` (`SaveData`) | `NvJpegCollector/main.cpp` (raw `fwrite`) |
| Header | `"adid"` magic + version (`index.adi` uses `"adii"`) | None (first `u32` = ThumbSize) |
| Reader | `LoadData()` / stream path | Auto-detect in `Load()` → `LoadCollectorNative()` |
| Created by | CPU file scan / DLL save | GPU collector utility |

### Collector-native format (0000.adi)

```
thumbSize(u32) + key(i16) + first(wstring) + last(wstring) + count(u64)
+ N records:
  path(wstring) + size(u64) + time(u64) + hash(u32) + type(u8)
  + width(u32) + height(u32) + blockiness(f64) + blurring(f64)
  + defect(u8) + crc32c(u64) + filled(u8)
  + thumb_size(u64) + thumb_data(bytes)
  + average(f32) + varianceSquare(f32)
```

---

## GPU acceleration — GPU-ускорение

| Operation / Операция | CPU | GPU | Speedup / Ускорение |
|---|---|---|---|
| JPEG decode / Декодирование | libjpeg-turbo | nvJPEG | 5-10x |
| AllVsAll comparison / Сравнение | adImageComparer | adGPU.cu | 10-50x |
| SSIM | adImageComparer | adGPU.cu (SsimKernel) | 10-50x |

### Supported GPUs — Поддерживаемые GPU
- RTX 20xx (Turing, SM 7.5)
- RTX 30xx (Ampere, SM 8.6)
- RTX 40xx (Ada Lovelace, SM 8.9)

---

## Command line — Командная строка

### NvJpegCollector

```bash
# Create a new database / Создать новую базу
NvJpegCollector.exe --input "D:\photos" --output "databases" --name "MyPhotos"

# Update an existing database / Обновить существующую базу
NvJpegCollector.exe --input "D:\photos" --update

# Options / Параметры
--input, -i    Path to image folder / Путь к папке с изображениями
--output, -o   Databases root folder / Корневая папка баз (default: databases/)
--name, -n     Database name / Имя базы (default: folder name)
--size, -s     Thumbnail size / Размер превью (default: 32)
--batch, -b    nvJPEG batch size / Размер батча nvJPEG (default: 64)
--update, -u   Incremental update / Инкрементальное обновление
```

### Supported formats — Поддерживаемые форматы

| Format / Формат | Decoding / Декодирование | Note / Примечание |
|---|---|---|
| JPEG/JPG/JFIF | GPU (nvJPEG) | Fastest / Максимальная скорость |
| PNG | CPU (WIC) | |
| BMP | CPU (WIC) | |
| TIFF | CPU (WIC) | |
| WebP | CPU (WIC) | |
| GIF | CPU (WIC) | |
| HEIF/AVIF/JXL | CPU (WIC) | |

---

## Build — Сборка

```bash
# 1. Clone / Клонировать
git clone https://github.com/Sucotasch/AntiDuplPlus.git
cd AntiDuplPlus

# 2. Full solution build (recommended) / Полная сборка (рекомендуется)
cmd\Deploy.cmd            # builds C++ + C#, copies CUDA deps + resources, verifies
# or
msbuild src\AntiDupl.sln /p:Configuration=Release /p:Platform=x64

# 3. Single C++ project (VS Developer Command Prompt)
msbuild src\AntiDupl\AntiDupl.vcxproj /p:Configuration=Release /p:Platform=x64 /m:1

# 4. Single C# project
dotnet build src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj /p:SolutionDir="src\\" -c Release
```

Notes / Замечания:
- Output goes to `bin/<Configuration>/` (shared by C++ and C#)
- `adExternal.h` / `External.cs` are auto-generated from `src/version.txt` — never edit by hand
- vcpkg installs under `src/vcpkg_installed/x64-windows-static/`; first build can be slow
- The GUI only runs from `bin/Release/AntiDupl.NET.WinForms.exe`

---

## License — Лицензия

MIT License — see [LICENSE](LICENSE) / см. [LICENSE](LICENSE) (inherited from original AntiDupl.NET).

## Acknowledgements — Благодарности

- [AntiDupl.NET](https://github.com/ermig1979/AntiDupl) — original project / оригинальный проект
- [NVIDIA nvJPEG](https://developer.nvidia.com/nvjpeg) — GPU JPEG decoding / GPU декодирование JPEG
- [CUDA Toolkit](https://developer.nvidia.com/cuda-toolkit) — GPU computing / GPU вычисления
- [vcpkg](https://github.com/Microsoft/vcpkg) — C++ dependency management / управление C++ зависимостями
