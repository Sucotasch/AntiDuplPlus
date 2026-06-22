# AntiDuplPlus - Проект NvJpegCollector и Database Manager

## Краткое описание
Интеграция GPU-ускоренного декодирования JPEG через nvJPEG и системы управления базами данных изображений.

## Текущий статус (на 08.04.2026)
✅ **NvJpegCollector**: GPU декодирование JPEG (113 img/sec), WIC для остальных форматов
✅ **Портативность**: Все данные хранятся рядом с `.exe`, никаких жестких путей
✅ **База данных**: Создается в `databases\ИмяПапки\index.adi + 0000.adi`
✅ **Реестр**: `ad_database.xml` рядом с `.exe`, относительные пути
✅ **Database Manager UI**: Список баз, Open Folder, Remove
✅ **Автоматическая загрузка**: Программа подхватывает готовые базы, пропуская сканирование файлов
✅ **Аудит исправлен**: GlobalLock leak (JXL), BGRI оптимизация, Pinned Memory (nvJPEG)
⚠️ **Нерешённый вопрос**: Батчинг (batch_size > 1) не тестировался после фикса RGBI — возможно теперь работает

---

## СИСТЕМНЫЕ ПУТИ И КОНФИГУРАЦИЯ (для восстановления)

### Папка проекта
`c:\Users\sucot\AntiDuplPlus\`

### Ключевые пути
- **Исходники**: `src\` (AntiDupl, NvJpegCollector, AntiDupl.NET.WinForms, AntiDupl.NET.Core, AntiDupl.NET.WPF)
- **Сборка (промежуточная)**: `src\bin\Release\`
- **Сборка (финальная)**: `bin\Release\`
- **Объекты компиляции**: `obj\Release\`, `src\obj\Release\`
- **vcpkg корень**: `vcpkg\`
- **vcpkg установленные**: `src\vcpkg_installed\x64-windows-static\`

### CUDA Toolkit
`C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.8\`
- Библиотеки: `lib\x64\nvjpeg.lib`, `lib\x64\cudart.lib`
- Заголовки: `include\nvjpeg.h`
- DLL: `bin\nvjpeg64_12.dll` (копируется в `bin\Release\`)

### MSBuild
`C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe`

### .NET SDK
`C:\Program Files\dotnet\sdk\9.0.307\`

---

## NVIDIA NVJPEG - КРИТИЧЕСКИ ВАЖНЫЕ ФАКТЫ

### Архитектура GPU
- RTX 4070 Ti Super → Ada Lovelace → SM 8.9
- Target компиляции: `-gencode=arch=compute_89,code=\"sm_89,compute_89\"`
- **НЕ поддерживает NVJPEG_BACKEND_HARDWARE** (это только для серверных GPU A100/H100)
- Используется `NVJPEG_BACKEND_DEFAULT` (software decoder на GPU)

### nvJPEG API - Что работает, что нет

#### РАБОТАЕТ:
```cpp
// 1. Инициализация
nvjpegDevAllocator_t dev_alloc = { cudaMalloc, cudaFree };
nvjpegPinnedAllocator_t pinned_alloc = { cudaHostAlloc, cudaFreeHost };
nvjpegCreateEx(NVJPEG_BACKEND_DEFAULT, &dev_alloc, &pinned_alloc, NVJPEG_FLAGS_DEFAULT, &handle);
nvjpegJpegStateCreate(handle, &state);

// 2. Batch decode с batch_size=1 (индивидуально для каждого файла)
nvjpegDecodeBatchedInitialize(handle, state, 1, 1, NVJPEG_OUTPUT_RGBI); // ВАЖНО: RGBI, не RGB!
nvjpegDecodeBatched(handle, state, &data_ptr, &data_len, &output, nullptr); // nullptr = default stream
cudaDeviceSynchronize();

// 3. Pitch выравнивание - ОБЯЗАТЕЛЬНО!
size_t pitch = ((srcW * 3 + 31) / 32) * 32; // выравнивание по 32 байта
cudaMalloc(&gpuBuf, pitch * srcH);
output.channel[0] = gpuBuf;
output.pitch[0] = (int)pitch;
cudaMemcpy2D(dst, srcW*3, gpuBuf, pitch, srcW*3, srcH, cudaMemcpyDeviceToHost);
```

