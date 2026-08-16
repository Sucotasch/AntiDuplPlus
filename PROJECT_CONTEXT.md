# AntiDuplPlus

GPU-ускоренный поисковик дубликатов/похожих изображений. Fork [AntiDupl.NET](https://github.com/ermig1979/AntiDupl) с CUDA/nvJPEG ускорением.

## Текущий статус (на 16.08.2026)

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

### Добавлено/исправлено после 18.07 (re-check 16.08)
- ✅ **WP-A (GPU filter parity)**: ratio-фильтр в `MatchCallback`, min/max size при GPU pack, `transformedImage → CPU` (`adEngine.cpp`)
- ✅ **DB source folder remap**: `RemapFrom` в registry + трансляция путей при загрузке, колонка `Moved` в Database Manager (`DatabaseManagerForm.cs`, `06378b2`)
- ✅ **NvJpegCollector переписан**: Y-decode, async pipeline (single sequential reader + N decoder threads), Simd-детекторы, фикс hang на недекодируемых файлах, per-file stats + failed-file log (`4ddd1cd`…`f6aecaa`)
- ✅ **GUI передаёт `--size` (= reducedImageSize) в collector** + VRAM hint и pre-check размера БД (`6618a3d`)
- ✅ **WPF переведён на net8.0-windows** (framework mismatch устранён)
- ✅ **C# output → общий `bin/<Config>/`** (csproj OutputPath `..\..\bin\`), Deploy.cmd передаёт `Platform=x64`
- ⏳ **BUG-13 (hash=0) всё ещё открыт**: `info.hash = 0` в `ProcessGray` (`main.cpp:307`); `SimpleCRC32(path)` используется только для имени файла БД
- Версия: `src/version.txt` = 2.5.3

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
- ~~C# build output идёт в src/...~~ (устарело): C# csproj теперь выводит в общий `bin/Release/` — см. «Заметки по сборке (09.08.2026)» ниже.

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
NvJpegCollector записывает `hash=0` для всех изображений (после rewrite — одно место: `info.hash = 0` в `ProcessGray`, `main.cpp`). При загрузке .adr файла `Actual()` проверяет path+size+time (hash исключён из проверки). Новые базы также будут с hash=0. `SimpleCRC32(path)` применяется только для генерации имени файла БД.

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
- OS: Windows 10
- CPU: Intel i7-5820K (6 cores / 12 threads)
- RAM: 64 GB
- GPU: NVIDIA RTX 4070 Ti Super 16GB (Ada Lovelace, SM 8.9)
- CUDA: 13.1 (collector links `nvjpeg64_13.dll`); 12.8 also installed for `cudart64_12.dll` (needed by `AntiDupl.dll`)
- NVJPEG_BACKEND_DEFAULT (software decoder on GPU)

## Деплой для тестирования (обязательно после любых изменений)
- Запускать `cmd\Deploy.cmd` — собирает C++ (AntiDupl.dll + NvJpegCollector.exe) и C# (WinForms), копирует CUDA-зависимости (`nvjpeg64_13.dll`, `cudart64_13.dll`) и resources, проверяет артефакты.
- GUI тестировать ТОЛЬКО из `bin\Release\AntiDupl.NET.WinForms.exe` (GUI запускает `NvJpegCollector.exe` из `Application.StartupPath`).
- Задача не считается готовой, пока `Deploy.cmd` не завершился строкой `[OK] Deploy complete.`

## Заметки по сборке (актуально 09.08.2026)
- C++ `OutDir`/`IntDir` = `$(ProjectDir)..\..\bin\` / `$(ProjectDir)..\..\obj\` — не зависят от `$(SolutionDir)`, поэтому одиночная сборка vcxproj и сборка через sln дают один и тот же `bin\<Config>\`.
- Старые копии `NvJpegCollector.exe` в `src\bin\Release\` и `src\AntiDupl.NET.WinForms\bin\Release\` устарели — использовать только корневой `bin\Release\`.

---

## Протокол тестирования производительности (benchmarking protocol)

**Правило №1: первый холодный запуск папки = реальный опыт пользователя.** Папка, которую ни разу не открывал ни один процесс на этой машине (или запуск сразу после перезагрузки) — единственный честный замер «как это видит пользователь». Он всегда медленнее повторных прогонов.

**Правило №2: эффект изменения изолируется парными тёплыми прогонами Run N vs Run N+1.** Для заявки «изменение X ускоряет Y» обязательна пара прогонов в одинаковом состоянии: до изменения (Run N) и после (Run N+1). Одиночные «разгонные» цифры (`113 img/sec`, `80 img/sec`) без пары НЕ принимаются как доказательство.

**Правило №3: подкачка RAM (memtouch) не делает прогон холодным.** Она вытесняет standby-страницы данных файлов, но НЕ вытесняет: метаданные NTFS (MFT-записи, индекс директории — живут в системном кэше с приоритетом), кэш контроллера/HDD, prefetch/Superfetch, драйверные буферы. Поэтому после подкачки повторный прогон той же папки всё ещё показывает прогретые метаданные.

**Стандартная процедура для измерения изменения:**
1. Run 1 — холодный (никогда не читавшаяся папка или после перезагрузки): фиксирует baseline пользователя.
2. Run 2 — тёплый: та же папка, подкачка 63 GB между прогонами; данные вне RAM, метаданные прогреты.
3. Внести изменение.
4. Run 3 — тёплый: тот же сценарий, что Run 2.
5. Сравнить Run 2 vs Run 3 (состояние идентично) → изолированный эффект изменения.
6. Run 4 (по желанию) — холодный после изменения: убедиться, что реальный первый запуск не ухудшился.

**Проверенные данные (09.08.2026):**
- Папка `D:\Arx\Software Downloads\Youtube-Edit` (3465 img, avg 137 KB): 1-й холодный прогон **59 img/sec**; повторные тёплые **83–92 img/sec** (метаданные NTFS прогреты).
- Папка `_Tattoo` (895 img, avg 2 MB): тёплые прогоны `readers 1` = **35.5 / 41.6 img/sec**, `readers 3` = **17.3**, `readers 6` = **18.6**. Параллельное чтение файлов на HDD резко хуже (head thrashing), fileRead распухает (27ms → 172–266ms/image). Текущий дизайн (один последовательный читатель + N декодеров) подтверждён.
- Холодный baseline пользователя через GUI на `_Tattoo` (~38 img/sec) совпадает с тёплым `readers 1` — GUI не вносит искажений (запускает тот же `NvJpegCollector.exe --input --output`, вывод не парсит).

**Спорные «разгонные» коммиты, требующие перепроверки по этому протоколу** (если решение опиралось на цифры): `c6729a7` («...113 img/sec»), `6febedf`, `c326490`, `4ddd1cd`.

---

## Процесс создания релиза

### Пошаговая инструкция

1. Обновить version в src/version.txt
2. Собрать C++ проекты: cmd\Deploy.cmd (или msbuild src\AntiDupl.sln /p:Configuration=Release /p:Platform=x64) — теперь exe/dll падают прямо в bin\Release, отдельно копировать не нужно
3. Собрать C#: dotnet build src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj -c Release
4. Self-contained publish: dotnet publish src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj -c Release -r win-x64 --self-contained true -o out/publish
5. Добавить native deps: скопировать AntiDupl.dll, nvjpeg64_13.dll, cudart64_13.dll, NvJpegCollector.exe, data/ в out/publish (nvjpeg64_12.dll более не требуется, т.к. collector линкуется против 13.x)
6. Zip: cd out/publish && 7za a -tzip ..\bin\AntiDupl.NET-{VER}.zip *
7. GitHub: git tag + gh release create --repo Sucotasch/AntiDuplPlus

### Критические замечания
- dotnet publish НЕ копирует native P/Invoke DLL (AntiDupl.dll) - копировать вручную
- MakeBin.cmd устарел - не включает CUDA зависимости
- zip должен содержать файлы в корне, не в поддиректории out/publish
- gh release create требует --repo Sucotasch/AntiDuplPlus
