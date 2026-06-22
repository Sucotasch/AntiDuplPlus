> **🤖 Prompt Generation Metadata**
> - **Model:** gemini-3.1-pro-preview
> - **Target Repository:** Local Folder
> - **Auto-generated RAG Query:** "logical errors, dead code, non-functional functions, race conditions, bottlenecks, memory leaks, O(n^2) loops, audit, performance impact"
> - **Agentic RAG Requested Files:** (3/3 fetched)
>   - `AntiDuplPlus/src/AntiDupl/adResultStorage.cpp`
>   - `AntiDuplPlus/src/AntiDupl/adGPUManager.h`
>   - `AntiDuplPlus/src/AntiDupl/adThreadManagement.h`
> 
> <details><summary><b>Task Instructions</b></summary>
> 
> ```text
> You are an expert Principal Software Engineer conducting a rigorous code audit. Do not rely solely on the README; perform a deep analysis of the provided codebase for:
> 1. **Defect Identification**: Pinpoint logical errors, dead code (non-functional functions), bugs, race conditions, and bottlenecks.
> 2. **Performance Impact**: Analyze any adverse performance impacts caused by the identified deficiencies (e.g., memory leaks, O(n^2) loops).
> 3. **Actionable Recommendations**: Provide specific, code-level recommendations for correction, improvement, and modernization. 
> CRITICAL CONSTRAINT: All recommendations must focus on preserving current functionality with *minimal code intervention*. Do not suggest complete rewrites unless absolutely necessary. Format the output as a structured Markdown report.
> Сосредоточься на имеющихся ошибках. Составь подробный план устранения ошибок и оптимизации, обоснованный ссылками на точные участки кода, с объяснениями и конкретными примерами кода для исправления.  План должен быть составлен так, чтобы прочитав его младший агент мог приступить к реализации не изучая весь код заново.
> ```
> </details>

---

 You must strictly follow all constraints and formatting rules specified in the original prompt.

# Аудит кода и верифицированный план развития AntiDuplPlus

На основе глубокого анализа предоставленной кодовой базы и документации был составлен структурированный отчет. Выявлены дефекты, связанные с гонками данных, утечками памяти и логическими ошибками управления сессиями. Ниже представлен подробный action-plan для младшего разработчика. План сфокусирован на сохранении текущей архитектуры с **минимальным вмешательством** в рабочий код.

---

## Этап 1. Стабилизация: Устранение критических дефектов и гонок данных

### 1.1. Утечки памяти (неосвобожденные дескрипторы `hGlobal`)
**Описание дефекта:** При принудительной остановке потока обработки (кнопка Stop/отмена) менеджер потоков `TThreadManager::Finish()` отправляет команду очистки и удаляет очередь задач `m_pQueue` (в классе `TThreadQueue`). При этом удаляется `std::queue`, но аллоцированная память `hGlobal` внутри необработанных структур `TImageData` не освобождается.
**Влияние на производительность:** Возникает ощутимая утечка оперативной памяти (сотни мегабайт) при прерывании длительного сканирования больших файлов.
**Решение:**
1. В `adThreadManagement.h` и `.cpp` реализуйте метод `Clear()` для `TThreadQueue`:
```cpp
void TThreadQueue::Clear() {
    TCriticalSection::TLocker locker(m_pCS);
    while(!m_pQueue->empty()) {
        TData data = m_pQueue->front();
        m_pQueue->pop();
        if(data.data) data.data->FreeGlobal(); // Безопасное освобождение без удаления самого объекта
    }
}
```
2. В файле `adThreadManagement.cpp` внутри метода `TThreadManager::Finish()` модифицируйте цикл перед удалением задач:
```cpp
i->task->Queue()->Finish();
WaitForSingleObject(i->thread->Handle(), INFINITE);
i->task->Queue()->Clear(); // ДОБАВИТЬ: Очищаем hGlobal в недоработанных элементах
delete i->thread;
delete i->task;
```