#### НЕ РАБОТАЕТ (падает с status=5/6 или cudaError 700):
- Batch decode с batch_size > 1 на consumer GPU (RTX 40xx) — отравляет CUDA context
- `nvjpegDecode` (individual) — молча не декодирует
- Decoupled API (`nvjpegDecodeJpegHost/TransferToDevice/Device`) — падает
- Пересоздание state между батчами — не помогает

#### КРИТИЧЕСКИЕ ОШИБКИ:
| Код | Описание | Причина |
|-----|----------|---------|
| 5 | ALLOCATION_FAILED | GPU память исчерпана или context отравлен |
| 6 | EXECUTION_FAILED | Ошибка выполнения на GPU (часто после status=5) |
| 700 (cuda) | cudaErrorIllegalAddress | Доступ за пределы буфера (pitch mismatch) |

### NVJPEG_OUTPUT_RGBI vs RGB
- **RGB** = planar (3 отдельных канала R, G, B) — требует заполнения 3 channel/pitch
- **RGBI** = interleaved (R,G,B,R,G,B...) — один буфер, один pitch
- На consumer GPU с `NVJPEG_BACKEND_DEFAULT` работает ТОЛЬКО RGBI

### Размеры изображений
- nvjpegGetImageInfo() вызывается ДО декодирования для получения размеров
- Для JPEG с субдискретизацией (YUV 4:2:0) размеры могут быть округлены
- nvJPEG автоматически конвертирует в RGB/RGBI при декодировании

---

## АРХИТЕКТУРА ANTI DUPL

### Как работает поиск дубликатов (по шагам)
1. **Сбор изображений**: `TSearcher::SearchImages()` сканирует папки из `searchPaths`, находит все файлы с расширениями JPEG/PNG/BMP и т.т.д.
2. **Декодирование**: Каждое изображение декодируется, создается thumbnail (32x32 grayscale)
3. **Сравнение AllVsAll**: Запускается `AllVsAllKernel` или `SsimAllVsAllKernel` в CUDA — сравнивает КАЖДУЮ пару изображений
4. **Pool фильтрация**: Если poolCompareMode != NONE — пропускает пары не соответствующие режиму
5. **Фильтрация**: Результаты фильтруются по порогам (similarity, size difference, type)
6. **Отображение**: Результаты показываются в GUI с Target колонкой для массового выбора

### Два GPU ядра (adGPU.cu)
- **AllVsAllKernel** — Mean Square: `diff*diff` reduction, shared memory, grid-stride
- **SsimAllVsAllKernel** — SSIM: dot product + scalar SSIM formula
- Оба поддерживают poolMask для фильтрации по пулам

### Два режима поиска
- **Database loading** (`LoadDatabase`): загружает из .adi файлов (быстро)
- **File scanning** (`SearchImages`): сканирует файлы на диске (медленно, декодирует на лету)
- **Проблема**: search paths не доходят до движка → используется file scanning вместо database loading

### Структура модулей
```
AntiDupl.dll (C++/CUDA) ← основная библиотека обработки изображений
├── adEngine.cpp         ← главный движок поиска дубликатов (Search(), UpdateGpuDatabase())
├── adSearcher.cpp       ← поиск и загрузка изображений (ТЗДЕСЬ LoadDatabase())
├── adImageDataStorage.cpp ← чтение/запись .adi файлов (Load(), Save(), LoadIndex(), LoadData())
├── adDatabaseRegistry.cpp ← работа с ad_database.xml (FindByPath(), Load(), Save())
├── adGPU.cu             ← CUDA ядра AllVsAll (сравнение thumbnails на GPU)
├── adNvJpeg.cpp         ← интеграция nvJPEG (для декодирования при сравнении)
└── adImageComparer.cpp  ← сравнение пар изображений (SSIM, histogram)

AntiDupl.NET.WinForms.exe (C#) ← GUI приложение
├── GUIControl\MainMenu.cs     ← меню Tools → Gpu Collector, Db Manager
├── Forms\DatabaseManagerForm.cs ← UI менеджера баз
└── CoreLib.cs                 ← обертка над AntiDupl.dll (P/Invoke вызовы)

NvJpegCollector.exe (C++/CUDA) ← утилита предобработки
└── main.cpp             ← декодирование + сохранение в .adi
```

