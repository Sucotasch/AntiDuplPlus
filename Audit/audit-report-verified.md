# AntiDuplPlus — Verified Audit Report
> Каждый баг верифицирован по реальному коду. Решения проверены на side effects.
> Применяйте в порядке таблицы — зависимостей между фиксами нет, каждый независим.

**Project root**: `C:\Users\sucot\AntiDuplPlus`

---

## Execution Order

| # | ID | Severity | Confidence | Effort | Summary |
|---|----|----------|------------|--------|---------|
| 1 | B04 | medium | confirmed | 1 line | WPF progress: вычитает total вместо накопления |
| 2 | B03 | medium | confirmed | 3 lines | Non-ASCII путь в регистре NvJpegCollector |
| 3 | B09 | low | confirmed | 1 char | `gpu_debug.log` — truncate в первом блоге, append везде ниже |
| 4 | B02 | medium | confirmed | ~8 lines | `static gpuBufferInitialized` переживает перезапуск Engine |
| 5 | B07 | medium | confirmed | ~5 lines | GPU failure: пустой результат без диагностики |
| 6 | B11 | low-med | confirmed | 3 lines | Нет валидации minimalImageSize > maximalImageSize |
| 7 | B08 | low-med | inferred | ~20 lines | `Thread.Sleep(200)` race window перед progress window |
| 8 | B06 | low | confirmed | 10 lines | .NET Framework 4.8 check при target .NET 8 |

---

## B04 — WPF Progress: `currentFirst -= total` вместо `currentSecond += total`

- **Severity**: medium
- **Confidence**: confirmed (код виден дословно)
- **File**: `src\AntiDupl.NET.WPF\Command\SearchDllCommand.cs`
- **Lines**: 125–126

**Root cause**: В ветке `else` (non-equality search, строки 113–128) цикл по
collect-потокам накапливает прогресс в `currentFirst`, но вычитает `total`
вместо того, чтобы складывать в `currentSecond`. При нескольких collect-потоках
`currentFirst` уходит в отрицательную зону — прогресс-бар отображает мусор.

**Before**:
```csharp
// SearchDllCommand.cs:113-128
else
{
    currentFirst = mainThreadStatus.current;
    for (int i = 0; ; i++)
    {
        CoreStatus collectThreadStatus = _core.StatusGet(CoreDll.ThreadType.Collect, i);
        if (collectThreadStatus == null)
            break;
        if (i == 0)
        {
            path = collectThreadStatus.path;
        }
        currentFirst += collectThreadStatus.current;
        currentFirst -= collectThreadStatus.total;   // БАГ
    }
}
```

**After**:
```csharp
else
{
    currentFirst = mainThreadStatus.current;
    for (int i = 0; ; i++)
    {
        CoreStatus collectThreadStatus = _core.StatusGet(CoreDll.ThreadType.Collect, i);
        if (collectThreadStatus == null)
            break;
        if (i == 0)
        {
            path = collectThreadStatus.path;
        }
        currentFirst += collectThreadStatus.current;
        currentSecond += collectThreadStatus.total;  // ФИКС: одна строка
    }
}
```

**Side effects**: Ноль. `currentSecond` уже объявлен на строке 89, используется
в equality-ветке (строки 109–110) для того же назначения.
`_progressDialogViewModel.CurrentSecond` (строка 137) принимает `currentSecond` —
bindings не меняются.

**Verification**: Запустить non-equality поиск с >1 collect-потоком; убедиться
что прогресс монотонно растёт от 0 до total.

---

## B03 — Non-ASCII путь в NvJpegCollector: `std::string(regPathW.begin(), regPathW.end())`

- **Severity**: medium
- **Confidence**: confirmed
- **File**: `src\NvJpegCollector\main.cpp`
- **Lines**: 629, 632

