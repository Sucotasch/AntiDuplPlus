# AntiDuplPlus — полный инженерный аудит кода

| Поле | Значение |
|------|----------|
| **Дата** | 2026-08-16 |
| **Базовый коммит** | `04d9495` (master, после синхронизации документации) |
| **Метод** | Сплошной read-only ревью: нативное ядро (`src/AntiDupl`), коллектор (`src/NvJpegCollector`), C# слой (Core + WinForms + WPF), сборка/CI/упаковка. Каждый пункт проверен чтением окружающего кода, ключевые P1 перепроверены вручную. |
| **Валидация** | `cmd\Deploy.cmd` (полная сборка) — результат в §8 |
| **Шкала** | P0 — краш/потеря данных; P1 — неверный результат/поведение; P2 — робастность; P3 — гигиена |
| **Правило** | Код в этом документе — готовые к применению патчи (minimal, production-ready). Поведение не меняется, кроме случаев, где оно объективно сломано. |

**Кратко:** 0×P0, 12×P1, ~20×P2, ~40×P3. Самое важное: (1) `--update` коллектора пишет дубликаты записей для изменённых файлов; (2) GPU-ассист CPU-пути читает неинициализированную VRAM для изображений из БД; (3) авто-выбор игнорирует критерий времени при включённом критерии качества; (4) релизный zip не содержит `nvjpeg64_13.dll`; (5) межмодульный контракт коллектор↔DLL нарушен в трёх местах (CRC, алгоритм превью, thumbSize).

**Статус исправлений 2026-08-18:** закрыты C1, C2, C4, C8, C9, C10, C13, N2–N7, N11, N18, N19, S1–S5, S13, S14, B1, B3, B6 (маркеры ✅ в тексте). Остаются открытыми: N1 (banded-ядро, требует верификации GPU), C3 (атомарная запись), C5/C6 (контракт CRC/превью), N8–N10, N12, B2 и P3-чистки — см. §7.

---

## §1. Нативное ядро (`src/AntiDupl`) — 5×P1, 7×P2, 9×P3

### N1 [P1] GPU AllVsAll молча обрезает результаты на 5 000 000 кандидатов
`adGPU.cu:131-146`, `adGPU.cu:713-739` (SSIM-двойник: `adGPU.cu:947`)

```cpp
size_t idx = atomicAdd(matchCount, (size_t)1);
if (idx < maxMatches) { ... results[idx] ... }   // сверх лимита — молча выбрасывается
...
size_t matchesToRead = (h_matchCount < maxMatchesPerBatch) ? h_matchCount : maxMatchesPerBatch;
```

Лимит `BATCH_MATCHES = 5'000'000` (`adEngine.cpp:412`) считается **до** метаданных-фильтров `MatchCallback` (type/size/folder/searchPath/ratio), поэтому на большом корпусе с рыхлым порогом кандидат-пар больше 5M — лишние молча теряются, поиск отчитывается успехом. «Стриминговый» цикл вычитки (`adGPU.cu:724-739`) структурно мёртв: `matchesToRead ≤ maxMatchesPerBatch` всегда, цикл делает ровно один проход. Это и есть суть известного BUG-08 (поле `ctx.bufferFullCount` мёртвое — `adEngine.cpp:244,409`: инициализируется нулём, никем не заполняется, никем не читается).

**Фикс (последовательный проход полосами строк, без изменения ядра семантики):** ядру добавить `iBegin/iEnd` и цикл `for (size_t i = iBegin + blockIdx.x % (iEnd-iBegin); i < iEnd; i += gridDim.x)`, хосту — перезапуск по полосам:

```cpp
const size_t BAND = 512;                        // строк за проход; BAND*count << 5M пар
std::vector<Match> h_batch(maxMatchesPerBatch);
for (size_t i0 = 0; i0 < count; i0 += BAND) {
    size_t i1 = std::min(i0 + BAND, count);
    size_t zero = 0;
    cudaMemcpy(d_matchCount, &zero, sizeof(size_t), cudaMemcpyHostToDevice);
    AllVsAllKernel<<<blocks, threads, thumbSize>>>(..., i0, i1, ..., d_results, d_matchCount);
    if (cudaDeviceSynchronize() != cudaSuccess) { /* free + return false */ }
    size_t n = 0;
    cudaMemcpy(&n, d_matchCount, sizeof(size_t), cudaMemcpyDeviceToHost);
    size_t toRead = std::min(n, maxMatchesPerBatch);
    if (n > toRead) AD_DEBUG_FMT("GPU: band overflow, %zu dropped\n", n - toRead);
    cudaMemcpy(h_batch.data(), d_results, toRead * sizeof(Match), cudaMemcpyDeviceToHost);
    callback(h_batch.data(), toRead, callbackContext);
}
```

Полоса убирает саму возможность обрезки; лог делает остаточный дроп видимым.

### N2 [P1] ✅ Исправлено 2026-08-18 — GPU-путь игнорирует `compare.checkOnEquality`
`adEngine.cpp:625-629` (условие `useGpu`) против `adThreadManagement.cpp:324` (`CanCompare`)

CPU-путь сравнивает только при `checkOnEquality == TRUE`; гейт GPU это условие опускает. При `checkOnEquality == FALSE` + включённой опции дефекта (тогда превью всё равно заполняются) GPU прогоняет полный AllVsAll и **впрыскивает пары дубликатов** в скан, который по настройкам CPU должен был дать только дефекты. Перепроверено вручную: `checkOnEquality` в `useGpu`-гейте отсутствует.

```cpp
bool useGpu = (m_pGpuManager && m_pGpuManager->IsAvailable() &&
               m_pOptions->compare.checkOnEquality == TRUE &&   // паритет с CanCompare
               (m_pOptions->compare.algorithmComparing == AD_COMPARING_SQUARED_SUM ||
                m_pOptions->compare.algorithmComparing == AD_COMPARING_SSIM) &&
               m_pOptions->advanced.ignoreFrameWidth == 0 &&
               m_pOptions->compare.transformedImage == FALSE);
```