### Как C# вызывает C++ DLL
- `AntiDupl.NET.Core` содержит `CoreDll.cs` с `DllImport` для всех функций из `AntiDupl.dll`
- Функции экспортируются через `extern "C" __declspec(dllexport)`
- Структуры передаются как `ref` или через `IntPtr`
- Важные экспортируемые функции: `adSearch`, `adStatusGet`, `adResultGet`, `adOptionsSet`

### Формат .adi файлов (бинарный)
**index.adi:**
```
thumbSize: uint32 (4 байта) — размер thumbnail (обычно 32)
count: uint64 (8 байт) — количество групп изображений
key: int16 (2 байта) — ключ группы
first: wstring — путь к первому изображению в группе
last: wstring — путь к последнему изображению
count: uint64 (8 байт) — количество изображений в группе
```

**0000.adi:**
```
thumbSize: uint32 (4 байта)
key: int16 (2 байта)
first: wstring
last: wstring
count: uint64 (8 байт)
N * TImageData:
    path: wstring
    size: uint64
    time: uint64
    hash: uint32
    type: uint8
    width: uint32
    height: uint32
    blockiness: float
    blurring: float
    defect: uint8
    crc32c: uint64
    filled: uint8 (1 = данные есть)
    thumbnail_size: uint64
    thumbnail_data: uint8[thumbnail_size]
    average: float
    varianceSquare: float
```

**wstring формат:** length(uint64) + wchar_t[length*2]

### ad_database.xml формат
```xml
<DatabaseRegistry>
  <Database Path="D:\Photos\Sandy" Folder="databases\Sandy" Name="Sandy" 
            ThumbSize="32" Count="1640" Status="Ready"/>
</DatabaseRegistry>
```
- **Path** — абсолютный путь к папке с изображениями (для идентификации)
- **Folder** — ОТНОСИТЕЛЬНЫЙ путь к папке базы (портативность!)
- **Name** — имя базы (для отображения в UI)
- **Status** — "Ready" = готова к использованию

### Система путей в основной программе
- **searchPaths** — папки для поиска дубликатов (с флагом `enableSubFolder`)
- **ignorePaths** — папки, которые пропускаются при сканировании
- **validPaths** — папки, где разрешено удаление/переименование
- **deletePaths** — папки, куда перемещаются удаленные дубликаты

---

## КОМАНДЫ СБОРКИ (точные)

### NvJpegCollector.exe
```
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" c:\Users\sucot\AntiDuplPlus\src\NvJpegCollector\NvJpegCollector.vcxproj /p:Configuration=Release /p:Platform=x64 /m:1
```
Результат: `src\bin\Release\NvJpegCollector.exe`

### AntiDupl.dll
```
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" c:\Users\sucot\AntiDuplPlus\src\AntiDupl\AntiDupl.vcxproj /p:Configuration=Release /p:Platform=x64 /m:1 /p:VcpkgInstallManifestDependencies=false
```
Результат: `src\bin\Release\AntiDupl.dll`

### WinForms приложение
```
dotnet build c:\Users\sucot\AntiDuplPlus\src\AntiDupl.NET.WinForms\AntiDupl.NET.WinForms.csproj /p:SolutionDir="c:\Users\sucot\AntiDuplPlus\src\\" -c Release
```
Результат: `src\AntiDupl.NET.WinForms\bin\Release\AntiDupl.NET.WinForms.exe`

### Копирование в финальную папку
```
copy /y src\bin\Release\NvJpegCollector.exe bin\Release\
copy /y src\bin\Release\AntiDupl.dll bin\Release\
xcopy /E /I /Y src\AntiDupl.NET.WinForms\bin\Release\*.* bin\Release\
```