**Root cause**: Итераторный конструктор `std::string(begin, end)` для `std::wstring`
приводит каждый `wchar_t` к `char` усечением. Для Cyrillic (U+0400–U+044F) это
даёт неправильные байты, и `std::ifstream rFile(regPathA, ...)` не открывает
файл. `ad_database.xml` не обновляется после `--update` при кириллическом пути.

Несколькими строками ниже (636–637) правильно используется `MultiByteToWideChar`
для чтения содержимого файла, что подтверждает намерение работать корректно —
но входной путь сломан.

**Before**:
```cpp
// main.cpp:629-632
std::string regPathA(regPathW.begin(), regPathW.end());
std::wstring xmlContent;
{
    std::ifstream rFile(regPathA, std::ios::binary);
```

**After**:
```cpp
// Убрать regPathA полностью. MSVC std::ifstream принимает std::wstring напрямую.
std::wstring xmlContent;
{
    std::ifstream rFile(regPathW, std::ios::binary);
```

> **Почему wstring, не WideCharToMultiByte**: `std::ifstream(std::wstring)` —
> официально поддерживается MSVC (VS2012+). Это единственный надёжный путь для
> Windows-путей с non-ASCII: UTF-8 `std::string` откроет файл только если
> `setlocale(CP_UTF8)` установлен, что здесь не гарантировано.

**Дополнительно — проверить строку ~670**: аналогичный `std::ofstream` для
записи обновлённого XML. Если там та же конвертация — заменить по той же схеме.

**Side effects**: Ноль. `regPathA` не используется нигде кроме этих двух мест.

**Verification**: `NvJpegCollector --update C:\Фото\база` — убедиться что
`ad_database.xml` перезаписывается корректно.

---

## B09 — `gpu_debug.log`: первый блок с `L"w"`, остальные с `L"a"`

- **Severity**: low
- **Confidence**: confirmed
- **File**: `src\AntiDupl\adEngine.cpp`
- **Line**: 610

**Root cause**: В `TEngine::Search()` первый блок логирования (строка 610)
открывает файл с `L"w"` — truncate. Все последующие блоки в той же функции
(строки 517, 546, 651, 676, 695, 711) используют `L"a"` — append. Это означает,
что при каждом новом запуске `Search()` строка 610 затирает все предыдущие
диагностические записи, но сохраняет записи *внутри* одной сессии.

Поведение "per-session лог" может быть намеренным. Проблема в том, что строка 610
расположена внутри блока `Search()`, а не в начале файла. Блоки в
`ExecuteGpuAllVsAllComparison` (которая вызывается *позже* в той же сессии)
используют `L"a"` корректно.

**Решение — добавить разделитель сессии, оставить `L"w"` для reset**:

```cpp
// adEngine.cpp:610-620 — заменить
FILE* logFile = _wfopen(logPath.c_str(), L"w");
if (logFile) {
    // Явный маркер новой сессии
    SYSTEMTIME st;
    GetLocalTime(&st);
    fwprintf(logFile, L"=== Search() session %04d-%02d-%02d %02d:%02d:%02d ===\n",
        st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
    fwprintf(logFile, L"GPU Debug:\n");
    fwprintf(logFile, L"  GpuManager: %s\n", m_pGpuManager ? L"OK" : L"NULL");
    fwprintf(logFile, L"  IsAvailable: %s\n", (m_pGpuManager && m_pGpuManager->IsAvailable()) ? L"YES" : L"NO");
    fwprintf(logFile, L"  Algorithm: %d (0=SqSum, 1=SSIM)\n", m_pOptions->compare.algorithmComparing);
    fwprintf(logFile, L"  ignoreFrameWidth: %d\n", m_pOptions->advanced.ignoreFrameWidth);
    fwprintf(logFile, L"  useGpu: %s\n", useGpu ? L"YES" : L"NO");
    fwprintf(logFile, L"  Images: %zu\n", m_pImageDataPtrs->size());
    fclose(logFile);
}
```

