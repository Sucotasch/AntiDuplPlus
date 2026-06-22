# Верифицированный план развития AntiDuplPlus

Каждый пункт проверен против актуального кода. Отмечены найденные проблемы и скорректированные решения.

---

## ⚠️ Архитектурный справочник (читать перед любыми изменениями)

Этот раздел фиксирует подтверждённую архитектуру проекта. **Не пытайтесь «исправлять» описанное здесь** — оно работает именно так по замыслу.

### Два независимых формата .adi и два независимых кодепути загрузки

> [!NOTE]
> В проекте существуют **два разных** формата `.adi` файлов и **два разных** кодепути их загрузки. Это намеренная архитектура, а не несовместимость.

#### Формат 1: DLL-native (с заголовками)
- **Кто пишет**: DLL (`SaveData` в [adImageDataStorage.cpp](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl/adImageDataStorage.cpp):406), через `TOutputFileStream`.
- **Структура**: Начинается с 4-байтового заголовка `"adid"` + 4-байтовой версии (`FILE_VERSION`). Далее — данные через `TInputFileStream`.
- **Кто читает**: DLL, `LoadData` (строка 459) через `TInputFileStream(fileName, DATA_CONTROL_BYTES)`. Индексный файл — через `"adii"`.
- **Сценарий**: Стандартное сканирование папок (без утилиты).