---

## ИЗВЕСТНЫЕ ПРОБЛЕМЫ И ИХ РЕШЕНИЯ

### NVJPEG Batch Decode (решено / эксперимент завершен)
**Гипотеза**: Ошибка `status=5/6` при батчинге была вызвана форматом `NVJPEG_OUTPUT_RGB`. После перехода на `BGRI` батчинг должен работать.
**Эксперимент (09.04.2026)**:
1. Реализован реальный батчинг (группировка по subsampling, batch_size=64).
2. **Результат**: Ошибок нет (статус=0), батчинг работает стабильно на любых объемах.
3. **Производительность**:
   - Индивидуальное декодирование: **113 img/sec** (на малых файлах).
   - Батчинг (batch_size=64): **30 img/sec**.
**Причина провала**: На `NVJPEG_BACKEND_DEFAULT` (software decoder) оверхед на подготовку батча (аллокация буферов под макс. размер группы) убивает преимущество параллелизма.
**Решение**: Использовать **индивидуальное декодирование** (по 1 файлу за раз). Батчинг полезен только для серверного железа (`NVJPEG_BACKEND_HARDWARE`), которого у нас нет.
**Примечание**: Скорость бенчмарков сильно зависит от кэша файловой системы Windows. Первый проход (холодный): 51 img/sec. Повторный (горячий): 109 img/sec.

### GlobalLock Leak в JXL (решено — 08.04.2026)
**Проблема**: `nvjpegDecodeBatched` падал после первого батча (status=5/6), затем cudaErrorIllegalAddress (700).
**Что НЕ помогло**:
- Пересоздание state между батчами
- cudaStreamNonBlocking vs default stream
- nvjpegDecode individual
- Decoupled API
- NVJPEG_BACKEND_HARDWARE fallback на DEFAULT
**Решение**: `NVJPEG_OUTPUT_RGBI` (interleaved) вместо `NVJPEG_OUTPUT_RGB` (planar) + отдельный GPU буфер для каждого файла с правильным pitch.
**⚠️ Нерешённый вопрос**: Настоящий батчинг (batch_size > 1) не тестировался после фикса RGBI. Возможно, проблема была именно в planar формате. Требуется проверка.

### GlobalLock Leak в JXL (решено — 08.04.2026)
**Проблема**: `::GlobalLock(hGlobal)` в `TJxl::Load()` не вызывал `::GlobalUnlock()` при ~15 `return NULL;` → утечка блокировок → крах.
**Решение**: RAII-обёртка `GlobalUnlockGuard` — гарантирует unlock при любом выходе.
**Файл**: `src/AntiDupl/adJxl.cpp`

### nvJPEG BGRI оптимизация (решено — 08.04.2026)
**Проблема**: Декодирование в `NVJPEG_OUTPUT_RGBI` + попиксельный swap R↔B на CPU.
**Решение**: Заменено на `NVJPEG_OUTPUT_BGRI` — nvJPEG выдаёт B,G,R напрямую, swap не нужен.
**Файл**: `src/AntiDupl/adNvJpeg.cpp`

### nvJPEG Pinned Memory оптимизация (решено — 08.04.2026)
**Проблема**: `std::vector<unsigned char>` вызывает двойное копирование (GPU → hidden pinned → vector).
**Решение**: `thread_local cudaHostAlloc` буфер — прямой DMA GPU → RAM, переиспользуется между вызовами.
**Файл**: `src/AntiDupl/adNvJpeg.cpp`

### Ошибка cudaMemcpy2D (решено)
**Проблема**: `cudaMemcpy2D failed: an illegal memory access was encountered`
**Причина**: Неправильный pitch при копировании. nvJPEG пишет с одним pitch, а мы читали с другим.
**Решение**: Выделять буфер с pitch = `((srcW*3+31)/32)*32` и использовать тот же pitch для cudaMemcpy2D.

### Git Push (решено)
**Проблема**: Push зависал/падал из-за 1500+ бинарных файлов (obj, bin, vcpkg, audit).
**Решение**: Создан `.gitignore`, файлы удалены из индекса через `git rm -r --cached`.

