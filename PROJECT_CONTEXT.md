# AntiDuplPlus

GPU-ускоренный поисковик дубликатов/похожих изображений. Fork [AntiDupl.NET](https://github.com/ermig1979/AntiDupl) с CUDA/nvJPEG ускорением.

## Текущий статус (на 15.07.2026)

### Рабочее
- ✅ GPU поиск дубликатов (Mean Square + SSIM)
- ✅ NvJpegCollector — GPU декодирование JPEG, CPU для остальных форматов
- ✅ Database Manager — управление базами, Pool1/Pool2, кросс-пуловое сравнение
- ✅ Результаты поиска сохраняются/загружаются при перезапуске (.adr файлы)
- ✅ SSIM корректно работает (average/variance загружаются из Collector-native баз)
- ✅ Pool settings загружаются при запуске (без открытия Database Manager)
- ✅ Портативность — все данные рядом с .exe
- ✅ Немедленное удаление в корзину (без отложенных temp файлов)
- ✅ База автоматически обновляется после delete/move (CheckImageData + фильтрация при загрузке)

### Исправлено в последней сессии (аудит-фиксы)
- ✅ **V01**: Thread dispose race в MainForm/StartFinishForm
- ✅ **V02**: Null StatusGet в WPF SearchDllCommand
- ✅ **V03**: WPF icon cache normalization (bounded 3-entry cache)
- ✅ **V04**: Core csproj External.cmd case mismatch → $(ProjectDir)
- ✅ **V05**: README — добавлен WPF frontend, уточнён формат .adi
- ✅ **V07**: Managed options — Math.Clamp для ThresholdDifference, IgnoreFrameWidth
- ✅ **FIND-1**: Tiebreaker для равных time/size/resolution (prefer First)
- ✅ **FIND-3**: Move throws abort batch → try/catch + failed count
- ✅ **FIND-4**: Delete counts attempts → ApplyToResult return value
- ✅ **FIND-6**: Path validation для move (IsSafeMoveTarget)
- ✅ **FIND-9**: Duplicate file в парах → HashSet dedup
- ✅ **FIND-12**: BatchResult with Succeeded/Failed/FailedPaths
- ✅ **A1-A4**: volatile для cross-thread state (4 формы)
- ✅ **B1-B2**: GDI+ leaks в ComplexProgressBar, DataGridViewDoubleTextBoxCell
- ✅ **B3-B5-B6**: FileStream/ImageAttributes/Font resource leaks
- ✅ **B8**: Resources.cs using blocks
- ✅ **C1-C3**: Build config $(SolutionDir) → $(MSBuildProjectDirectory)
- ✅ **Immediate delete**: TRecycleBin::Delete → FileDelete (SHFileOperation + FOF_ALLOWUNDO)
- ✅ **Database update**: adCheckImageData API + LoadData/LoadCollectorData IsFileExists check

### Отложено (low priority, не влияют на работоспособность)
- ⏳ **FIND-8**: Cancel не wired для batch flows — требует background thread + ProgressForm integration (~50 строк)
- ⏳ **V06**: Нет GPU→CPU fallback при ошибке GPU — CPU path существует но не вызывается (~10 строк, нужна проверка совместимости)

---

## Сборка

### Команды

```bash
# Полная сборка (рекомендуется)
msbuild src\AntiDupl.sln /p:Configuration=Release /p:Platform=x64

# Одиночный C++ проект (из VS Developer Command Prompt)
msbuild src\AntiDupl\AntiDupl.vcxproj /p:Configuration=Release /p:Platform=x64 /m:1

# Одиночный C# проект
dotnet build src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj /p:SolutionDir="src\\" -c Release
```

### Конфигурации
- **Debug|x64**, **Release|x64**, **Publish|x64**
- NvJpegCollector поддерживает только Release
- Весь вывод в `bin/<Configuration>/` (общий для C# и C++ проектов)

### Особенности сборки
- **`adExternal.h`** (C++) и **`External.cs`** (C#) **генерируются автоматически** из `src/version.txt`preh-build скриптами. Не редактировать вручную.
- **Post-build**: `cmd/CopyData.cmd` копирует `data/resources/` в папку вывода.
- **vcpkg зависимости** устанавливаются в `src/vcpkg_installed/x64-windows-static/`. Первая сборка может быть долгой.

### vcpkg: проблема с simd
Пакет `simd` устанавливает заголовки в `vcpkg_installed/x64-windows-static/include/`, но MSBuild ищет в `vcpkg_installed/x64-windows-static/x64-windows-static/include/`. **Workaround**: скопировать заголовки и .lib файлы вручную:
```bash
Copy-Item "src\vcpkg_installed\x64-windows-static\include\Simd" "src\vcpkg_installed\x64-windows-static\x64-windows-static\include\Simd" -Recurse -Force
Copy-Item "src\vcpkg_installed\x64-windows-static\lib\*.lib" "src\vcpkg_installed\x64-windows-static\x64-windows-static\lib\" -Force
```
При сборке использовать `/p:VcpkgManifestInstall=false` чтобы не пересобирать vcpkg.

---

## Архитектура

### Модули
```
AntiDupl.dll (C++/CUDA) — основная библиотека
├── adEngine.cpp          — движок поиска (Search)
├── adGPU.cu              — CUDA ядра AllVsAll + SSIM
├── adImageDataStorage.cpp — чтение/запись .adi, .adr файлов
├── adSearcher.cpp        — загрузка Collector-native баз
├── adImageInfo.cpp       — Actual() — проверка актуальности файлов
└── adNvJpeg.cpp          — GPU декодирование JPEG

AntiDupl.NET.WinForms.exe (C#) — GUI
├── DatabaseManagerForm.cs — менеджер баз (Pool1/Pool2)
├── SearchExecuterForm.cs  — запуск поиска
├── Options.cs             — настройки приложения
└── CoreLib.cs             — P/Invoke обёртка

NvJpegCollector.exe (C++/CUDA) — утилита создания баз
└── main.cpp — GPU декодирование + запись Collector-native .adi
```

### Два формата .adi
- **DLL-native**: `"adid"` заголовок + version. Записывается при сканировании файлов (CPU).
- **Collector-native**: Без заголовков, raw fwrite. Записывается NvJpegCollector.
- **Не путать**: `LoadData()` читает DLL-native, `LoadDatabase()` читает Collector-native.

### Формат .adr (результаты)
- `"adr"` magic + version 4
- ImageInfoStorage: count + N * TImageInfo (path, size, time, hash, type, width, height, blockiness(double), blurring(double), imageExif)
- Result count + N * TResult (type, first_index, second_index, defect, difference, transform, group, groupSize, hint)

### Известная проблема: hash=0
NvJpegCollector записывает `hash=0` для всех изображений (4 места: строки 321, 518, 548, 752 main.cpp). При загрузке .adr файла `Actual()` проверяет path+size+time (hash исключён из проверки). Новые базы также будут с hash=0.

---

## Ключевые файлы

| Задача | Файлы |
|--------|-------|
| Движок поиска | `src/AntiDupl/adEngine.cpp` |
| GPU ядра | `src/AntiDupl/adGPU.cu` |
| Загрузка/сохранение результатов | `src/AntiDupl/adResultStorage.cpp` |
| Actual() — проверка файлов | `src/AntiDupl/adImageInfo.cpp` |
| Загрузка Collector-native баз | `src/AntiDupl/adSearcher.cpp` |
| Формат .adi/.adr | `src/AntiDupl/adFileStream.cpp` |
| Удаление файлов (Recycle Bin) | `src/AntiDupl/adRecycleBin.cpp` |
| Проверка файлов при загрузке | `src/AntiDupl/adImageDataStorage.cpp` |
| Auto-Select логика | `src/AntiDupl.NET.WinForms/AutoSelector.cs` |
| Batch операции | `src/AntiDupl.NET.WinForms/AutoSelector.cs` (ExecuteBatch) |
| Pool настройки | `src/AntiDupl.NET.WinForms/Forms/DatabaseManagerForm.cs` |
| P/Invoke | `src/AntiDupl.NET.Core/CoreDll.cs` |

---

## Оборудование (dev)
- GPU: NVIDIA RTX 4070 Ti Super 16GB (Ada Lovelace, SM 8.9)
- CUDA: 12.8
- RAM: 64GB
- NVJPEG_BACKEND_DEFAULT (software decoder on GPU)