### N3 [P1] ✅ Исправлено 2026-08-18 — `CompareWithSetGPU` сравнивает с неинициализированной VRAM для картинок из БД
`adThreadManagement.cpp:376-396` (кэш-ветка `TCollectManager::Add`) + `adImageComparer.cpp:125-134`; `TEngine::UpdateGpuDatabase` (`adEngine.cpp:174`) — **ноль вызывавших** (проверено grep'ом)

Сценарий: `transformedImage == TRUE` + SQSUM + GPU → `useGpu == false` → работает CPU `CompareManager`, который диспетчеризуется в `CompareWithSetGPU`, читая глобальный VRAM-буфер по `globalIdx`. В глобальный буфер загружаются только изображения, прошедшие `TDataCollector::FillPixelData` (`adDataCollector.cpp:155-178`). Изображения, взятые из БД по кэш-ветке (`FillOther` + `CompareManager->Add` без загрузки), никогда не загружены; `GpuCreateBuffer` не делает `cudaMemset` — в слотах мусор/чужие пиксели → тихо неверные разности. Функция, которая должна была заполнять буфер (`UpdateGpuDatabase`), мёртвая.

**Фикс — загрузка в кэш-ветке (минимальный):**

```cpp
else {
    ...
    pImageData->FillOther(m_pOptions);
    if (!m_pEngine->SkipComparisonDuringCollection() &&
        pImageData->data && pImageData->data->filled)
    {
        TGpuManager* pGpu = m_pEngine->GpuManager();
        const size_t thumbSize = Simd::Square(m_pOptions->advanced.reducedImageSize);
        if (pGpu->IsAvailable() && pImageData->data->side == m_pOptions->advanced.reducedImageSize &&
            pGpu->EnsureCapacity(pImageData->globalIdx + 1, thumbSize))
            pGpu->UploadThumbnail(pImageData->globalIdx, pImageData->data->main);
    }
    ...
}
```

(Альтернатива: вызвать `UpdateGpuDatabase()` после загрузки БД в `Search()` и снять с неё флаг мёртвого кода. Выбрать один вариант.)

### N4 [P1] ✅ Исправлено 2026-08-18 — `.adr` крашится на выходе индекса за границы
`adResultStorage.cpp:341-345, 359-363` + `adImageInfoStorage.cpp:50-53`, `adFileStream.cpp:165-166`

`TImageInfoStorage::Get` возвращает `NULL` для индекса ≥ размера; индекс читается из файла без проверки. Обрезанный/битый `.adr` → `NULL->Actual()` → краш хоста вместо ошибки.

```cpp
result.first  = m_pImageInfoStorage->Get((size_t)result.first);
result.second = m_pImageInfoStorage->Get((size_t)result.second);
if(result.first == NULL || (result.type == AD_RESULT_DUPL_IMAGE_PAIR && result.second == NULL))
    throw TException(AD_ERROR_INVALID_FILE_FORMAT);
```

(в обеих ветках `TResultStorage::Load`.)

### N5 [P1] ✅ Исправлено 2026-08-18 — Префикс-матчинг путей без границы разделителя: `C:\Foo` ловит `C:\Foo2`
`adEngine.cpp:431-444` (пулы в движке) и `adResultStorage.cpp:493-500` (`FilterByPool`); та же ошибка в C# — см. S14; и в реестре БД — см. N11

Класс ровно тот, от которого защищает `RemapPath` (`adImageDataStorage.cpp:59-63`), но назначение пулов не защищено: БД, зарегистрированная на `C:\Foo`, захватывает изображения `C:\Foo2\...` → режимы пулов 1–4 включают/исключают не те пары. Две копии логики ещё и разъехались. **Фикс — один общий хелпер** (`adPath.h`), используемый всеми тремя местами:

```cpp
inline bool PathStartsWith(const std::wstring& path, const std::wstring& prefix) {
    if (path.size() < prefix.size() ||
        ::CompareStringOrdinal(path.c_str(), (int)prefix.size(), prefix.c_str(),
                               (int)prefix.size(), TRUE) != CSTR_EQUAL)
        return false;
    return path.size() == prefix.size() ||
           path[prefix.size()] == L'\\' || path[prefix.size()] == L'/';
}
```

### N6 [P2] ✅ Исправлено 2026-08-18 — `LoadCollectorData`: состояние записи не сбрасывается + все `fread` без проверки
`adImageDataStorage.cpp:602-667`

Два дефекта: (a) `imageData` переиспользуется между итерациями, но `data->filled`/`average`/`varianceSquare` только пишутся в `true`/значения — запись с `filled == 0` после записи с `filled == 1` вставляется с превью предыдущей записи, помеченным заполненным. Сейчас латентно (коллектор всегда пишет `filled=1`, проверено), но формат это допускает. (b) Все метаданные-`fread` (строки 613–623, 646, 655, 659–660) игнорируют результат: гнилой хвост → нули, запись тихо пропускается, функция возвращает `true` → `Load` возвращает `AD_OK`.

```cpp
for (uint64_t i = 0; i < count; i++) {
    imageData.data->filled = false;
    imageData.data->average = 0; imageData.data->varianceSquare = 0;
    uint64_t fileSize, fileTime, crc32c; uint32_t hash, width, height;
    float blockiness, blurring; uint8_t type, defect, filled;
    if (fread(&fileSize,8,1,f)!=1 || fread(&fileTime,8,1,f)!=1 || fread(&hash,4,1,f)!=1 ||
        fread(&type,1,1,f)!=1   || fread(&width,4,1,f)!=1  || fread(&height,4,1,f)!=1  ||
        fread(&blockiness,4,1,f)!=1 || fread(&blurring,4,1,f)!=1 || fread(&defect,1,1,f)!=1 ||
        fread(&crc32c,8,1,f)!=1 || fread(&filled,1,1,f)!=1)
    { fclose(f); return false; }
    ...
```

### N7 [P2] ✅ Исправлено 2026-08-18 — `LoadCollectorData` принимает thumbSize из файла, не равный `reducedImageSize`
`adImageDataStorage.cpp:602, 643-657`; параметр `thumbSizeFromHeader` (`:505`) читается в `Load()` и не используется; `allLoad` тоже игнорируется

DLL-native загрузчик строг (`adFileStream.cpp:141-143` — исключение при несовпадении стороны), collector-native — нет: БД с `--size 64` при опции 32 грузится с `filled=true`, GPU-pack их отфильтрует (известный фикс), но **CPU**-компаратор (`IsDuplPair`, `m_mainSize` = опция², `adImageComparer.cpp:56/275`) читает первые `m_mainSize` байт из большего буфера — сравнивает верхний левый квадрант → мусорные разности. Плюс `fileThumbSize` без верхней границы: битый заголовок кормит `new TPixelData(side)` без try/catch → `bad_alloc` вылетает из экспорта DLL.

```cpp
if (fread(&fileThumbSize, 4, 1, f) != 1 ||
    fileThumbSize == 0 || fileThumbSize > 1024 ||
    fileThumbSize != m_pOptions->advanced.reducedImageSize)
{ fclose(f); return false; }
```

(Паритет с C-фиксом C6: оба конца должны отвергать несовпадение размера.)

### N8 [P2] Постоянный leak пиннед-буферов на каждый поток каждого поиска
`adNvJpeg.cpp:209-223`

`thread_local` сырой указатель на `cudaHostAlloc`-память (до `w*3*h`, десятки МБ на современных фото) никогда не освобождается; collect-потоки создаются/умирают на каждый `Search()` → накопительный leak page-locked памяти.

```cpp
static thread_local struct PinnedBuffer {
    unsigned char* p = nullptr; size_t size = 0;
    ~PinnedBuffer() { if (p) cudaFreeHost(p); }
} s_pin;
```

### N9 [P2] `MoveCurrentGroup`/`RenameCurrentGroupAs` — leak и порча undo-состояния на раннем выходе
`adUndoRedoEngine.cpp:622-628` и `686-692`

В отличие от остальных путей отказа в файле, ранний `return false` при `pImageGroup == NULL` не удаляет свежесозданный change и не восстанавливает `pOldChange` → `m_pCurrent->change` указывает на пустой объект, последующие действия копятся в чужой change.

```cpp
if(pImageGroup == NULL) {
    delete m_pCurrent->change;
    m_pCurrent->change = pOldChange;
    return false;
}
```

### N10 [P2] Undo рапортует успех, но файлы из корзины не восстанавливаются
`adRecycleBin.cpp:57-62` (стаб `Restore` → `false`) + `adUndoRedoEngine.cpp:345-351` (результат игнорируется)

`Undo` возвращает `true` при неудачном restore — в списке результатов воскрешаются пары, чьи файлы удалены (пропадут только по Refresh). Минимум — распространить отказ:

```cpp
for(...deletedImages...) {
    if (!m_pRecycleBin->Restore(*it)) { m_pStatus->Reset(); return false; }
    ...
}
```

Реальный restore (IFileOperation из корзины) — см. §7 (не автоматизируется безопасно).

### N11 [P2] ✅ Исправлено 2026-08-18 — `TDatabaseRegistry::UpdateCount`: префикс-коллизия в обе стороны + неатомарная незакавыченная запись XML
`adDatabaseRegistry.cpp:185-194`, `97-115`

(a) `searchPath.find(dbPath) == 0 || dbPath.find(searchPath) == 0` без границы разделителя — `C:\Foo` обновляет счётчик БД `C:\Foo2`, первый матч выигрывает. (b) `Save()` пишет `ad_database.xml` усечением на месте, без экранирования `Name` (`&`, `<`, `"` калечат файл) и без temp+rename — краш или параллельно работающий коллектор (он переписывает тот же файл, `main.cpp:1051`) теряет записи. Фикс: хелпер N5 + запись через `ad_database.xml.tmp` + `MoveFileEx(REPLACE_EXISTING)` + экранирование атрибутов.

### N12 [P2] `adDatabaseRegistryLoadW`: NULL-разыменование / переполнение буфера вызывающего
`AntiDupl.cpp:641-651`

`*pCount = size` затирает capacity до использования как границы; `pPaths != NULL && pCount == NULL` → краш; `wcscpy_s(pPaths[i], MAX_PATH_EX, ...)` прерывает процесс, если буфер вызывающего меньше. Экспорт не объявлен в `AntiDupl.h` и не используется C# (он парсит XML сам) — мёртвая поверхность без контракта. Фикс: honor capacity + null-check, либо удалить экспорт:

```cpp
adSize capacity = pCount ? *pCount : 0;
if (pCount) *pCount = databases.size();
for (size_t i = 0; i < databases.size() && i < capacity; i++) { ... }
```

### N3-мелочи [P3] — ядро

| # | Место | Проблема | Фикс |
|---|------|----------|------|
| N13 | `adGPU.cu:477-517` | `GpuCompareSquaredSum` возвращает `0.0` (= «идентичны») на всех путях отказа | возвращать `1e10` как в null-пути |
| N14 | `adGPU.cu:657-661, 922-927` | `cudaMemcpy` poolMask не проверен; неудачный `cudaMalloc` тихо отключает фильтр пулов в ядре | проверять оба, фейлить вызов |
| N15 | `adEngine.cpp:253-294` | `MatchCallback` нет гейта `type > AD_IMAGE_NONE` (есть в `CanCompare`); прогресс = «найденные пары», а не «сравнённые» → бар стоит на 0% и прыгает | добавить gate; кормить прогресс числом обработанных кандидатов |
| N16 | `adImageData.h:44` + `main.cpp:324` + `adDataCollector.cpp:229` | crc32c: u32 против u64 на диске; коллектор считает CRC **превью**, DLL — **файла** → штраф `ADDITIONAL_DIFFERENCE_FOR_DIFFERENT_CRC32` ведёт себя по-разному для БД-кэш и свежих картинок | см. C5 — унифицировать на CRC файла |
| N17 | — | Мёртвый код: `UpdateGpuDatabase` (до N3), `GpuCompareOneVsMany`/`CompareOneVsMany`, фейковый стриминговый цикл (N1), экспорты N12; дубль-парсинг заголовков `LoadCollectorNative` vs `LoadCollectorData` (`:521-544` vs `:578-599`) | удалить; извлечь `ReadCollectorString(FILE*)` |
| N18 | `adThreadManagement.h:68` | ✅ 2026-08-18: `TThreadQueue::Size()` читает `m_pQueue->size()` без CS — формальная гонка | обернуть в `TCriticalSection::TLocker` |
| N19 | `adEngine.h:64` | ✅ 2026-08-18: `m_skipComparisonDuringCollection` — обычный `bool` через потоки | `std::atomic<bool>` |
| N20 | `adFileStream.cpp:145-149` | legacy (v≤3) DLL-БД: average/variance не читаются и не пересчитываются → SSIM деградирует тихо | пересчитывать при `filled && average==0 && varianceSquare==0` |
| N21 | `adSearcher.cpp:217-229` | эвристика `globalIdx >= prevCount` для «новых» записей связывает счётчик вставок с размером вектора — хрупко | помечать вставленные записи явно |

### Проверено чисто (ядро)
`RemapPath` (граница, регистр, слэши); конвейер DLL-native save/load (throw-on-short-IO, backup-порядок, `LoadSizeChecked`); `UpdateIndex` (деление защищено валидацией опции [16..128]); сериализация `TGpuManager` одним рекурсивным мьютексом; паритет CPU↔GPU порогов MS/SSIM (формулы, направление, штраф CRC — совпадают); fan-out `TCompareManager::Add` (пара сравнивается ровно один раз); `TSearcher::SearchImages` (рекурсия, фильтры, `FindClose`); `adOptions::Validate`; владение `TImageData` (`m_owner`, memcpy fast|main); стандартные экспорты `AntiDupl.cpp` (CHECK_HANDLE/LOCK-протокол).

---

## §2. NvJpegCollector (`src/NvJpegCollector/main.cpp`) — 2×P1, 8×P2, 17×P3

### C1 [P1] ✅ Исправлено 2026-08-18 — `--update`: для ИЗМЕНЁННЫХ файлов пишутся ОБЕ записи — старая и новая
`main.cpp:699-704` (плейсхолдер) vs `main.cpp:746-747` (вставка декодированных)

```cpp
// MODIFIED: need to re-decode
images.push_back(existingImg); // placeholder, will be overwritten after decode  ← НИКОГДА не перезаписывается
toDecode.push_back(it->second);
...
images.insert(images.end(), updJpegImages.begin(), updJpegImages.end());   // только append
```

Каждый изменённый файл попадает в `0000.adi` дважды: устаревшая запись и свежая. DLL `Find()` берёт первую попавшуюся (при `hash=0` у всех — недетерминированно), т.е. у изменённого файла может навсегда остаться старое превью/размер. Счётчик в index/реестре завышен. Если декод изменённого файла падает — остаётся только устаревшая запись. Перепроверено вручную: перезаписи плейсхолдера нет.

**Фикс:** не пушить плейсхолдер — декодированная запись его заменяет:

```cpp
} else { // MODIFIED
    toDecode.push_back(it->second); // не пушить existingImg: декодированная запись заменит её
    modified++;
}
```

(Если декод упал — записи нет вообще; файл и так битый и попадает в failed.log.)

### C2 [P1] ✅ Исправлено 2026-08-18 — `LoadExistingDatabase`: use-after-close / двойной fclose на битой БД (UB, вероятен краш)
`main.cpp:254-260`, `:298`

```cpp
auto readStr = [&](FILE* fp) -> std::wstring {
    uint64_t len = 0; fread(&len, 8, 1, fp);
    if (len > 10000) { fclose(fp); return L""; }  // закрыл общий FILE*, цикл продолжает читать
```

На обрезанном `0000.adi` цикл записей (274–297) читает из закрытого хэндла, затем `fclose(f)` закрывает второй раз. Плюс все `fread` без проверки — усечение посреди записи даёт мусорные `size/time/hash`.

```cpp
bool bad = false;
auto readStr = [&](FILE* fp) -> std::wstring {
    uint64_t len = 0;
    if (fread(&len, 8, 1, fp) != 1 || len > 10000) { bad = true; return L""; }
    std::wstring s((size_t)len, L'\0');
    if (len && fread(&s[0], sizeof(wchar_t), (size_t)len, fp) != len) { bad = true; return L""; }
    return s;
};
// после каждого поля: if (bad || ferror(f)) { fclose(f); return false; }
```

### C3 [P2] БД пишется неатомарно: усечение на месте, index раньше data, без backup
`main.cpp:787, 800` (update) и `940, 953` (full): `_wfopen_s(..., L"wb")`

`"wb"` мгновенно убивает прежнюю БД; краш посреди записи (в update-режиме прежняя БД — единственная копия всех UNCHANGED превью) = потеря всей базы. DLL-наттив путь хранит `backup.adi`; коллектор — нет. `fwrite` не проверяются (диск full → тихо обрезанная БД).

**Фикс:** писать `0000.adi.tmp` → `fflush` → `MoveFileExW(tmp, ..., MOVEFILE_REPLACE_EXISTING)`; data-файл первым, index последним (коммит-точка); проверять `fwrite(...) == 1`.

### C4 [P2] ✅ Исправлено 2026-08-18 — Update при полном удалении файлов не пишет ничего — старая БД выживает
`main.cpp:782`: `if (!images.empty()) {...}` — если все файлы источника удалены, записи и реестр не обновляются, DLL продолжает грузить призраки. Фикс: писать count=0 (или удалить папку БД + запись реестра).

### C5 [P2] Контракт: crc32c коллектора — CRC превью; DLL — CRC файла
`main.cpp:324` против `adDataCollector.cpp:222-235`; потребитель — `adEngine.cpp:456/480` (`ADDITIONAL_DIFFERENCE_FOR_DIFFERENT_CRC32` как сигнал байт-идентичности)

В смешанных БД: байт-идентичные файлы получают штраф «разный CRC», а перекодированные файлы с идентичными превью — бонус «одинаковый CRC». Сырые байты файла уже есть в `item.raw` — CRC файла бесплатен:

```cpp
info.crc32c = SimdCrc32c(item.raw.data(), (size_t)item.raw.size());  // вместо thumbnail
```

(Для WIC-пути — прочитать файл; см. §7 про совместимость со старыми БД.)

### C6 [P2] Контракт: алгоритм превью отличается от DLL (один Resize против 256+пирамида 2x2)
`main.cpp:319-322` против `adDataCollector.cpp:51-52, 133-137`

Разные пиксели → разные `average`/`varianceSquare` (входы SSIM) и превью; одна и та же картинка, заполненная коллектором и DLL, сравнивается по-разному. Фикс в `ProcessGray`: повторить пирамиду — `Simd::Resize(gray -> 256)` (INITIAL_REDUCED_IMAGE_SIZE, `adConfig.h:101`), затем `Simd::ReduceGray2x2` до `side`.

### C7 [P2] DLL доверяет thumbSize файла и игнорирует свой `reducedImageSize` — следствие N7
(полное описание в N7; здесь — сторона контракта: оба конца должны отвергать несовпадение, включая `--size` коллектора — см. C13.)

### C8 [P2] ✅ Исправлено 2026-08-18 — Непроверенные CUDA-результаты: буфер предыдущей картинки записывается как данные текущей
`main.cpp:528, 533`

При device-lost/async-ошибке `cudaEventSynchronize` вернёт ошибку, но `slot.gray` всё ещё держит пиксели **предыдущего** изображения — `ProcessGray` захеширует их и запишет под путём текущего → у двух разных картинок идентичные превью/CRC → гарантированный ложный дубликат в БД.

```cpp
if (cudaEventSynchronize(slot.done) != cudaSuccess) { logFail(fp, 6, (long)cudaGetLastError()); continue; }
```

(+ проверять код постановки `cudaMemcpy2DAsync`.)

### C9 [P2] ✅ Исправлено 2026-08-18 — `fs::file_size()` бросает на исчезнувшем файле → весь прогон падает
`main.cpp:693` (классификация update) и `ProcessGray` `:306` из WIC-циклов `:762`, `:918`

Незакрытые `filesystem_error` долетают до обработчика `wmain` → «FATAL ERROR», exit 2, вся декодированная работа теряется. В `ProcessJpegList` внутри try/catch — только там безопасно.

```cpp
std::error_code ec; uint64_t sz = fs::file_size(path, ec); if (ec) { /* log + skip */ }
```

### C10 [P2] ✅ Исправлено 2026-08-18 — `thumbSizeVal` из файла без валидации → `resize(0x7FFF...)` → необработанный `length_error`
`main.cpp:289-291` — в отличие от DLL-ридера (`:647-651`), собственный загрузчик доверяет полю длины: `img.thumbnail.resize((size_t)thumbSizeVal);`. Фикс: `if (thumbSizeVal != (uint64_t)ts*ts) { fclose(f); return false; }`.

### C11-C17 [P3] — коллектор

| # | Место | Проблема | Фикс |
|---|------|----------|------|
| C11 | `main.cpp:55-74, 380-386, 146/161` | Мёртвое: `GenerateAdiFileName`, `ResolveWorkers` (help обещает «cores−1», фактически `DetectPhysicalCores()`), `--batch` парсится и не используется (batch захардкожен 1) | удалить/синхронизировать help |
| C12 | `main.cpp:193-199` | Глобальные nvJPEG state+stream инициализируются и никогда не используются (каждый декодер создаёт свои) | удалить |
| C13 | `main.cpp:160` | ✅ 2026-08-18 (clamp 16..128): `--size` без валидации: 0 → десинк ридера DLL; 50000 → UB переполнения `int` | clamp 8..256, степень двойки |
| C14 | `main.cpp:476-478` | `threads.emplace_back` может бросить после старта reader'а → деструктор joinable thread → terminate | try/catch + join что есть |
| C15 | `main.cpp:134-142` | `GetImageType` — только точный регистр: `.Jpg`/`.Tiff` → 0 → файл тихо не собирается (даже не failed) | `_wcsicmp` |
| C16 | `main.cpp:777-829 vs 931-979` | Дубль save-кода (update vs full) и реестр-XML блока | извлечь `SaveDatabase(...)` |
| C17 | `main.cpp:833-870 vs 984-1055` | Реестр XML: наивный `find(Path="")`+`Count`, без экранирования `&<>"`, full-path rebuild дропает всё не-self-closing | XElement + escaping (синхронно с N11) |

Прочее [P3]: WIC `pConverter` leak при неудачном `Initialize` (`:108-112`); failed.log теряет не-ASCII пути (`std::wofstream`, `:581-583` — писать UTF-8 через `WideCharToMultiByte`); трижды повторяющаяся сортировка `images` в update (`:723,771,784`); регистрозависимое сравнение путей в update (`D:\` vs `d:\` → всё NEW+DELETED, `:674-710`); очередь ограничена штукой, не байтами (`kQueueCap=nThreads*2` × 250MP ≈ 4 ГБ, push вне try → terminate); «(100%)» печатается даже при неудачах (`:568`); `--help` через `PauseAndExit` MessageBox (`:606`); crc32c=0 для полностью чёрных превью трактуется DLL как «не собрано» (`adThreadManagement.cpp:361`); бессмысленный копи `string what(...)` в `wmain` catch (`:228`).

### Проверено чисто (коллектор)
Шатдаун очереди (нет дедлока, `DoneGuard` на исключениях); расшаренное состояние (`outMtx`, атомики, `nextIdx` только у reader'а); паттерн per-thread nvJPEG state+stream; время жизни `item.raw` против `cudaEventSynchronize`; pitch-математика (`((w*3+31)/32)*32`, alloc `imgPitch*h`, `cudaMemcpy2DAsync` согласованы); wire-формат писателя ↔ DLL-ридера поле-в-поле (включая average/variance после пропущенного превью); семантика update (удалённые записи выкидываются, UNCHANGED переиспользуют превью, несовпадение thumbSize → graceful full rebuild); COM-баланс; широкие пути (`_wfopen_s`).

---

## §3. C# слой (Core + WinForms + WPF) — 5×P1, 13×P2, 16×P3

### S1 [P1] ✅ Исправлено 2026-08-18 — AutoSelector: критерии Time и Pool молча игнорируются при включённом критерии качества
`AutoSelector.cs:243-299`

Диалог (`AutoSelectDialog.cs:49-101`) позволяет одновременно «выбрать старый файл» И «выбрать меньший файл», но `DetermineSide` при любом активном критерии качества возвращается из каждой ветки каскада — Time/Pool недостижимы. Решение ведёт удаление → **удаляется не тот файл**.

```csharp
if (criteria.TimeSide != AutoSelectSide.DontCare) {
    AutoSelectSide side = OlderSide(r);
    if (side != AutoSelectSide.DontCare)
        return (criteria.TimeSide == AutoSelectSide.First) ? side : Opposite(side);
}
if (hasQualityCriterion) { /* существующий каскад */ }
```

(Альтернатива — блокировать конфликтующие группы в диалоге; выбрать одно.)

### S2 [P1] ✅ Исправлено 2026-08-18 — Массовое удаление без подтверждения, на UI-потоке, без длинных путей
`MainMenu.cs:391-400`, `ResultsListViewContextMenu.cs:180-188`

Классический путь (`ResultsListView.MakeAction`) предупреждает о безвозвратном удалении длинных путей и работает через фон `ProgressForm`; новый батч-путь вызывает `AutoSelector.ExecuteBatch(m_core, true)` синхронно: ноль подтверждений (безвозвратно при выключенной корзине или пути >260 — проверка `HasLongPaths` не выполняется), фриз UI на весь батч, без отмены. Фикс: `MessageBox.Show(...YesNo)` + обёртка в фоновое выполнение как в классическом пути.

### S3 [P1] ✅ Исправлено 2026-08-18 — `CoreOptions.Set`: NRE в fallback-ветке, проглоченный логом → поиск идёт не по тем папкам
`CoreOptions.cs:143-148`

```csharp
CorePathWithSubFolder[] tmpSearch = new CorePathWithSubFolder[1];
if (... Directory.Exists(searchPath[0].path))
    tmpSearch[0] = searchPath[0];
else
    tmpSearch[0].path = Application.StartupPath;   // tmpSearch[0] == null → NRE
```

NRE ловится внешним catch (`:162`) и пишется только в `path_debug.log`; поиск продолжает использовать прежние пути нативной стороны без видимой ошибки.

```csharp
tmpSearch[0] = new CorePathWithSubFolder { path = Application.StartupPath };
```

### S4 [P1] ✅ Исправлено 2026-08-18 — Запуск коллектора из DatabaseManagerForm: классический дедлок пайпов
`DatabaseManagerForm.cs:425-439` (Update) и `:681-694` (Update All)

Последовательный `ReadToEnd` на stdout, потом stderr: если коллектор пишет в stderr больше ёмкости пайпа, пока stdout открыт — взаимоблокировка. `UpdateAllDatabases` вообще не дренит stderr. Плюс `WaitForExit()` блокирует UI-поток.

```csharp
var stderrTask = proc.StandardError.ReadToEndAsync();
string stdout = proc.StandardOutput.ReadToEnd();
string stderr = stderrTask.Result;
proc.WaitForExit();
```

### S5 [P1] ✅ Исправлено 2026-08-18 — Рабочие потоки без обработки исключений оставляют модальные диалоги навсегда
`SearchExecuterForm.cs:148-201`, `ProgressForm.cs:222-313`, `StartFinishForm.cs:108-143`, WPF `SearchDllCommand.cs:156-174`

`CoreThreadTask` ставит `State.Finish` только на успехе; любое исключение убивает поток молча — таймер никогда не видит Finish, неклозабельный диалог висит, Stop/Cancel заблокированы. Конкретные триггеры: `LogPerformance` (`:482-497`) разыменовывает `statistic.searchedImageSize`, когда `CoreLib.GetStatistic()` вернул null (`CoreLib.cs:165-190`); null-делегаты DynamicModule (S7). Фикс: try/catch вокруг тела `CoreThreadTask` → состояние `Error` → закрытие с сообщением; null-check `statistic`.

### S6 [P2] `CoreLib` передаёт нативному коду адреса НЕзакреплённых managed-массивов
`CoreLib.cs:292-300, 344-349, 354-361, 395-403, 522-525`

`Marshal.UnsafeAddrOfPinnedArrayElement` валиден только для pinned-массивов; GC на другом потоке во время нативного вызова может перенести массив → повреждение кучи. Спасает то, что большие буфера попадают на LOH; `UIntPtr[1]` и мелкие буфера действительно подвижны.

```csharp
fixed (UIntPtr* pStart = pStartFrom, pSize = pSizeArr)
fixed (byte* pBuf = buffer) {
    if (m_dll.adResultGetW(m_handle, (IntPtr)pStart, (IntPtr)pBuf, (IntPtr)pSize) == Error.Ok) { ... }
}
```

### S7 [P2] DynamicModule глотает отсутствующие экспорты → null-делегаты → дальние NRE
`DynamicModule.cs:57-70`: `GetProcAddress == 0` → throw → catch → поле null; первый вызов даёт NRE без намёка на причину. Фикс: собрать недостающие имена и `throw new MissingMethodException("AntiDupl.dll", field.Name)`.

### S8 [P2] `jpegPeaks` в interop-структуре читает нативный tail-padding
`CoreDll.cs:411` vs `AntiDupl.h:552-565` — нативная `adImageInfoW` не имеет поля; вычисление показывает, что C# `uint` попадает ровно в 4 байта tail-padding (sizeof совпадает, stride верен — не дрейф размера), но значение — мусор паддинга. WinForms всегда видит 0 (`CoreImageInfo` не копирует), WPF использует `JpegPeaks` как критерий (`ImageInfoClass.cs:98`, `DuplResultMultiValueConverter.cs:52-56`) — мёртвая/вводящая в заблуждение фича. Фикс: удалить поле из `adImageInfoW` и из WPF-цепочки (либо реализовать нативно и маршалить).

### S9-S18 [P2] — прочее

| # | Место | Проблема | Фикс |
|---|------|----------|------|
| S9 | `ThumbnailPanel.cs:67-79`, `ThumbnailGroupTable.cs:364-383` | `BeginInvoke` без guard `IsDisposed` → `ObjectDisposedException`; `UpdateThumbnailsStop` — `Join()` без таймаута → вечный фриз на мёртвом сетевом диске | `if (IsDisposed \|\| Disposing) return;` / `Join(TimeSpan.FromSeconds(5))` |
| S10 | `ResultsPreviewDuplPair.cs:352-371` | событие подсветки стреляет дважды — второй раз со ВСЕМИ прямоугольниками, `MaxFragmentsForHighlight` затирается; `_highlightStop` не volatile; проверка `ThreadState == Running` пропускает `Unstarted/WaitSleepJoin` → два потока подсветки | `else` вокруг второго вызова; volatile; generation-токен |
| S11 | `DatabaseManagerForm.cs:961-965` | `int.Parse(GetAttr(...))` без try/catch в `LoadRegistry` — битый атрибут валит конструктор формы И рабочий поток поиска (`GetEnabledDatabasePaths` зовётся из `SearchExecuterForm.cs:153`) | `int.TryParse` + skip + log |
| S12 | `DatabaseManagerForm.cs:1026`, `SearchExecuterForm.cs:157`, `CoreOptions.cs:124`, `CoreLib.cs:605`, `AntiDupl.cpp:389` | Дебажные логи в папке exe в проде: `cs_debug.log` (необрезаемый), `trace.log`, `path_debug.log` — под Program Files тихо падают, на writable — растут вечно | удалить или гейт `#DEBUG`/настройкой |
| S13 | `AutoSelector.cs:169-229` | ✅ 2026-08-18: Учёт батча: неудачи delete не попадают в `FailedPaths`; исчезнувший файл считается failed, но строка в результатах не снимается (`MarkRemoved*` не зовётся) — вечный stale-ряд; `s_sideCache.Clear()` стирает разметку и при частичной неудаче | дополнять FailedPaths; MarkRemoved для отсутствующих; чистить только успешные ключи |
| S14 | `AutoSelector.cs:368-376` | ✅ 2026-08-18: Префикс-матчинг пула без разделителя (та же болезнь, что N5) — `C:\Photos` ловит `C:\Photos2` → автовыбор удаляет не с той стороны | `StartsWith(..., OrdinalIgnoreCase) && (len==dbPath.Length \|\| imgPath[dbPath.Length]=='\\')` |
| S15 | `MainMenu.cs:427-438` + 2 копии | `IsSafeMoveTarget` блокирует только System32; `C:\Windows`, Program Files, корни дисков проходят; сообщение врёт про тест записи; три копии кода | блокировать `%SystemRoot%`, `%ProgramFiles%`, корни; probe записью temp-файла; одна общая копия |
| S16 | `CoreOptions.cs:104-107` | `Get(onePath)` индексирует `core.searchPath[0]` без проверки длины | length guard |
| S17 | `MainForm.cs:111-112` | После таймаута `WaitForWorker(10000)` `m_core.Dispose()` зовёт `adRelease`, пока finish-воркер может быть внутри `adSaveW` на освобождённом движке → нативный краш на выходе | при таймауте утечь core (`GC.SuppressFinalize`, не Dispose) вместо release |
| S18 | `ResultsListView.cs:468-474` | `row.selected = selection[i]` без проверки `selection.Length` (нативный `GetSelection` может вернуть меньше) | `&& i < selection.Length` |

### S19-S34 [P3] — C# гигиена (выжимка)

Мёртвая interop-структура `adPathWithSubFolderW` с неверным layout (65540 vs 65538 байт — безопасно только потому, что `CoreLib.SetPath` пакует вручную; удалить); отсутствие `AD_IMAGE_UNDEFINE=-1`/`AD_DEFECT_UNDEFINE=-1` в enum'ах; `Mutex` как внутрипроцессный лок + недиспоз замещённых битмапов в `ThumbnailStorage`; `LockBits` без try/finally и `GC.Collect()` как средство от OOM в `BitmapWorker`; недиспоз Timer/NotifyIcon и `-=` только на счастливом пути в `SearchExecuterForm`; Pen/StringFormat на каждый paint в `DataGridViewDoubleTextBoxCell`; статическое событие `Strings.OnCurrentChange` без отписок (10 подписчиков); хардкод-английский против инфраструктуры Strings RU/EN (MainMenu, DatabaseManagerForm, AutoSelectDialog); утроенная логика move/delete/IsSafeMoveTarget в MainMenu/ContextMenu/ToolStrip (копии разъехались); перевёрнутые имена пресетов («Select Worst» → `KeepBest` — поведение верное, имена путают); `LoadRegistry(string userPath)` игнорирует параметр; hand-rolled XML в `SaveDatabases` без экранирования; цепочки ремапа (`RemapFrom` перезаписывается — второй ремап до Update ломает оба значения); `AutoSelector.Apply` — NRE на разреженных страницах + burst аллокаций ~138 КБ/результат на UI-потоке.

**WPF (быстрый проход):** `RunProcess` без try/catch (окно прогресса не закрывается — тот же класс S5); `GetResults` индексирует по `resultSize`, прочитанному ДО `GetResult` → NRE на усохшей странице; `JpegPeaks` — мёртвые данные (S8).

### Проверено чисто (C#)
Дрейф enum'ов между `CoreDll.cs` и `AntiDupl.h` — **нет** (Error, LocalActionType 0–15 c MarkRemoved*=14/15, SortType, все сравниваемые — совпадают); размеры interop-структур поле-к-полю (x64, pack 8; MAX_PATH_EX=32768, MAX_EXIF_SIZE=260; bool → int везде); делегаты Cdecl+Unicode; фейковые указатели `IntPtr(1)` в GetResultSize/GetGroupSize безопасны; SHFILEOPSTRUCT корректен для x64; Options round-trip + Clone; pool-mode registry; порядок ремапа (`RemapFrom` до `Path`); защита от двойного старта поиска модальностью.

---

## §4. Сборка / CI / упаковка — 1×P1, 7×P2/P3

### B1 [P1] ✅ Исправлено 2026-08-18 — MakeBin.cmd пакует НЕ ту nvjpeg: релизный zip без `nvjpeg64_13.dll`
`cmd/MakeBin.cmd:44-46`

NvJpegCollector линкуется против CUDA 13.1 (`nvjpeg.lib` → `nvjpeg64_13.dll` + `cudart64_13.dll`; подтверждено содержимым `bin/Release`). MakeBin копирует `nvjpeg64_12.dll` (устаревшую) и опционально `cudart64_12.dll`, **не копируя** `nvjpeg64_13.dll`/`cudart64_13.dll` — `xcopy` молча проваливается (нет проверки errorlevel), CI собирает артефакт, и коллектор в дистрибутиве не запускается.

```bat
REM было: xcopy %RELEASE_DIR%\nvjpeg64_12.dll ...
xcopy %RELEASE_DIR%\nvjpeg64_13.dll %TMP_DIR%\* /y /i
if exist %RELEASE_DIR%\cudart64_13.dll xcopy %RELEASE_DIR%\cudart64_13.dll %TMP_DIR%\* /y /i
if exist %RELEASE_DIR%\cudart64_12.dll xcopy %RELEASE_DIR%\cudart64_12.dll %TMP_DIR%\* /y /i
```

(`cudart64_12.dll` нужна самой `AntiDupl.dll` — оставить опционально; проверить фактические импорты dumpbin'ом и зафиксировать список.) То же проверить в `MakePublish.cmd`.

### B2 [P2] CI ставит CUDA 12.8, локальная сборка — 13.1
`.github/workflows/AntiDupl_CI.yml:40-44` — collector на CI линкуется против 12.8-версии nvjpeg, локально против 13.1: одна и та же ветка производит бинарники против разных мажор-версий nvJPEG. Зафиксировать 13.1 в CI (`JimVer/cuda-toolkit-action` с `cuda: '13.1.x'`) или перевести локальную сборку на 12.8.

### B3 [P2] ✅ Исправлено 2026-08-18 — Deploy.cmd: шаг коллектора без `/p:VcpkgManifestInstall=false`
`cmd/Deploy.cmd:40` против `:32` — несогласованность с шагом AntiDupl: одиночный запуск может триггернуть полную vcpkg manifest-установку (долго, ловит simd-quirk). Добавить флаг.

### B4 [P3] Deploy.cmd: хардкод пути `v12.8` относительно CUDA 13
`cmd/Deploy.cmd:62` — `if not exist "%BIN_DIR%\cudart64_12.dll" copy ...v12.8\bin\...` — работает только на конкретной машине. Вынести `CUDA12_BIN` в переменную/параметр с понятным WARN при отсутствии.

### B5 [P3] CI без `timeout-minutes` и `concurrency`
Отсутствуют оба — зависший билд жрёт раннер 6 часов (дефолт), параллельные пуши не отменяются. Добавить `concurrency: { group: ci-${{ github.ref }}, cancel-in-progress: true }` и `timeout-minutes: 90`.

### B6 [P3] ✅ Исправлено 2026-08-18 — Сгенерированные `External.cs`/`adExternal.h` закоммичены
`git ls-files`: оба файла в репо, при том что pre-build перегенерирует их из `version.txt`. Дрейф при смене версии без билда + вечные диффы. Удалить из индекса и добавить в `.gitignore` (pre-build создаёт их до компиляции — сборке не мешает).

### B7 [P3] `nuget restore` на решении без NuGet-пакетов
`AntiDupl_CI.yml:46-48` — шаг безвреден, но лишний (нет packages.config/PackageReference вне SDK-дефолтов). Удалить или оставить осознанно.

### B8 [Проверено] Прочее сборки — чисто
Решение: NvJpegCollector имеет только Release-маппинг в sln (Debug-билд решения его корректно пропускает); C# OutputPath → общий `bin/`; C++ OutDir → общий `bin/`; Verify-блок Deploy.cmd полный (ловит отсутствующие CUDA dll); MakeBin/MakePublish имеют фолбэк `7za` при отсутствии WinRAR (GH-раннеры survive); vcpkg.json соответствует фактическим include'ам (libjpeg-turbo, openjpeg, webp, heif, avif, jxl, simd); `.gitignore` не пропускает bin/obj/adi/log в индекс (git ls-files чист).

---

## §5. Сквозные темы (чинить классами, не по одному)

1. **Префикс-матчинг путей без границы разделителя** — 4 независимых копии одной ошибки: N5 (adEngine, FilterByPool), N11 (реестр БД), S14 (AutoSelector пулы). Один хелпер на каждой стороне (N5 C++ + S14 C#), все сайты переводятся на него.
2. **Контракт коллектор↔DLL нарушен в 3 местах**: C5 (CRC превью vs файла), C6 (алгоритм превью), C7/N7 (thumbSize без валидации). Любой из них делает смешанные БД тихо несравнимыми. Чинить парой: и писателя, и читателя; старые БД без CRC-миграции продолжат работать (CRC-штраф — эвристика, не критерий).
3. **Глотание ошибок на границах**: unchecked `fread`/`fwrite` (N6, C3), игнор CUDA-кодов (C8, N14), `catch { Trace }` (S7), thread-без-try/catch (S5). Правило: на границе формата/IPC ошибка = отказ от записи/вызова, не нули.
4. **Мёртвый код как симптом**: `UpdateGpuDatabase`, `bufferFullCount`, стриминговый цикл, `--batch`, `GenerateAdiFileName`, `jpegPeaks`, `adPathWithSubFolderW`, экспорты N12 — почти каждый мёртвый элемент связан с реальным багом (N3, N1) или вводит в заблуждение (S8). Удалить после фиксов-хозяев.

## §6. Порядок внедрения (каждый пункт ≈ один маленький PR)

| PR | Состав | Почему сначала |
|----|--------|----------------|
| 1 | ✅ C1, C2, C4, C10 (корректность update + UB загрузчика) | Портит данные пользователей прямо сейчас |
| 2 | ✅ N4, N6, N7/C7, C13 (валидация форматов при загрузке) | Краш/мусор на битых входных, обе стороны контракта |
| 3 | ✅ N2, N3, S1, S3 (неверные результаты: GPU-гейт, VRAM-слоты, AutoSelector, CoreOptions) | Тихо неверные результаты — худший класс |
| 4 | ✅ S2, S4, S5, S13 (UI-безопасность батчей и потоков) | Данные под угрозой от одного клика |
| 5 | ◐ N5+S14+N11 ✅; C5, C6 ⏭ отложены (сквозные контракты: префиксы, CRC, превью) | Классовые фиксы, требуют Smoke на GPU+CPU |
| 6 | ✅ B1, B3 (упаковка/деплой) | Дистрибутив с неработающим коллектором |
| 7 | ⏭ N8-N12, C3, C8, C9 ✅, S6-S12, S15-S18, B2, B5 (робастность) | Вторая волна |
| 8 | ⏭ P3-чистка (N13-N21, C11-C17, S19-S34, B4, B6 ✅, B7) + удаление мёртвого кода | После стабилизации |

После каждого PR: `cmd\Deploy.cmd` до `[OK] Deploy complete.` + Smoke из DEV_GUIDE §1 (коллектор → поиск SqSum и SSIM → delete pair → рестарт).

## §7. Остаточные проблемы — безопасно НЕ фиксируется автоматически

1. **N1/Bug-08 (5M cap)** — требует изменения сигнатуры ядра и перезапуска по полосам; патч-скетч в N1 готов, но изменение GPU-плана нужно верифицировать на реальном корпусе (регресс скорости), прежде чем включать.
2. **Restore из корзины (N10)** — честная реализация (IFileOperation / исходная temp-схема оригинала) — отдельная фича; сейчас минимальный фикс только прекращает врать про успех.
3. **Смешанные БД со старым CRC (C5)** — пересчёт CRC файлов для уже собранных БД потребовал бы полной перечитки источников или `--rehash`-режима; миграция не бесплатная, решение за владельцем.
4. **WPF-паритет** — Rot прогрессирует (S8/WPF-найдётки); чинить точечно только при запросе (правило AGENTS.md).
5. **S17 (shutdown race)** — правильное решение — кооперативная отмена сохранения; «утечь core при таймауте» — паллиатив, безопасный на выходе процесса.
6. **Batch cancel (WP-B)** — остаётся открытым пунктом DEV_GUIDE; частично перекрывается S2.
7. **Тесты** — автотестов нет; перед PR3/PR5 минимально необходимы фикстуры: magic-detect, thumbBytes≠side², thumbSize-mismatch reject, MarkRemoved enum size, AutoSelector criteria-matrix (S1), update-duplicate-records (C1) — иначе фиксы не на что опереть.

## §8. Валидация

- `cmd\Deploy.cmd`: **пройден** — `[OK] Deploy complete.` (exit 0); C++ (AntiDupl.dll, NvJpegCollector.exe) и C# (WinForms + Core) собрались, CUDA-депенденси и ресурсы на месте, все артефакты верифицированы (16.08.2026, коммит `04d9495`).
- Перепроверка после фиксов 2026-08-18: **`cmd\Deploy.cmd` пройден** (`[OK] Deploy complete.`); одиночные MSBuild-сборки AntiDupl.dll / NvJpegCollector.exe / WinForms — 0 ошибок; запуск `bin\Release\AntiDupl.NET.WinForms.exe` — стартует, процесс жив, закрывается штатно. Полный GUI Smoke (коллектор → поиск → delete → auto-select → restart) требует ручного прогона — см. DEV_GUIDE §1.
- Автотестов в решении нет — CI проверяет только сборку (см. §7.7).
- Lint/typecheck: не настроены (C# — /warnasdefault; C++ — W3). Рекомендация: включить `/W4` + `TreatWarningAsError` на новый код — не в этом аудите.

## §9. Допущения

1. «Портативность данных рядом с exe» и два формата `.adi` — незыблемые инварианты (AGENTS.md); фиксы N7/C7 намеренно отвергают, а не конвертируют чужой thumbSize — консервативный выбор в пользу инварианта.
2. Поведенческие фиксы (N2, S1, S2, S3, C1) меняют наблюдаемое поведение там, где текущее объективно противоречит настройкам пользователя/формату данных; это разрешено стандартом ревью («preserve existing intended behaviour unless clearly broken»).
3. Номера строк указаны на коммит `04d9495`; после PR-1..8 будут сдвигаться — привязываться к символам (`MatchCallback`, `ProcessGray` и т.п.), они названы в каждом пункте.
4. Агентские находки P1 перепроверены вручную (N2, N3, C1 — grep/чтение исходника в этом отчёте); находки P2/P3 приведены as-is с готовыми патчами — применять по одному с Smoke-проверкой.
5. Гонки/UB-пункты (C2, S6, N8) доказаны статически (пути кода), не воспроизведены динамически — поэтому им P1/P2, а не P0, и они не «прогоняются» до фикса.

---

*Аудит подготовлен ZCode (read-only ревью, 2026-08-16). Код приложения не изменялся; все патчи — предложения, готовые к применению PR-батчами §6.*

*Применение фиксов 2026-08-18: закрыты C1-C2, C4, C8-C10, C13, N2-N7, N11, N18-N19, S1-S5, S13-S14, B1, B3, B6 (маркеры ✅). Отложены (не фиксить без согласования): N1, C3, C5-C6, N8-N10, N12, B2, B4-B5, B7 и P3-чистки.*