### Пути к базам (решено)
**Проблема**: `ad_database.xml` создавался в `%APPDATA%`, пути были абсолютными.
**Решение**: Все файлы рядом с `.exe`, пути в реестре относительные (например `databases\FolderName`).

### Старые версии файлов (решено)
**Проблема**: При тестировании запускалась старая версия утилиты, затирающая базы.
**Решение**: Маркер версии `=== NvJpegCollector v2 ===` в выводе, проверка хешей файлов.

---

## ЗАВИСИМОСТИ (vcpkg пакеты)
Указаны в `src\vcpkg.json` и устанавливаются автоматически при сборке AntiDupl.dll:
- **simd** — SIMD оптимизации (Base, Sse41, Avx2, Avx512bw, Avx512vnni)
- **libjpeg-turbo** — декодирование JPEG на CPU (fallback для nvJPEG)
- **openjpeg** — декодирование JPEG 2000
- **libwebp** — декодирование WebP
- **libheif** — декодирование HEIF/HEIC
- **libavif** — декодирование AVIF
- **libjxl** — декодирование JPEG XL

**PNG, BMP, TIFF** декодируются через **GDI+** (adGdiplus.cpp) в основной программе.
В NvJpegCollector все non-JPEG форматы декодируются через **WIC**.

Установленные пакеты лежат в `src\vcpkg_installed\x64-windows-static\` (после первой сборки).
До первой сборки каталог может быть пустым — vcpkg установит зависимости автоматически.

---

## ТЕКУЩАЯ ПРОБЛЕМА: ФАНТОМНЫЕ РЕЗУЛЬТАТЫ (Требует внимания)

### Описание симптома (со слов пользователя)
1. Пользователь создает новую базу через утилиту (например, `Brandy_Ledford`).
2. В **Search - Paths** оставляет **ТОЛЬКО** новые папки (`Brandy`, `Matty`, `Sofa`).
3. Запускает поиск → В результатах **также** присутствуют файлы из **старых баз** (`Sandy`, `Sara_St_James`), которых нет в путях.
4. Перезапуск программы **не помогает** — проблема воспроизводится каждый раз.

### Что я нашел в коде (технический анализ)

#### 1. Глобальное хранилище `m_storage` (adImageDataStorage.cpp)
Все изображения хранятся в `TImageDataStorage::m_storage`. При загрузке базы данных вызывается `LoadData`:
```cpp
if(Find(imageData) == m_storage.end())
    Insert(new TImageData(imageData)); // Добавляет в общий кэш
