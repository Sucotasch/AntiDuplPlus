> **🤖 Prompt Generation Metadata**
> - **Model:** gemini-3.1-pro-preview
> - **Target Repository:** Local Folder
> - **Auto-generated RAG Query:** "You are an expert Principal Software Engineer conducting a rigorous code audit. Do not rely solely on the README; perform a deep analysis of the provided codebase for:
1. **Defect Identification**: Pinpoint logical errors, dead code (non-functional functions), bugs, race conditions, and bottlenecks.
2. **Performance Impact**: Analyze any adverse performance impacts caused by the identified deficiencies (e.g., memory leaks, O(n^2) loops).
3. **Actionable Recommendations**: Provide specific, code-

Attached Documents: PROJECT_CONTEXT.md, IMPLEMENTATION_PLAN.md"
> - **Agentic RAG Requested Files:** (0/3 fetched)
>   - `src/AntiDupl/adResultStorage.cpp`
>   - `src/AntiDupl/adEngine.cpp`
>   - `src/AntiDupl/adSearcher.cpp`
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



# Отчет об аудите кода AntiDuplPlus: Архитектура, Дефекты и Оптимизация

На основе проведенного анализа кодовой базы (файлов `adEngine.cpp`, `adSearcher.cpp`, `adImageDataStorage.cpp`, `adGPUManager.h`, `adThreadManagement.h`, `adImageComparer.cpp`, `main.cpp` и компонентов .NET), подготовлен подробный и структурированный план устранения багов и оптимизации производительности. 

Данный план составлен как **прямое руководство к действию для разработчика (Junior/Middle)**. Каждая проблема сопровождается точным указанием файлов и готовыми фрагментами кода для внедрения. Основной упор сделан на сохранение существующей архитектуры (минимальное вмешательство) и стабилизацию двух кодепутей загрузки баз (`.adi`).

---

## 🛑 ПРИОРИТЕТ 0: Критические ошибки поиска и загрузки (Блокеры)

### 0.1 Фантомные результаты (Утечка состояния сессии)
**Проблема:** Глобальное хранилище `m_storage` никогда не очищается между вызовами поиска. Новые результаты смешиваются со старыми. 
**Файл:** `src/AntiDupl/adEngine.cpp` (функция `TEngine::Search()`, строки ~386-393)

**Решение:**
Необходимо принудительно очищать списки и память перед каждым новым поиском.
Найдите блок очистки статуса (около строки 393):
```cpp
m_pStatus->ClearStatistic();
m_pStatus->SetProgress(0, 0);
m_pResult->Clear();
// ДОБАВИТЬ СЛЕДУЮЩИЕ ДВЕ СТРОКИ:
m_pImageDataStorage->ClearMemory();
m_pImageDataPtrs->clear();
```

### 0.2 Ошибка загрузки баз NvJpegCollector (INVALID_FILE_FORMAT)
**Проблема:** `NvJpegCollector` пишет базы в формате *Collector-native* (без заголовка `"adid"`, начинается с `thumbSize=32`), но метод `Load()` вызывает `LoadIndex()`, который ожидает заголовок *DLL-native*. В результате пути загружаются, но база отбрасывается с ошибкой `error=4`.
**Файл:** `src/AntiDupl/adImageDataStorage.cpp` (метод `Load`)

**Решение:**
Модифицировать логику чтения внутри `Load()` для определения формата на лету по первым 4 байтам `index.adi`. **Не изменяйте** саму функцию `LoadIndex()`.

```cpp
// Псевдокод логики для внедрения в начало adImageDataStorage::Load:
FILE* f = _wfopen(indexFilePath.c_str(), L"rb");
if (f) {
    uint32_t header = 0;
    fread(&header, 4, 1, f);
    fclose(f);
    
    // Если первые 4 байта НЕ равны "adid" (0x64696461), значит это Collector-native.
    if (header != 0x64696461) {
        // Читать через старый raw-метод Collector'a (fread напрямую)
        // Пропустить вызов LoadIndex() и LoadData()
    } else {
        // Стандартное чтение DLL-native
        // LoadIndex(...)
        // LoadData(...)
    }
}
```