#### Формат 2: Collector-native (без заголовков, raw fwrite)
- **Кто пишет**: `NvJpegCollector` ([main.cpp](file:///c:/Users/sucot/AntiDuplPlus/src/NvJpegCollector/main.cpp):469-521), через `fwrite` напрямую.
- **Структура**: `thumbSize(u32)`, `key(i16)`, `first(wstring)`, `last(wstring)`, `count(u64)`, затем N записей `TImageData` в raw-формате. **Без заголовков.**
- **Кто читает**: DLL, `LoadDatabase` в [adSearcher.cpp](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl/adSearcher.cpp) через `fread`, ожидая именно этот raw-формат.
- **Сценарий**: Загрузка предобработанных баз, созданных GPU-утилитой.

**Вывод**: `TInputFileStream` (с заголовками) и `LoadDatabase` (без заголовков) — **разные** функции для разных форматов. Они никогда не пересекаются. Базы коллектора **не предназначены** для загрузки через `LoadData`, и это нормально.

---

### GPU: два режима работы и два буфера

> [!NOTE]
> `GpuCompareAllVsAll` (adGPU.cu:611) **не использует** глобальные буферы `g_pDeviceThumbnailBuffer`. Она аллоцирует собственную VRAM через `cudaMalloc` внутри вызова и освобождает после. Глобальные буферы (`GpuCreateBuffer`/`GpuUploadThumbnail`) используются только в режиме `OneVsList` (CPU fallback). Это два независимых GPU-кодепути.

---

### Что уже исправлено и работает

- ✅ **GlobalLock leak в adJxl.cpp** — RAII-обёртка `GlobalUnlockGuard`.
- ✅ **NVJPEG BGRI + Pinned Memory** — `NVJPEG_OUTPUT_BGRI` + `thread_local cudaHostAlloc` в `adNvJpeg.cpp`.
- ✅ **Кодировка ad_database.xml** — коллектор пишет UTF-8 через `WideCharToMultiByte`.
- ✅ **Портативность путей** — все пути относительно `.exe`, реестр `ad_database.xml` рядом с `.exe`.

---

## Примечания по верификации

> [!WARNING]
> **Пункт 1.2 (GPU Deadlock)**: `GpuCompareAllVsAll` аллоцирует **собственную** VRAM и не трогает глобальные буферы. Коллбэк вызывается после `cudaDeviceSynchronize`, под тем же lock, но **не** обращается к GPU Manager. Сейчас Deadlock не возникает. Замена на `recursive_mutex` — превентивная страховка на случай будущих изменений в Этапе 3.

> [!NOTE]
> **Пункт 2.1 (ClearMemory)**: `ClearMemory()` → `GpuManager()->ClearBuffer()` → `GpuReleaseBuffer()` + `GpuCreateBuffer()`. Пересоздаёт глобальные буферы (OneVsList режим). В режиме AllVsAll не влияет. Вызов безопасен.

---

## Этап 1: Стабилизация

### 1.1 Логическая ошибка валидации

**Файл**: [adResultStorage.cpp](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl/adResultStorage.cpp) строка 160
**Также**: строка 163 и строка 176 — **та же ошибка повторяется трижды**.

**Текущий код** (все три места):
```cpp
// Строка 160:
if(localActionType < 0 && localActionType >= AD_LOCAL_ACTION_SIZE)
// Строка 163:
if(targetType < 0 && targetType >= AD_TARGET_SIZE)
// Строка 176:
if(renameCurrentType < 0 && renameCurrentType >= AD_RENAME_CURRENT_SIZE)
```

**Исправление**: Заменить `&&` на `||` во всех трёх местах.

**Последствия**: Нулевые. Эти проверки сейчас вообще не срабатывают, поэтому исправление только добавляет защиту.

---

### 1.2 GPU Deadlock (Рекурсивный мьютекс)

**Файл**: [adGPUManager.h](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl/adGPUManager.h) строка 97

**Анализ реального потока вызовов**:
1. `CompareAllVsAll` (строка 84) берёт `lock_guard<mutex>`.
2. Внутри `GpuCompareAllVsAll` (adGPU.cu:611) — длинная функция с `cudaMalloc`, kernel launch, `cudaDeviceSynchronize`.
3. Коллбэк `MatchCallback` (adEngine.cpp:245) вызывается **после** sync, под тем же lock.
4. `MatchCallback` вызывает `Result()->AddDuplImagePair()` — это **не** обращается к GPU Manager.

**Вывод**: Сейчас Deadlock **не возникает**, но при будущих изменениях (Этап 3) коллбэк может начать обращаться к менеджеру. Замена на `recursive_mutex` — превентивная мера.

**Исправление**:
```cpp
// adGPUManager.h строка 97
mutable std::recursive_mutex m_mutex;
```
Все `std::lock_guard<std::mutex>` заменить на `std::lock_guard<std::recursive_mutex>`.

**Последствия**: Оверхед ~20нс на захват — ничтожно для GPU-операций.

---

### 1.3 Утечки памяти (hGlobal в очередях)

**Файлы**: [adThreadManagement.h](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl/adThreadManagement.h), [adThreadManagement.cpp](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl/adThreadManagement.cpp)

**Анализ потока данных**:
1. `TCollectManager::Add` (строка 353): вызывает `LoadFileToMemory` → `pImageData->hGlobal` выделена.
2. Данные ставятся в очередь (строка 359).
3. `TCollectTask::DoOwn` (строка 174-184): вызывает `Fill()` → внутри `FreeGlobal()` (строка 75 adDataCollector.cpp).
4. **Если `Finish()` вызван до обработки** — `TThreadManager::Finish()` (строка 199) посылает `Queue()->Finish()` и ждёт поток. Поток выходит из цикла `Work()` при получении `FINISH` от `Pop()`.

**Проблема**: Очередь `m_pQueue` в `TThreadQueue` содержит `TData` с `TImageData*`. Деструктор `~TThreadQueue()` (строка 50) просто делает `delete m_pQueue` — это уничтожает `std::queue`, но **не** вызывает `FreeGlobal()` на оставшихся элементах.

**Важный нюанс**: `TData.data` — это **не владеющий** указатель. `TImageData` принадлежит `m_pImageDataStorage`. Вызов `FreeGlobal()` безопасен — он освобождает только `hGlobal`, не удаляя сам объект.

**Исправление** — добавить в `TThreadQueue`:
```cpp
void Clear() {
    TCriticalSection::TLocker locker(m_pCS);
    while(!m_pQueue->empty()) {
        TData data = m_pQueue->front();
        m_pQueue->pop();
        if(data.data) data.data->FreeGlobal();
    }
}
```
В `TThreadManager::Finish()` (строка 199), **перед** `delete i->task`:
```cpp
i->task->Queue()->Finish();
WaitForSingleObject(i->thread->Handle(), INFINITE);
i->task->Queue()->Clear(); // Освобождаем необработанные hGlobal
delete i->thread;
delete i->task;
```

**Последствия**: При нормальном завершении очередь пуста → `Clear()` — no-op. При принудительной остановке — гарантированное освобождение.

---

### 1.4 SSIM Race Condition

**Файл**: [adImageComparer.cpp](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl/adImageComparer.cpp) строки 447-482

**Анализ**: `TImageComparer_SSIM::IsDuplPair` проверяет `if (pFirst->data->average == 0)` и записывает новые значения. Этот метод вызывается из `CompareWithSet` (строка 142), который работает в **потоках сравнения**. Одно изображение может сравниваться одновременно в нескольких потоках → гонка записи в `average` и `varianceSquare`.

**Два источника данных**:
1. **NvJpegCollector** (строки 518-519 main.cpp): записывает `avg=0, var=0` — **намеренно нулевые заглушки**, которые никогда не были реализованы.
2. **DLL FillPixelData** (adDataCollector.cpp): декодирует изображения и строит thumbnail, но также НЕ рассчитывает SSIM-статистику.

> [!CAUTION]
> **Не трогайте** формат записи в `main.cpp` (строки 480-521). Raw `fwrite` без заголовков — это намеренный формат коллектора, совместимый с `LoadDatabase` в DLL. Изменение формата сломает загрузку. Менять нужно только **значения** `avg`/`var` (строки 518-519), оставляя структуру записи нетронутой.

**Решение (3 файла)**:

#### A. NvJpegCollector — рассчитывать при создании базы
В `ProcessDecoded` (main.cpp, после строки 318):
```cpp
// Расчёт SSIM статистики
uint64_t sum = 0, sumSq = 0;
for (int i = 0; i < thumbSize * thumbSize; i++) {
    sum += gray[i];
    sumSq += (uint64_t)gray[i] * gray[i];
}
info.average = (float)sum / (thumbSize * thumbSize);
float avgSq = (float)sumSq / (thumbSize * thumbSize);
info.varianceSquare = fabs(avgSq - (info.average * info.average));
```
И записывать в файл (строки 518-519 заменить):
```cpp
fwrite(&info.average, 4, 1, dataFile);
fwrite(&info.varianceSquare, 4, 1, dataFile);
```

#### B. DLL FillPixelData — рассчитывать при сборе (fallback)
В `adDataCollector.cpp`, после строки 138 (`data.filled = true;`):
```cpp
// Предрассчитываем SSIM-статистику (потокобезопасно — каждый файл обрабатывается одним потоком)
if (data.average == 0) {
    uint64_t sum = 0;
    SimdValueSum(data.main, data.side, data.side, data.side, &sum);
    data.average = (float)sum / (data.side * data.side);
    uint64_t sumSq = 0;
    SimdSquareSum(data.main, data.side, data.side, data.side, &sumSq);
    float avgSq = (float)sumSq / (data.side * data.side);
    data.varianceSquare = fabs(avgSq - (data.average * data.average));
}
```

#### C. Удалить расчёт из IsDuplPair
В `adImageComparer.cpp`, удалить строки 447-482 (четыре блока `if (average == 0)`). Метод становится read-only.

**Последствия**: 
- Для коллектора: +доли мс на файл (арифметика над 1024 байтами).
- Для DLL: расчёт выполняется в `FillPixelData`, который уже работает в одном потоке на файл → безопасно.
- Для SSIM: если данные загружены из новой базы — `average`/`varianceSquare` уже заполнены. Если из старой базы без данных — DLL досчитает при сборе.

---

## Этап 2: Мульти-базовость и Фикс Фантомов

### 2.1 Изоляция сессий

**Файл**: [adEngine.cpp](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl/adEngine.cpp) строки 386-487

**Текущий код** (строка 391-393):
```cpp
m_pStatus->ClearStatistic();
m_pStatus->SetProgress(0, 0);
m_pResult->Clear();
```

**Проблема**: `m_pImageDataStorage` и `m_pImageDataPtrs` не очищаются.

**Исправление** — добавить после строки 393:
```cpp
m_pImageDataStorage->ClearMemory();
m_pImageDataPtrs->clear();
```

**Проверка побочных эффектов**:
- `ClearMemory()` (adImageDataStorage.cpp:83) удаляет все `TImageData`, очищает map, сбрасывает `m_nextGlobalIdx`, вызывает `GpuManager()->ClearBuffer()`.
- `ClearBuffer()` (adGPUManager.h:63) пересоздаёт GPU-буфер с тем же размером.
- `m_pImageDataPtrs->clear()` — очищает список указателей (не удаляет объекты, они уже удалены в `ClearMemory`).

**Последствия**: Каждый `Search()` начинается с чистого листа. База перечитывается с диска (~мс для NVMe).

### 2.2 Мульти-загрузка баз

**Файл**: [adEngine.cpp](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl/adEngine.cpp) строки 396-412

**Текущий код**:
```cpp
bool dbLoaded = false;
for (size_t i = 0; i < m_pOptions->searchPaths.Size() && !dbLoaded; i++) {
    // ...
    dbLoaded = m_pSearcher->LoadDatabase(searchPath.Original());
}
if (!dbLoaded) { m_pSearcher->SearchImages(); }
```

**Проблема**: `&& !dbLoaded` — останавливается после первой найденной базы.

**Исправление**:
```cpp
bool anyDbLoaded = false;
for (size_t i = 0; i < m_pOptions->searchPaths.Size(); i++) {
    if (m_pSearcher->LoadDatabase(m_pOptions->searchPaths[i].Original())) {
        anyDbLoaded = true;
    }
}
if (!anyDbLoaded) { m_pSearcher->SearchImages(); }
```

**Проверка `LoadDatabase`** (adSearcher.cpp:195-235):
- Вызывает `m_pImageDataStorage->Load(dbFolder, true)` — загружает все записи в `m_storage`.
- Затем итерирует **весь** `m_storage` и добавляет в `m_pImageDataPtrs`.

**Проблема**: При загрузке второй базы `LoadDatabase` снова итерирует **весь** `m_storage` (включая первую базу) → дубли в `m_pImageDataPtrs`.

**Исправление `LoadDatabase`** (adSearcher.cpp:222-231):
```cpp
adError result = m_pImageDataStorage->Load(dbFolder.c_str(), true);
if (result == AD_OK) {
    // Добавляем только НОВЫЕ записи (по globalIdx)
    size_t prevSize = m_pImageDataPtrs->size();
    const TImageDataStorage::TStorage& storage = m_pImageDataStorage->Storage();
    for (auto it = storage.begin(); it != storage.end(); ++it) {
        if (it->second && it->second->data && it->second->globalIdx >= prevSize) {
            m_pImageDataPtrs->push_back(it->second);
        }
    }
    return true;
}
```

**Альтернатива (проще)**: Запомнить размер `m_storage` до загрузки и добавить только элементы с `globalIdx >= prevCount`. Это работает потому что `Insert()` (adImageDataStorage.cpp:78) присваивает последовательные `globalIdx`.

---

## Этап 3: Архитектура Пулов

### 3.1 Расширение API

**Файл**: [adOptions.h](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl/adOptions.h)

Добавить в `TOptions`:
```cpp
enum TPoolCompareMode { ALL_VS_ALL = 0, POOL1_INTERNAL, POOL2_INTERNAL, POOL1_VS_POOL2 };
TPoolCompareMode poolMode;
TPathContainer pool1Paths;
TPathContainer pool2Paths;
```

**Последствия**: `TPathContainer` уже используется для `searchPaths`, `ignorePaths` и т.д. — проверенный тип. Нужно добавить сериализацию в `Load`/`Save`.

### 3.2 GPU Маскирование

**Файл**: [adGPU.cu](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl/adGPU.cu) строка 74, `AllVsAllKernel`

**Текущая сигнатура** ядра (строки 74-84):
```cuda
__global__ void AllVsAllKernel(
    const uint8_t* thumbnails, const uint64_t* crcArray,
    size_t thumbSize, size_t count, double threshold,
    double maxDifference, double addDiffForCrcMismatch,
    size_t maxMatches, Match* results, size_t* matchCount)
```

**Расширение**: Добавить `const uint8_t* poolMask, int mode`.
- `poolMask[i]`: 0 = не в пуле, 1 = Pool1, 2 = Pool2, 3 = оба.
- Один `uint8_t*` массив вместо двух `bool*` — экономия VRAM и bandwidth.

**В начале внутреннего цикла** (после строки 102):
```cuda
for (size_t j = i + 1 + threadIdx.x; j < count; j += numThreads) {
    // Pool filtering — мгновенный выход для ненужных пар
    if (mode != 0) { // 0 = ALL_VS_ALL
        uint8_t pi = poolMask[i], pj = poolMask[j];
        bool skip = true;
        if (mode == 1 && (pi & 1) && (pj & 1)) skip = false;      // POOL1_INTERNAL
        else if (mode == 2 && (pi & 2) && (pj & 2)) skip = false;  // POOL2_INTERNAL
        else if (mode == 3 && (((pi & 1) && (pj & 2)) || ((pi & 2) && (pj & 1)))) skip = false; // CROSS
        if (skip) continue;
    }
    // ... остальной код сравнения
```

**Подготовка маски** в `ExecuteGpuAllVsAllComparison` (adEngine.cpp):
```cpp
std::vector<uint8_t> poolMask(validCount, 0);
for (size_t k = 0; k < validCount; k++) {
    if (IsInPool1(imageByIndex[k]->path)) poolMask[k] |= 1;
    if (IsInPool2(imageByIndex[k]->path)) poolMask[k] |= 2;
}
```

**Последствия**: Для `ALL_VS_ALL` (mode=0) — ветка `if(mode!=0)` не выполняется, нулевой оверхед. Для режимов пулов — одно чтение из global memory + битовые операции, ~1нс на пару.

### 3.3 Database Manager UI

**Файл**: [DatabaseManagerForm.cs](file:///c:/Users/sucot/AntiDuplPlus/src/AntiDupl.NET.WinForms/Forms/DatabaseManagerForm.cs)

**Текущая структура**: Один `DataGridView` + `BindingList<DbEntry>` + три кнопки.

**Решение**: Реорганизовать форму с тремя панелями:
1. **Реестр** (все доступные базы) — вверху.
2. **Pool 1** и **Pool 2** — внизу, рядом.
3. Кнопки `→ Pool 1`, `→ Pool 2`, `← Remove` для переноса баз.
4. `ComboBox` для выбора режима сравнения.

**Передача данных в движок**: Через существующий P/Invoke механизм (`adOptionsSet` в CoreDll.cs). Нужно расширить `adAdvancedOptions` структуру полями `poolMode`, `pool1Count`, `pool2Count` и передавать пути через `adPathSet`.

---

## Стратегия внедрения

1. **Этап 1** → сборка → тест (поиск по 1000+ фото с SSIM) → коммит.
2. **Этап 2** → сборка → тест (загрузка 2+ баз, проверка отсутствия фантомов) → коммит.
3. **Этап 3** → итеративно: C++ backend → CUDA → C# UI → интеграционный тест.

## Verification Plan

### Этап 1
- Запуск поиска с алгоритмом SSIM → убедиться, что результаты корректны и нет крашей.
- Принудительная остановка поиска в середине → проверить, что память не утекает (Task Manager).

### Этап 2
- Создать 2 базы через NvJpegCollector → загрузить обе → убедиться, что нет дублей в результатах.
- Перезапустить поиск → убедиться, что старые результаты НЕ примешиваются.

### Этап 3
- Pool1 vs Pool2: убедиться, что пары внутри Pool2 (эталон) не появляются в результатах.
- ALL_VS_ALL: убедиться, что результаты идентичны текущему поведению (регрессия).
