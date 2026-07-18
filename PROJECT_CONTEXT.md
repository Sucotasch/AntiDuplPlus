# AntiDuplPlus

GPU-ускоренный поисковик дубликатов/похожих изображений. Fork [AntiDupl.NET](https://github.com/ermig1979/AntiDupl) с CUDA/nvJPEG ускорением.

## Текущий статус (на 18.07.2026)

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

### Исправлено в предыдущих сессиях (аудит-фиксы)
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

### Исправлено 18.07.2026 (аудит FULL_AUDIT_2026-07-17)
- ✅ **BUG-01 [P0]**: Thumb size mismatch → heap OOB. Добавлена проверка `data->side == reducedImageSize` в GPU pack loop (`adEngine.cpp`)
- ✅ **BUG-02 [P1]**: DLL-native magic `"adid"` вместо `"adii"`. Исправлено `0x69696461u` в `Load()` (`adImageDataStorage.cpp`)
- ✅ **BUG-03 [P1]**: Unbounded fread thumbnail bytes. Добавлена проверка `thumbBytes == side*side` в `LoadCollectorData` (`adImageDataStorage.cpp`)
- ✅ **BUG-04 [P1]**: GPU игнорировал CPU фильтры (type/size/folder/searchPath). Добавлены фильтры в `MatchCallback` (`adEngine.cpp`)
- ✅ **BUG-05 [P1]**: Batch move не обновлял result list. Добавлен новый native API `MarkRemovedFirst/Second` (enum 14/15) + `MarkRemoved()` функция (`adUndoRedoEngine.cpp`, `AutoSelector.cs`)
- ✅ **BUG-06 [P1]**: Shutdown dispose race 2s timeout. Увеличен до 10s (`MainForm.cs`)
- ✅ **BUG-07 [P2]**: validCount < 2 → false (фейковая ошибка GPU). Теперь возвращает `true` (`adEngine.cpp`)
- ✅ **BUG-09 [P2]**: d_poolMask VRAM leak при ошибках MS ядра. Добавлен cleanup в error paths (`adGPU.cu`)
- ✅ **BUG-11 [P3]**: Skip flag устанавливался после CollectManager.Start(). Перемещён до Start() (`adEngine.cpp`)

### Отложено / закрыто
- ⏭ **BUG-08 [P2]**: Match buffer truncation — пропущен (>5M пар, редкий случай)
- ⏭ **BUG-10 [P2]**: GPU→CPU fallback — пропущен (не нужен, пользователь может использовать оригинальную версию)
- ❌ **BUG-12 [P3]**: DB load блокирует FS scan — закрыт как "not a bug" (текущее поведение корректно для рабочего процесса с DB)
- ⏳ **FIND-8**: Cancel не wired для batch flows

### Планы на будущее
- 🔮 **Сравнение без DB**: добавить возможность сравнения папок без предварительного создания DB (или авто-создание DB в фоне)

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
- **`adExternal.h`** (C++) и **`External.cs`** (C#) **генерируются автоматически** из `src/version.txt` pre-build скриптами. Не редактировать вручную.
- **Post-build**: `cmd/CopyData.cmd` копирует `data/resources/` в папку вывода.
- **vcpkg зависимости** устанавливаются в `src/vcpkg_installed/x64-windows-static/`. Первая сборка может быть долгой.
- **C# build output** идёт в `src/AntiDupl.NET.WinForms/bin/Release/`, не в общий `bin/Release/`. Копировать вручную.

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
- **DLL-native**: `"adii"` заголовок (magic = `0x69696461`). Записывается при сканировании файлов (CPU).
- **Collector-native**: Без заголовков, raw fwrite. Первый u32 = ThumbSize. Записывается NvJpegCollector.
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

## Заметки по коду (проверено 18.07.2026)

### GetKey() в AutoSelector.cs — безопасно
Ключ генерируется как `path1 + "|" + path2` (отсортированы алфавитно). Каждая пара путей уникальна, ключ уникален. Проблем нет.

### AD_LOCAL_ACTION_SIZE — безопасно
Используется только в `adResultStorage.cpp:160` для проверки границ. Enum автоматически инкрементируется (SIZE = 16 после добавления 14, 15). Нет hardcoded размеров массивов. Все switch statement обрабатывают новые case.

---

## Оборудование (dev)
- GPU: NVIDIA RTX 4070 Ti Super 16GB (Ada Lovelace, SM 8.9)
- CUDA: 12.8
- RAM: 64GB
- NVJPEG_BACKEND_DEFAULT (software decoder on GPU)

---

## Процесс создания релиза

### Пошаговая инструкция

1. Обновить version в src/version.txt
2. Собрать C++ проект: msbuild src\AntiDupl\AntiDupl.vcxproj /p:Configuration=Release /p:Platform=x64 /p:VcpkgManifestInstall=false
3. Скопировать AntiDupl.dll: Copy-Item src\bin\Release\AntiDupl.dll bin\Release\AntiDupl.dll -Force
4. Собрать C#: dotnet build src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj -c Release
5. Скопировать C# output: Copy-Item src\AntiDupl.NET.WinForms\bin\Release\AntiDupl.NET.WinForms.exe bin\Release\ -Force
6. Self-contained publish: dotnet publish src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj -c Release -r win-x64 --self-contained true -o out/publish
7. Добавить native deps: скопировать AntiDupl.dll, nvjpeg64_12.dll, cudart64_12.dll, NvJpegCollector.exe, data/ в out/publish
8. Zip: cd out/publish && 7za a -tzip ..\bin\AntiDupl.NET-{VER}.zip *
9. GitHub: git tag + gh release create --repo Sucotasch/AntiDuplPlus

### Критические замечания
- dotnet publish НЕ копирует native P/Invoke DLL (AntiDupl.dll) - копировать вручную
- MakeBin.cmd устарел - не включает CUDA зависимости
- zip должен содержать файлы в корне, не в поддиректории out/publish
- gh release create требует --repo Sucotasch/AntiDuplPlus