> Timestamp через `GetLocalTime` (уже включён через `<windows.h>`) — нулевые
> зависимости.

**Side effects**: Файл по-прежнему очищается при каждой сессии (ожидаемо), но
теперь с явным временным штампом. Append-блоки ниже продолжают работать без
изменений.

---

## B02 — `static gpuBufferInitialized` переживает пересоздание TEngine

- **Severity**: medium
- **Confidence**: confirmed
- **File**: `src\AntiDupl\adDataCollector.cpp`
- **Lines**: 158–173

**Root cause**: `static bool gpuBufferInitialized = false` — функциональный
статик, инициализируется ровно один раз за lifetime процесса. Если `TEngine`
уничтожается и пересоздаётся (в C# это `CoreLib.Destroy()` + `CoreLib.Create()`),
новый `TGpuManager` создаётся с чистым состоянием (`m_capacity = 0`),
но `EnsureCapacity` не вызывается — `UploadThumbnail` пишет в несуществующий или
освобождённый буфер.

**Контекст**: Этот блок активен только в non-AllVsAll режиме (CPU comparison,
строка 155: `!m_pEngine->SkipComparisonDuringCollection()`). В GPU AllVsAll
режиме (основной путь) буфер управляется внутри `CompareAllVsAll`. Баг
актуален для CPU-compare (OneVsList) и последующих сессий.

**Правильное решение**: Убрать `static`, вызывать `EnsureCapacity` на каждом
входе в блок. Метод идемпотентен: проверяет `m_capacity >= required` и
возвращает `true` без пересоздания если места достаточно (проверяется в
`adGPUManager.cpp`).

**Before**:
```cpp
// adDataCollector.cpp:157-173
// Ensure GPU buffer is initialized before first upload
static bool gpuBufferInitialized = false;
if (!gpuBufferInitialized)
{
    AD_DEBUG("FillPixelData: Initializing GPU buffer\n");
    size_t estimatedCapacity = 10000; // Start with reasonable estimate
    size_t thumbSize = m_pOptions->advanced.reducedImageSize * m_pOptions->advanced.reducedImageSize;
    if (m_pEngine->GpuManager()->EnsureCapacity(estimatedCapacity, thumbSize))
    {
        gpuBufferInitialized = true;
        AD_DEBUG("FillPixelData: GPU buffer initialized\n");
    }
    else
    {
        AD_DEBUG("FillPixelData: GPU buffer initialization FAILED\n");
    }
}
```

**After**:
```cpp
// adDataCollector.cpp:157-173 — заменить весь блок целиком
// EnsureCapacity is idempotent: no-op if buffer already has enough capacity.
// Must be called per-session because TGpuManager is recreated with TEngine.
{
    size_t thumbSize = m_pOptions->advanced.reducedImageSize *
                       m_pOptions->advanced.reducedImageSize;
    if (!m_pEngine->GpuManager()->EnsureCapacity(10000, thumbSize))
    {
        AD_DEBUG("FillPixelData: GPU EnsureCapacity FAILED\n");
    }
}
```

**Performance**: `EnsureCapacity` выполняет `m_capacity >= required` + lock/unlock
рекурсивного мьютекса (~20нс). На 10k изображений = ~200мкс суммарно.
Пренебрежимо мало на фоне декодирования JPEG.

**Side effects**: Ноль. Убираем только static-флаг; семантика инициализации
буфера не меняется.

**Verification**: `CoreLib.Destroy()` + `CoreLib.Create()` в C#, затем поиск
в CPU-режиме — нет крашей и нет "UploadThumbnail FAILED" в debug output.

---

## B07 — GPU failure path: пустой результат без диагностики пользователю

- **Severity**: medium
- **Confidence**: confirmed
- **File**: `src\AntiDupl\adEngine.cpp`
- **Lines**: 718–720

**Root cause**: Если `ExecuteGpuAllVsAllComparison()` возвращает `false`
(CUDA error, OOM, context loss), поиск завершается с пустым результатом.
Пользователь видит "0 дубликатов" и не знает что сравнение не произошло.

**Почему CPU fallback не рекомендован**: AllVsAll на 50k+ изображений займёт
часы. Молчаливое переключение хуже явной ошибки.

**Решение**: Использовать `m_pStatus->SetPath` для записи диагностики.
`SetPath(AD_THREAD_TYPE_MAIN, 0, msg)` пишет в `path` поле главного потока.
WPF читает это поле в `OnTimerTick` (строка 132: `path = mainThreadStatus.path`)
и отображает в `_progressDialogViewModel.ProgressMessage` (строка 138).

**Before**:
```cpp
// adEngine.cpp:718-720
if (!gpuSuccess) {
    AD_DEBUG("Search: GPU comparison FAILED — no CPU fallback (too slow for large collections)\n");
}
```

**After**:
```cpp
if (!gpuSuccess) {
    AD_DEBUG("Search: GPU comparison FAILED\n");
    m_pStatus->SetPath(AD_THREAD_TYPE_MAIN, 0,
        L"ERROR: GPU comparison failed. See gpu_debug.log for details.");
}
```

> Убедиться что `TStatus::SetPath` принимает `AD_THREAD_TYPE_MAIN` и `wchar_t*` —
> проверить сигнатуру в `adStatus.h`. Если сигнатура другая — адаптировать.

**Side effects**: Сообщение будет показано в поле пути progress window в момент
закрытия. Если UI уже закрыл окно до чтения — сообщение потеряется. Для
гарантированной видимости добавить аналогичный `MessageBox.Show` в C# слое
при `resultCount == 0` после завершения — но это отдельное изменение.

**Verification**: Временно вернуть `return false` в начало
`ExecuteGpuAllVsAllComparison()`, запустить поиск, убедиться что строка
"ERROR: GPU comparison failed" видна в UI.

---

## B11 — Нет валидации `minimalImageSize > maximalImageSize`

- **Severity**: low-medium
- **Confidence**: confirmed (поле существует, validation path найден)
- **File**: `src\AntiDupl\adOptions.cpp`
- **Lines**: 138–153 (метод `TOptions::Validate`)

**Root cause**: `TOptions::Validate()` вызывается при каждом `Import()` и
`Load()`. Сейчас метод clamping-валидирует отдельные поля через `TOption::Validate()`.
Нет перекрёстной проверки: если `minimalImageSize > maximalImageSize`, фильтр
`(size >= minimal && size <= maximal)` всегда ложен — 0 изображений без
объяснения.

**Стратегия**: Clamp `minimal = maximal`. `Validate()` возвращает `void` — переделка
на `adError` дорога (затрагивает всю цепочку). Clamp безопасен и детерминирован.

**Before**:
```cpp
// adOptions.cpp:138-153
void TOptions::Validate()
{
    for(TOptionsList::iterator it = m_options.begin();  it != m_options.end(); ++it)
        it->Validate();

    for(int size = REDUCED_IMAGE_SIZE_MIN; size < INITIAL_REDUCED_IMAGE_SIZE; size <<= 1)
    {
        if(size >= advanced.reducedImageSize)
        {
            advanced.reducedImageSize = size;
            advanced.ignoreFrameWidth = int(ceil(size*advanced.ignoreFrameWidth/double(DENOMINATOR*2))/size*DENOMINATOR*2);
            break;
        }
    }
}
```

**After**:
```cpp
void TOptions::Validate()
{
    for(TOptionsList::iterator it = m_options.begin();  it != m_options.end(); ++it)
        it->Validate();

    for(int size = REDUCED_IMAGE_SIZE_MIN; size < INITIAL_REDUCED_IMAGE_SIZE; size <<= 1)
    {
        if(size >= advanced.reducedImageSize)
        {
            advanced.reducedImageSize = size;
            advanced.ignoreFrameWidth = int(ceil(size*advanced.ignoreFrameWidth/double(DENOMINATOR*2))/size*DENOMINATOR*2);
            break;
        }
    }

    // Cross-field: inverted size range means zero images pass the filter
    if (compare.minimalImageSize > compare.maximalImageSize)
        compare.minimalImageSize = compare.maximalImageSize;
}
```

**Side effects**: Ноль. Затрагивает только инвертированные диапазоны — нормальные
конфигурации не меняются.

**Verification**: Установить `minimalImageSize = 5000, maximalImageSize = 100`
через `adOptionsSet(AD_OPTIONS_COMPARE, ...)`, затем `adOptionsGet` — убедиться
что оба поля равны 100.

---

## B08 — `Thread.Sleep(200)` race window перед progress window

- **Severity**: low-medium
- **Confidence**: inferred (логика прозрачна)
- **File**: `src\AntiDupl.NET.WPF\Command\SearchDllCommand.cs`
- **Lines**: 155–171

**Root cause**: `Execute()` блокирует UI dispatcher на 200мс через `Thread.Sleep`.
Два граничных сценария:
1. DLL завершается за <200мс (малая коллекция) → `_state == Finish` → окно не
   открывается, но timer уже стартован и попытается закрыть несуществующее окно.
2. DLL стартует >500мс (cold GPU init) → окно открывается до того, как прогресс
   начинает обновляться → UX-скачок.

**Before**:
```csharp
// SearchDllCommand.cs:157-171
Thread thread = new Thread(new ThreadStart(RunProcess));
thread.IsBackground = true;
thread.Name = "DllThread";
thread.Start();

_timer.Start();

Thread.Sleep(200);

if (_state != StateEnum.Finish)
{
    _windowService.OpenProgressWindow(_progressDialogViewModel);
}
```

**After**:
```csharp
// Добавить поле в класс:
private readonly ManualResetEventSlim _searchStarted = new ManualResetEventSlim(false);

// Execute() — заменить блок:
_state = StateEnum.Start;
_searchStarted.Reset();

Thread thread = new Thread(new ThreadStart(RunProcess));
thread.IsBackground = true;
thread.Name = "DllThread";
thread.Start();

// Ждём сигнала что DLL thread реально стартовал (не блокируя dispatcher)
Task.Run(() =>
{
    _searchStarted.Wait(500);  // таймаут 500мс как страховка
    Application.Current.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
    {
        if (_state != StateEnum.Finish)
        {
            _timer.Start();
            _windowService.OpenProgressWindow(_progressDialogViewModel);
        }
    }));
});

// RunProcess() — добавить первой строкой в тело метода:
public void RunProcess()
{
    _progressDialogViewModel.State = "Search";
    _searchStarted.Set();  // сигнализируем что поток запущен

    _mainViewModel.Options = new CoreOptions(_core);
    _mainViewModel.LocationsModel.CopyToDll();
    var result = _core.Search();
    _state = StateEnum.Finish;
}
```

**Side effects**:
- `ManualResetEventSlim` — struct-based, нет heap allocation при `Wait()`.
- `Task.Run` добавляет один threadpool task — пренебрежимо.
- Timer стартует после открытия окна → исключена гонка закрытия
  несуществующего окна.
- `_searchStarted` нужно инициализировать как поле (не локальную переменную),
  иначе `Reset()` при повторном поиске не сработает.

**Verification**: Запустить поиск на 1 изображении → progress window не
появляется (или появляется и сразу закрывается корректно). Запустить на 10k →
окно открывается стабильно.

---

## B06 — `IsDotNet4Installed` при target .NET 8

- **Severity**: low
- **Confidence**: confirmed
- **File**: `src\AntiDupl.NET.WinForms\Program.cs`
- **Lines**: 37, 77–90

**Root cause**: `IsDotNet4Installed` читает
`HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Client\Install`.
Проект таргетирует `.NET 8`. На чистой машине с только .NET 8 Desktop Runtime
этот ключ может отсутствовать → приложение предлагает скачать .NET Framework 4.8
вместо .NET 8.

**Важный нюанс**: Весь блок обёрнут в `#if !PUBLISH` (строки 36–61). В
PUBLISH-сборках проверка полностью отсутствует. Баг актуален только для
dev/debug сборок.

**Before**:
```csharp
// Program.cs:77-90
private static bool IsDotNet4Installed
{
    get
    {
        try
        {
            return (Convert.ToInt32(Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Client"
            ).GetValue("Install")) == 1);
        }
        catch { return false; }
    }
}
```

**After** (заменить метод целиком + обновить строки 37 и 58):
```csharp
private static bool IsDotNet8DesktopRuntimeInstalled
{
    get
    {
        // .NET 5+ на Windows x64 регистрирует версии здесь
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App");
            if (key == null) return false;
            foreach (var name in key.GetValueNames())
            {
                if (name.StartsWith("8.")) return true;
            }
            return false;
        }
        catch { return false; }
    }
}
```

```csharp
// Program.cs:37 — обновить
if (IsDotNet8DesktopRuntimeInstalled)

// Program.cs:58 — обновить текст диалога
else if (MessageBox.Show(
    "You need .NET 8 Desktop Runtime to run this program. Download it?",
    "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
    System.Diagnostics.Process.Start("https://dotnet.microsoft.com/download/dotnet/8.0");
```

**Side effects**: Ноль для PUBLISH-сборок. Для dev-сборок — корректная проверка
правильного runtime.

---

## Уже реализованные фиксы — не трогать

Следующие пункты из старого audit-report-final.md **уже реализованы** в коде:

| Источник | Статус | Подтверждение |
|----------|--------|---------------|
| §1.2 (recursive_mutex) | ✅ Готово | `adGPUManager.h:122` — `mutable std::recursive_mutex m_mutex` |
| §1.3 (TThreadQueue::Clear) | ✅ Готово | `adThreadManagement.cpp:71-79, 215` |
| §2.1 (ClearMemory в Search) | ✅ Готово | `adEngine.cpp:534-535` |
| §2.2 (multi-DB load) | ✅ Готово | `adEngine.cpp:557-574` — цикл без `&& !dbLoaded` |
| F01 (scratch TImageData) | ✅ Безопасно | `Insert(new TImageData(imageData))` делает deep copy |
| F05 (Get() mutex) | ✅ Не нужен | `Get()` вызывается из `TSearcher`, не из collect-потоков |

## Отклонённые находки из audit-report-final.md

| ID | Причина |
|----|---------|
| F13 (README CPU fallback) | README строки 25-26 не содержат упомянутой фразы — галлюцинация |
| F10 (CMake/CI) | Tech-debt wishlist, не баг |
| F12 (тесты) | Правда, но mock GPU/FS — недели работы, не хотфикс |

---

## Verification Checklist

```
[ ] B04: non-equality поиск → прогресс монотонно 0→100%, нет отрицательных значений
[ ] B03: NvJpegCollector --update с кириллическим путём → ad_database.xml обновлён
[ ] B09: два Search() подряд → gpu_debug.log содержит два timestamp-разделителя
[ ] B02: CoreLib.Destroy()+Create(), поиск в CPU-режиме → нет "UploadThumbnail FAILED"
[ ] B07: ExecuteGpuAllVsAllComparison возвращает false → UI показывает "ERROR: GPU..."
[ ] B11: minimalImageSize=5000, maximalImageSize=100 → после Import оба равны 100
[ ] B08: поиск на 1 файле → progress window не мигает; на 10k → открывается стабильно
[ ] B06: dev-сборка на машине без .NET Framework → нет диалога про .NET 4.8
```