### 0.3 Проблема: Search paths не доходят до движка
**Проблема:** C#-оболочка передает пути поиска, но они молча отклоняются. Макрос `CHECK_ACCESS` внутри `adPathWithSubFolderSetW` возвращает `AD_ERROR_ACCESS_DENIED`, а C# игнорирует ошибку.
**Файл:** `src/AntiDupl.NET.Core/CoreLib.cs` (или аналогичный класс P/Invoke), `src/AntiDupl/adPath.cpp`

**Решение:**
1. В C# коде при вызове установки путей (`adPathWithSubFolderSetW`) обязательно проверяйте код возврата и выводите/логируйте ошибку.
2. В C++ временно отключите проверку `CHECK_ACCESS` для путей поиска баз данных, либо убедитесь, что пути передаются с правильными слэшами и правами доступа.

---

## 🛠 ЭТАП 1: Устранение утечек и мертвых блокировок (Стабилизация)

### 1.1 Логическая ошибка валидации (Dead code)
**Проблема:** В трех местах допущена ошибка условия (использовано `&&` вместо `||`). Условие вида `x < 0 && x >= MAX` никогда не выполнится.
**Файл:** `src/AntiDupl/adResultStorage.cpp` (строки 160, 163, 176)

**Решение:**
Заменить `&&` на `||` во всех трех проверках:
```cpp
// Было:
if(localActionType < 0 && localActionType >= AD_LOCAL_ACTION_SIZE)
if(targetType < 0 && targetType >= AD_TARGET_SIZE)
if(renameCurrentType < 0 && renameCurrentType >= AD_RENAME_CURRENT_SIZE)

// Стало:
if(localActionType < 0 || localActionType >= AD_LOCAL_ACTION_SIZE)
if(targetType < 0 || targetType >= AD_TARGET_SIZE)
if(renameCurrentType < 0 || renameCurrentType >= AD_RENAME_CURRENT_SIZE)
```

### 1.2 Защита от GPU Deadlock (Превентивная)
**Проблема:** `GpuCompareAllVsAll` использует `std::mutex`. Вызываемый после синхронизации коллбэк может в будущем обратиться к GPU Manager, что вызовет Deadlock.
**Файл:** `src/AntiDupl/adGPUManager.h` (строка 97)

**Решение:**
```cpp
// 1. Заменить объявление мьютекса:
mutable std::recursive_mutex m_mutex; 

// 2. Во всех методах adGPUManager.cpp / .h изменить lock_guard:
std::lock_guard<std::recursive_mutex> lock(m_mutex);
```

### 1.3 Утечка системной памяти (`hGlobal`) при прерывании
**Проблема:** Если остановить поиск, `TThreadQueue` уничтожает `std::queue`, но не вызывает `FreeGlobal()` для оставшихся внутри изображений.
**Файлы:** `src/AntiDupl/adThreadManagement.h` и `src/AntiDupl/adThreadManagement.cpp`

**Решение:**
Добавить метод очистки в класс `TThreadQueue` (`adThreadManagement.h`):
```cpp
void Clear() {
    TCriticalSection::TLocker locker(m_pCS);
    while(!m_pQueue->empty()) {
        TData data = m_pQueue->front();
        m_pQueue->pop();
        if(data.data) data.data->FreeGlobal(); // Безопасно освобождаем хэндл памяти
    }
}
```
В `adThreadManagement.cpp` (метод `TThreadManager::Finish()`, строка ~199), добавить вызов перед удалением задачи:
```cpp
i->task->Queue()->Finish();
WaitForSingleObject(i->thread->Handle(), INFINITE);
i->task->Queue()->Clear(); // <-- ДОБАВИТЬ ЭТО
delete i->thread;
delete i->task;
```

---

## 🗄 ЭТАП 2: Корректная мульти-загрузка баз