```

**Проблема**: `m_storage` **никогда не очищается** в течение сессии.

#### 2. LoadDatabase() берет ВСЁ хранилище (adSearcher.cpp, строка 220)
```cpp
const TImageDataStorage::TStorage& storage = m_pImageDataStorage->Storage();
for (auto it = storage.begin(); it != storage.end(); ++it) {
    if (it->second && it->second->data) {
        m_pImageDataPtrs->push_back(it->second); // БЕРЕТ ВСЁ!
    }
}
```
**Что это значит**: Если `m_storage` содержит данные от предыдущих загрузок, они **все** попадут в `m_pImageDataPtrs` и будут сравниваться.

#### 3. Отсутствие пред-очистки (adEngine.cpp)
В функции `TEngine::Search()` **НЕТ** очистки `m_pImageDataPtrs` перед загрузкой новых данных. Очистка `m_pImageDataPtrs->clear()` стоит только в **конце** функции (строка 483).

#### 4. Сохранение результатов (results.adm)
При запуске программа загружает сохраненные результаты (`results.adm`). Это нормальная фича. Но если при загрузке результатов восстанавливаются пути к старым файлам, это может триггерить подгрузку данных.

### ГИПОТЕЗЫ (требуют проверки):
1. **Наследие "Единой базы"**: В исходной архитектуре AntiDupl всегда существовала **только одна база** (один `m_storage`). Все пути считались частью одной коллекции.
   - Мы добавили разделение на файлы (`databases\Sandy`, `databases\Brandy`), но **не изменили логику памяти**.
   - Функция `LoadDatabase` добавляет данные в общий `m_storage`.
   - Функция формирования списка изображений (`adSearcher.cpp`) берет **ВСЁ содержимое `m_storage`**, а не только то, что относится к текущей базе.
   - **Итог**: Программа по-прежнему сравнивает "всё со всем", что когда-либо было загружено в сессию.
2. **Путаница с путями**: Возможно, старые пути остаются в `default.xml` даже после удаления из GUI.
3. **Автозагрузка баз**: Возможно, при старте программа автоматически загружает все готовые базы из `ad_database.xml`, независимо от Search Paths.

### Что нужно сделать:
1. Проверить, очищается ли `m_storage` при создании нового `TEngine`.
2. Проверить, вызывает ли `LoadDatabase` очистку перед загрузкой.
3. Добавить логирование в `Search()`: сколько изображений было ДО загрузки, сколько стало ПОСЛЕ.

### 7. Search paths — КОРЕНЬ НАЙДЕН (2026-06-21)
**Суть**: LoadDatabase() вызывается для 8 путей, все проходят проверки, но Load() возвращает AD_ERROR_UNKNOWN.
**Трейс** (12 точек):
- [C#1-C#6] Пути установлены корректно ✅
- [C#7] searchPaths.Size()=8 ✅
- [C#8] LoadDatabase для 8 путей ✅
- [C#10] Status=Ready, Enabled=1, Folder=correct ✅
- [C#12] LoadIndex: index.adi, reducedImageSize=32 ✅
- [C#12] **LoadIndex EXCEPTION: error=4** (INVALID_FILE_FORMAT) ❌
- [C#10] Load result=1 ❌
**Корень**: NvJpegCollector пишет **Collector-native** формат (raw fwrite, ThumbSize=32). `LoadIndex()` ожидает **DLL-native** формат (заголовок "adid"). Первые 4 байта index.adi = 0x20000000 (ThumbSize=32), НЕ "adid". error=4 = INVALID_FILE_FORMAT.
**Решение**: Изменить `Load()` чтобы определять формат: если первые 4 байта != "adid" → читать как Collector-native. НЕ трогать `LoadIndex()` (она для DLL-native).

---

## ИЗВЕСТНЫЕ БАГИ

---

## ИЗВЕСТНЫЕ БАГИ (подтверждённые)

### 1. Encoding conflict: C++ wofstream vs C# File.WriteAllText
**Суть**: C++ утилита писала `ad_database.xml` через `wofstream` (системная кодировка), а C# менеджер — через `File.WriteAllText` (UTF-8). Они портили файл друг за другом.
**Статус**: ✅ **ИСПРАВЛЕНО** (09.04.2026). Утилита теперь пишет UTF-8 через `WideCharToMultiByte`.

### 2. NVJPEG Batch Decode (batch_size > 1)
**Суть**: `nvjpegDecodeBatched` с batch_size > 1 давал 30 img/sec вместо 113 img/sec (оверхед на software decoder).
**Статус**: ✅ **ПРИНЯТО**. Оставляем batch_size=1. Батчинг работает, но бесполезен для consumer GPU.

### 3. GlobalLock Leak в adJxl.cpp
**Суть**: RAII-обёртка `GlobalUnlockGuard` предотвращает утечку дескрипторов.
**Статус**: ✅ **ИСПРАВЛЕНО**.

### 4. nvJPEG BGRI + Pinned Memory
**Суть**: Замена RGBI на BGRI + thread_local буфер для декодирования в основной DLL.
**Статус**: ✅ **ИСПРАВЛЕНО**.

### 5. Database Manager не видит новые базы
**Суть**: Новые базы, созданные утилитой, не появлялись в UI менеджера.
**Статус**: ✅ **ИСПРАВЛЕНО** (исправление кодировки).

### 6. Search не очищает данные перед запуском
**Суть**: Новые результаты поиска смешиваются со старыми данными из `m_storage`.
**Статус**: ✅ **ИСПРАВЛЕНО** (2026-06-20). `ClearMemory()` + `m_pImageDataPtrs->clear()` в начале Search().

### 7. Search paths не доходят до движка (2026-06-20)
**Суть**: C# `GetEnabledDatabasePaths()` возвращает 12 путей, но `m_pOptions->searchPaths.Size() == 0` в C++. Изображения загружаются через `SearchImages()` (сканирование файлов), а не из баз.
**Корень**: `adPathWithSubFolderSetW` может молча вернуть `AD_ERROR_ACCESS_DENIED` из-за `CHECK_ACCESS` макроса. C# `SetPath()` игнорирует результат.
**Исправление**: Требуется — добавить проверку возврата в `SetPath()` и логирование ошибок.

### 8. Enabled checkbox не сохранялся (2026-06-20)
**Суть**: При закрытии DatabaseManagerForm изменения Enabled не сохранялись в ad_database.xml.
**Корень**: Не было `CellContentClick` handler и `FormClosing` save.
**Статус**: ✅ **ИСПРАВЛЕНО**.

### 9. Pool mode не работал (2026-06-20)
**Суть**: ComboBox PoolCompareMode не влиял на поиск.
**Корень**: `CoreCompareOptions` не содержал `poolCompareMode`. Не было default в adOptions.cpp.
**Статус**: ✅ **ИСПРАВЛЕНО**.

---

## Текущие задачи (TODO)

### Приоритет 0: Критический баг — Search paths не доходят до движка
- [ ] **Исправить**: Добавить проверку возврата в `SetPath()` (CoreLib.cs)
- [ ] **Исправить**: Добавить логирование ошибок в `adPathWithSubFolderSetW`
- [ ] **Исправить**: Проверить `CHECK_ACCESS` макрос — почему он блокирует установку путей

### Приоритет 1: GPU SSIM верификация
- [ ] Запустить поиск с SSIM на реальных данных → убедиться что GPU используется
- [ ] Сравнить GPU SSIM vs CPU SSIM → производительность
- [ ] Проверить что poolCompareMode корректно фильтрует результаты

### Приоритет 2: Этап 4 — Архитектура Пулов
- [ ] Pool1/Pool2 API (adOptions.h)
- [ ] GPU pool masking (adGPU.cu)
- [ ] Database Manager 3-panel UI
- [ ] Mass auto-select (CloneSpy-style)
- [ ] Кэширование загруженных баз в памяти
- [ ] Инкрементальное обновление баз (только новые файлы)

---

## Структура файлов (портативная)
```
bin\Release\
├── AntiDupl.NET.WinForms.exe    # Главная программа
├── NvJpegCollector.exe          # Утилита GPU декодирования
├── ad_database.xml              # Реестр баз данных (портативный)
├── AntiDupl.dll                 # Основная DLL
└── databases\
    ├── Sandy_0650\
    │   ├── index.adi
    │   └── 0000.adi
    └── Sara_St_James\
        ├── index.adi
        └── 0000.adi
```

## Оборудование
- **GPU**: NVIDIA RTX 4070 Ti Super 16GB (Ada Lovelace, SM 8.9)
- **CUDA**: 12.8
- **RAM**: 64GB
- **nvJPEG**: NVJPEG_BACKEND_DEFAULT
- **NvJpegCollector**: NVJPEG_OUTPUT_RGBI (batch_size=1)
- **adNvJpeg.cpp**: NVJPEG_OUTPUT_BGRI (batch_size=1, pinned memory)

## Ключевые файлы проекта
- `src/NvJpegCollector/main.cpp` — утилита GPU декодирования
- `src/AntiDupl/adDatabaseRegistry.cpp/h` — реестр баз данных
- `src/AntiDupl/adSearcher.cpp/h` — загрузка готовых баз
- `src/AntiDupl/adEngine.cpp` — интеграция загрузки баз в поиск
- `src/AntiDupl.NET.WinForms/Forms/DatabaseManagerForm.cs` — UI менеджера баз
- `src/AntiDupl.NET.WinForms/GUIControl/MainMenu.cs` — меню Tools