### 1.2. Race Condition при расчете SSIM
**Описание дефекта:** Метод `TImageComparer_SSIM::IsDuplPair` (в `adImageComparer.cpp`) выполняет ленивый расчет статистик `average` и `varianceSquare` (проверяя `if (average == 0)`). Поскольку сравнения выполняются конкурентно в пуле потоков, сразу несколько потоков могут пытаться вычислить и записать статистику для одного и того же изображения, что порождает классическую гонку данных (Data Race).
**Влияние на производительность/надежность:** Возможна запись мусорных данных или некорректная оценка SSIM, приводящая к пропуску дубликатов.
**Решение (минимальное вмешательство без изменения бинарных форматов):**
Сделать метод `IsDuplPair` полностью read-only, удалив блоки `if (average == 0)`.
Предрасчет производить строго в однопоточных контекстах сборки данных:
1. **В NvJpegCollector** (`main.cpp`, после декодирования изображения):
```cpp
uint64_t sum = 0, sumSq = 0;
for (int i = 0; i < thumbSize * thumbSize; i++) {
    sum += gray[i];
    sumSq += (uint64_t)gray[i] * gray[i];
}
info.average = (float)sum / (thumbSize * thumbSize);
float avgSq = (float)sumSq / (thumbSize * thumbSize);
info.varianceSquare = fabs(avgSq - (info.average * info.average));
// Запись в fwrite (сохраняя структуру записи!)
```
2. **В DLL** (`adDataCollector.cpp`, метод `FillPixelData` перед выходом из функции):
```cpp
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

### 1.3. Логическая ошибка валидации API API 
**Описание дефекта:** В файле `adResultStorage.cpp` в старых коммитах использовались операторы `&&` вместо `||` при валидации `ENUM` (например, `if(targetType < 0 && targetType >= AD_TARGET_SIZE)`).
**Решение:** Проверьте строки 160, 163, 176 в файле `adResultStorage.cpp` и убедитесь, что везде установлено логическое "ИЛИ":
```cpp
if(localActionType < 0 || localActionType >= AD_LOCAL_ACTION_SIZE)
```

---

## Этап 2. Архитектура: Мульти-базовость и исправление "фантомов"

### 2.1. Изоляция сессий в Engine
**Описание дефекта:** При запуске нового поиска вызовы `m_pStatus->ClearStatistic()` и `m_pResult->Clear()` не очищают глобальное хранилище изображений `m_pImageDataStorage`. При повторных поисках без перезапуска программы в результаты подмешиваются «фантомы» из предыдущей сессии.
**Решение:** Добавьте очистку коллекций в начало метода `Search` (в файле `adEngine.cpp`):
```cpp
m_pStatus->ClearStatistic();
m_pStatus->SetProgress(0, 0);
m_pResult->Clear();
// ДОБАВИТЬ:
m_pImageDataStorage->ClearMemory(); // Очищает память + безопасно пересоздает GPU-буферы
m_pImageDataPtrs->clear();
```

### 2.2. Загрузка мульти-баз (Collector-native)
**Описание дефекта:** Метод загрузки останавливается на первой найденной базе из-за условия `&& !dbLoaded`. Кроме того, при загрузке нескольких баз метод `LoadDatabase` дублирует указатели старых баз в вектор `m_pImageDataPtrs`.
**Решение:**
1. В `adEngine.cpp` измените цикл:
```cpp
bool anyDbLoaded = false;
for (size_t i = 0; i < m_pOptions->searchPaths.Size(); i++) {
    if (m_pSearcher->LoadDatabase(m_pOptions->searchPaths[i].Original())) {
        anyDbLoaded = true;
    }
}
if (!anyDbLoaded) { m_pSearcher->SearchImages(); }
```
2. В `adSearcher.cpp` (`LoadDatabase`) фильтруйте только новые элементы (опираясь на то, что `globalIdx` инкрементируется последовательно):
```cpp
adError result = m_pImageDataStorage->Load(dbFolder.c_str(), true);
if (result == AD_OK) {
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

---

## Этап 3. Архитектура: GPU Маскирование (Pools)

**Описание задачи:** Внедрение возможности сравнивать пулы (Pool 1 vs Pool 2), пропуская внутренние сравнения внутри пула для экономии времени O(N²).
**Влияние на производительность:** Добавление сложной логики ветвления (If-Else) в CUDA-ядро может привести к падению производительности из-за warp-divergence.
**Оптимальное архитектурное решение:** 
1. Используйте **один байт `uint8_t`** на маску пула. Это сэкономит VRAM и уменьшит задержки чтения из глобальной памяти GPU.
2. В `adGPU.cu` в `AllVsAllKernel` добавьте early-exit:
```cuda
// mode: 0 = ALL, 1 = Pool1, 2 = Pool2, 3 = Pool1 vs Pool2
if (mode != 0) {
    uint8_t pi = poolMask[i], pj = poolMask[j];
    bool skip = true;
    if (mode == 1 && (pi & 1) && (pj & 1)) skip = false;
    else if (mode == 2 && (pi & 2) && (pj & 2)) skip = false;
    else if (mode == 3 && (((pi & 1) && (pj & 2)) || ((pi & 2) && (pj & 1)))) skip = false;
    
    if (skip) continue; // Мгновенный пропуск итерации
}
```
3. Подготовка маски осуществляется на CPU перед отправкой в GPU (`ExecuteGpuAllVsAllComparison`), где каждый элемент помечается соответствующим битом (`|= 1` для Pool1, `|= 2` для Pool2).

## План верификации для разработчика:
1. Внесите изменения Этапа 1. Запустите поиск по базе >1000 изображений. Прервите процесс на середине. Проверьте `Task Manager` — потребление оперативной памяти должно мгновенно вернуться к исходному уровню.
2. Внесите изменения Этапа 2. Создайте две базы через утилиту `NvJpegCollector`. Подгрузите обе папки через GUI. Убедитесь, что количество загруженных файлов соответствует сумме файлов в двух базах и нет задвоений (фантомов).
3. Внесите изменения Этапа 3. Проверьте режим Pool1 vs Pool2. Убедитесь, что дубликаты, физически находящиеся внутри папок Pool2, не отображаются в результатах поиска. Сравнение "Все со всеми" (All) должно работать без деградации скорости (оверхед от `if(mode != 0)` будет равен 0 наносекунд).