### 2.1 Преждевременная остановка загрузки баз
**Проблема:** Движок прекращает перебор папок поиска после первой успешно загруженной базы из-за условия `&& !dbLoaded`.
**Файл:** `src/AntiDupl/adEngine.cpp` (строки 396-412)

**Решение:**
Изменить цикл загрузки, чтобы он перебирал *все* пути:
```cpp
bool anyDbLoaded = false;
for (size_t i = 0; i < m_pOptions->searchPaths.Size(); i++) {
    if (m_pSearcher->LoadDatabase(m_pOptions->searchPaths[i].Original())) {
        anyDbLoaded = true; // Фиксируем успех, но продолжаем цикл
    }
}
if (!anyDbLoaded) { 
    m_pSearcher->SearchImages(); 
}
```

### 2.2 Дублирование указателей при загрузке нескольких баз
**Проблема:** Метод `LoadDatabase` перебирает *всё* хранилище `m_storage` каждый раз. Если баз две, изображения из первой добавятся в `m_pImageDataPtrs` дважды.
**Файл:** `src/AntiDupl/adSearcher.cpp` (метод `LoadDatabase`, строки 222-231)

**Решение:**
Добавлять только новые записи, отсекая их по `globalIdx` (который растет монотонно).
```cpp
adError result = m_pImageDataStorage->Load(dbFolder.c_str(), true);
if (result == AD_OK) {
    // ЗАПОМНИТЬ ПРЕДЫДУЩИЙ РАЗМЕР
    size_t prevSize = m_pImageDataPtrs->size(); 
    const TImageDataStorage::TStorage& storage = m_pImageDataStorage->Storage();
    for (auto it = storage.begin(); it != storage.end(); ++it) {
        // ДОБАВИТЬ ФИЛЬТР ПО globalIdx
        if (it->second && it->second->data && it->second->globalIdx >= prevSize) {
            m_pImageDataPtrs->push_back(it->second);
        }
    }
    return true;
}
```

---

## ⚡ ЭТАП 3: Устранение Race Condition в SSIM алгоритме

**Проблема:** Многопоточная гонка (Race Condition) при расчете SSIM `average` и `varianceSquare`. Несколько потоков читают/пишут эти значения одновременно в `adImageComparer.cpp`.

**Решение: Сделать метод сравнения Read-Only, перенеся вычисления на стадию сбора данных.**

**1. NvJpegCollector (генератор баз)**
*Файл:* `src/NvJpegCollector/main.cpp` (после строки 318, внутри `ProcessDecoded`):
```cpp
// Добавить расчет вместо отправки нулей:
uint64_t sum = 0, sumSq = 0;
for (int i = 0; i < thumbSize * thumbSize; i++) {
    sum += gray[i];
    sumSq += (uint64_t)gray[i] * gray[i];
}
info.average = (float)sum / (thumbSize * thumbSize);
float avgSq = (float)sumSq / (thumbSize * thumbSize);
info.varianceSquare = fabs(avgSq - (info.average * info.average));
```
*Замените запись заглушек (нулей) в файл на запись реальных `average` и `varianceSquare` (строки 518-519).*

**2. AntiDupl Engine (сбор в реальном времени)**
*Файл:* `src/AntiDupl/adDataCollector.cpp` (после `data.filled = true;`):
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

**3. Удаление Race Condition**
*Файл:* `src/AntiDupl/adImageComparer.cpp`
Удалите строки 447-482 (четыре блока проверки `if (average == 0)` внутри метода `TImageComparer_SSIM::IsDuplPair`). Значения уже гарантированно будут рассчитаны.

---

## 🚀 Резюме для разработчика
1. **Сначала** примените исправления из раздела **Приоритет 0**, так как без них программа некорректно загружает пути и дублирует данные в памяти. Это починит UI (Database Manager) и саму суть "фантомов".
2. Внедрите Этап 1 и Этап 2 для обеспечения потокобезопасности и защиты от падений при прерывании поиска пользователем.
3. Правки Этапа 3 обязательны перед полноценным релизом GPU-ускорения для SSIM алгоритма, так как Race Condition сильно искажает процент сходства изображений (similarity threshold).