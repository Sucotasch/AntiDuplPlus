# Agent Development Guide — Reliability / GPU / DB (post-audit)

| Field | Value |
|-------|--------|
| **Created** | 2026-07-17 |
| **Updated** | 2026-10-19 (dupsearch этапы 1–6: pHASH, атомарность/лок DB, ratio, пресеты, безопасный batch, отметки «разобрано») · 2026-08-16 (BUG-01…07, 09, 11 + WP-A filter parity) |
| **Track** | Надёжность GPU-поиска, dual `.adi`, lifecycle результатов · dupsearch (pHASH, пороги, безопасный разбор результатов) · дальше — UX без обязательной DB |
| **Audit** | `Audit/FULL_AUDIT_2026-07-17.md` (исторический снимок; **код новее**) + корневой `Audit.md` (полный аудит 2026-08-16, актуальные P1: C1, N2, N3, S1, S4, B1) |
| **Status source** | `PROJECT_CONTEXT.md` (обновлён 19.10.2026, статусы dupsearch 1–6) + re-check кода |
| **Dupsearch** | план и журнал гейтов: `Research/2026-10-18-dupsearch/` (локальная папка, вне git) |
| **Rules** | `AGENTS.md`, `.agents/skills/karpathy/SKILL.md` |
| **Code in this doc** | **None** — только документация |

> **Trust code.** Если `FULL_AUDIT` говорит «баг открыт», а код уже чинит — верь коду и этому guide.

---

## 0. Концепция продукта (не менять)

**AntiDuplPlus** — Windows x64 desktop:

1. **NvJpegCollector** строит portable DB (`index.adi` + `0000.adi`).  
2. **Database Manager** — multi-DB, Pool1/Pool2.  
3. **GPU AllVsAll** (SqSum / SSIM / pHASH) ищет near-duplicates.  
4. WinForms — primary UI; delete/move/auto-select; данные рядом с exe.

Не делать: cloud, отказ от collector-формата, замена WinForms «по пути», silent breaking change wire-layout `.adi`.

---

## 1. Onboarding за 15 минут

### Сборка

```bash
msbuild src\AntiDupl.sln /p:Configuration=Release /p:Platform=x64
```

Prereqs: VS2022 v143, CUDA 12.8+, vcpkg `x64-windows-static`, .NET 8.  
Simd path quirk и publish: `AGENTS.md` / `PROJECT_CONTEXT.md` § release. Единый правильный путь сборки — `cmd\Deploy.cmd`.

**Важно:** при ручной сборке C# добавь `/p:Platform=x64` — иначе output уходит в `src/AntiDupl.NET.WinForms/bin/Release/` и его надо копировать в `bin/Release/` рядом с `AntiDupl.dll`.

### Smoke (после любой правки в search/DB/GUI)

1. Collector: папка → `databases/<Name>/`  
2. Database Manager: On → Search (SqSum, SSIM, pHASH)  
3. Delete pair → Recycle Bin  
4. Auto-Select → Move → файл на месте, пара исчезла из списка  
5. Restart → `.adr` результаты на месте  
6. **GUI стартует**: окно из `bin/Release/` поднимается и держится ~10 с — конструирование GUI не покрыто ни одним автотестом, краш старта (меню-пункт `null`, 19.10) проходил все гейты  

Автотестов в solution нет; CI только build. Дополнительно: контрактные тесты interop (66 проверок, `tests/AntiDupl.Contract.Tests`) и локальный харнесс гейтов dupsearch (§7).

### Must-read files

| Роль | Путь |
|------|------|
| Search / GPU pack / MatchCallback | `src/AntiDupl/adEngine.cpp` |
| pHASH / кэш хэшей | `src/AntiDupl/adPhash.cpp`, `adImageData.cpp` (`phashValid`/`phashVariants`), `adImageComparer.cpp` |
| Dual `.adi` load/save + atomic write | `src/AntiDupl/adImageDataStorage.cpp` |
| Kernels / VRAM (в т.ч. `GpuCompareAllVsAllHash`) | `src/AntiDupl/adGPU.cu` |
| Collector write | `src/NvJpegCollector/main.cpp` |
| Multi-DB entry | `src/AntiDupl/adSearcher.cpp` |
| MarkRemoved | `src/AntiDupl/adUndoRedoEngine.cpp`, `AntiDupl.h` |
| Search UI | `src/AntiDupl.NET.WinForms/Form/SearchExecuterForm.cs` |
| Batch delete/move, protected paths, разметка пар | `src/AntiDupl.NET.WinForms/AutoSelector.cs` |
| Отметки «разобрано» / фильтр | `src/AntiDupl.NET.WinForms/ProcessedPairs.cs`, `ResultFilter.cs` |
| Interop enum | `src/AntiDupl.NET.Core/Original/CoreDll.cs` |
| Core wrapper | `src/AntiDupl.NET.Core/CoreLib.cs` |

### Инварианты

| ID | Правило |
|----|---------|
| I1 | Collector raw layout ↔ `LoadCollectorData` — **не ломать** без миграции |
| I2 | DLL index magic **`"adii"`** (`0x69696461`); data **`"adid"`** |
| I3 | В одном GPU pack все thumbs с `data->side == options.reducedImageSize` |
| I4 | Portable paths: `ad_database.xml`, `databases/<Name>/` |
| I5 | Interop: `LocalActionType` / native enum синхронны (`MarkRemovedFirst=14`, `Second=15`) |
| I6 | Surgical diffs (Karpathy) |
| I7 | Запись DB атомарна (temp + rename; порядок `0000.adi` → `index.adi`), per-DB lock во всех режимах; неполная запись не должна попадать на диск |
| I8 | pHASH: 256-битный DCT-хэш (64×64 → блок 16×16), порог в % (`best*100 > threshold*256` ⇔ `best > maxHamming`, `maxHamming = threshold*256/100`), 8 D4-вариантов при `transformedImage`; в pHASH-режиме трансформации не уводят на CPU-путь (в SqSum/SSIM — уводят), GPU-путь требует `checkOnEquality == TRUE` |
| I9 | Скрытые фильтром пары не участвуют в batch/авто-выборе; `processed.tsv` (`Resources.UserPath`) — необязательный кэш отметок, его потеря не портит данные |

---

## 2. Статус аудита (что уже сделано)

Коммиты: `ae1e450` (8 bugs), `4c915df` (BUG-05 MarkRemoved), `c445e48` (docs), `895c8d2` (native robustness/leaks/CI), плюс пост-аудит: WP-A filter parity, collector rewrite (`4ddd1cd`…`f6aecaa`), DB folder remap (`06378b2`), `--size`+VRAM (`6618a3d`).

| ID | Sev | Статус | Где в коде (re-check 18.07) |
|----|-----|--------|----------------------------|
| BUG-01 | P0 | ✅ | `adEngine.cpp` pack: `data->side == reducedImageSize` |
| BUG-02 | P1 | ✅ | `Load()`: `0x69696461u` (`"adii"`) |
| BUG-03 | P1 | ✅ | `LoadCollectorData`: `thumbBytes == side*side` else skip thumb |
| BUG-04 | P1 | ✅ | `MatchCallback`: type/size/folder/searchPath + ratio (WP-A); min/max — в pack; transforms → CPU (кроме pHASH-режима, где трансформации считает ядро) |
| BUG-05 | P1 | ✅ | `File.Move` + `MarkRemovedFirst/Second` |
| BUG-06 | P1 | ✅ | shutdown timeout 10s (`MainForm`) |
| BUG-07 | P2 | ✅ | `validCount < 2` → `return true` |
| BUG-08 | P2→**P1** | ⏭ open | Обрезка 5M капов: лимит считается ДО метаданных-фильтров (кандидат-пар > 5M на больших корпусах), стриминговый цикл вычитки структурно мёртв (один проход), `bufferFullCount` не заполняется. План фикса — Audit.md N1 (полосы строк) |
| BUG-09 | P2 | ✅ | `d_poolMask` free on MS error paths |
| BUG-10 | P2 | ⏭ skipped | GPU→CPU fallback — сознательно не делали |
| BUG-11 | P3 | ✅ | skip flag **до** `CollectManager::Start()` |
| BUG-12 | P3 | ❌ closed | DB-only workflow — by design |
| BUG-13 | P3 | open | collector пишет `hash=0` для записей (`info.hash = 0` в `ProcessGray`, main.cpp; `SimpleCRC32` используется только для имени файла БД) |
| BUG-14 | P3 | open | нет unit/smoke automated tests |
| FIND-8 | — | open | Cancel не wired для batch flows |

### Известные остаточные дыры (не «всё зелёное»)

1. **BUG-08 — реальная тихая обрезка результатов (повышен до P1, аудит 16.08)** — лимит 5M считается по кандидат-парам ДО фильтров `MatchCallback` («стриминговый» цикл вычитки делает ровно один проход, т.е. мёртв); `ctx.bufferFullCount` не заполняется ядром. Фикс-план (полосы строк в ядре) — `Audit.md` N1.
2. **BUG-03 fail-soft** — bad thumb: `fseek` + continue, load всё равно `true` (не OOB, но запись может быть «пустой»).
3. **Shutdown 10s** — лучше, чем 2s; при очень большом `.adr` теоретически всё ещё race.
4. **MarkRemoved** — после move путь в FS новый, в result storage пара снимается; image DB чистится `CheckImageData` (файл «пропал» со старого path). Не путать с true rename-in-DB.

### Что появилось после аудита 17.07 (re-check 16.08; dupsearch — 19.10)

| Фича | Где |
|------|-----|
| **dupsearch 1 — pHASH**: 256-битный DCT-хэш, порог в % (дефолт 33, UI 0–50), GPU-ядро `GpuCompareAllVsAllHash`, пресеты «Похожие: строго/полнее» | `adPhash.cpp`, `adImageComparer.cpp`, `adEngine.cpp`, `adGPU.cu` |
| **dupsearch 2 — надёжность DB**: атомарная запись (temp + rename, `0000.adi` → `index.adi`), per-DB lock, защита записей от повторного декодирования | `adImageDataStorage.cpp`, `NvJpegCollector/main.cpp` |
| **dupsearch 3 — ratio по разрешению**: целочисленно, `ratioResolution = 32`, пара отбрасывается только если `|r1-r2| > 1` **и** `|r1+r2| > 1` | `adEngine.cpp` |
| **dupsearch 4 — пресеты поиска** в тулбаре: алгоритм + порог + трансформации в один клик | `MainToolStrip.cs`, `data/resources/strings/*` |
| **dupsearch 5 — безопасный batch**: удаление через выборку ядра (`SetSelection` + `ApplyToResult(*, Selected)`, две серии по сторонам), «Не удалять» (protected paths) с предупреждением о пропущенных файлах, «Оставить только это», Ctrl-U отмена разметки | `AutoSelector.cs`, `CorePathsForm.cs`, `MainMenu.cs` |
| **dupsearch 6 — «разобрано»**: отметки пар в `<user>/processed.tsv`, фильтр в тулбаре, счётчики `Разобрано` / `Показано: X из Y`; скрытые пары сохраняют пометки | `ProcessedPairs.cs`, `ResultFilter.cs`, `ResultsListView.cs`, `MainToolStrip.cs`, `MainStatusStrip.cs` |
| **DB source folder remap** — БД «переезжает» за перемещённой папкой: `RemapFrom` пишется в registry, DLL транслирует пути при загрузке; позже `Update` переписывает пути | `DatabaseManagerForm.cs` (`RemapFrom`, col. `RemapIndicator`), `adDatabaseRegistry.cpp` / `adSearcher.cpp` |
| **Collector переписан**: Y-decode, async pipeline с worker pool, Simd-детекторы, single sequential reader (anti HDD-thrash), фикс hang на недекодируемых файлах, per-file stats + failed-file log | `NvJpegCollector/main.cpp` (коммиты `4ddd1cd`…`f6aecaa`) |
| **GUI передаёт `reducedImageSize` в collector (`--size`)** — thumb size DB синхронен с options; VRAM hint + pre-check размера БД | `MainMenu.cs` / `DatabaseManagerForm.cs`, `main.cpp` (`--size, -s`) |
| Версия | `src/version.txt` = **2.6.0** |

---

## 3. Algorithm map (актуальный)

```
Search (WinForms)
  GetEnabledDatabasePaths + poolCompareMode
  → TEngine::Search
       ClearMemory
       for path: LoadDatabase → Storage::Load
            magic adii → DLL-native
            else → LoadCollectorNative (thumb bounds check)
       useGpu = GPU && algo ∈ {SqSum, SSIM, pHASH} && !ignoreFrameWidth &&
                (transformedImage == FALSE || algo == pHASH) && checkOnEquality
       useGpu? set m_skipComparisonDuringCollection
       CollectManager.Start()   // threads count sees skip flag
       collect loop → Finish
       if useGpu && algo == pHASH: ExecuteGpuAllVsAll
            EnsurePhash (кэш в adImageData) → pack hashes (×8 при transformedImage)
            kernel GpuCompareAllVsAllHash → порог % (maxHamming = thr*256/100)
                 → MatchCallback (metadata filters) → AddDuplImagePair
       else if useGpu: ExecuteGpuAllVsAll
            pack only side==reducedImageSize
            kernel → MatchCallback (metadata filters) → AddDuplImagePair
       else: CompareManager (full CPU filters; pHASH — тот же порог, adImageComparer)
       optional FilterByPool

Batch (AutoSelector.ExecuteBatch, этап 5)
  цели собираются по файлам один раз
  delete: SetSelection + ApplyToResult(DeleteFirst/Second, TargetType.Selected), две серии (первая/вторая сторона)
  move:   File.Move → ApplyToResult(MarkRemovedFirst/Second, Selected) → CheckImageData
  успех — по File.Exists; пометки «разобрано» — по клавишам выживших пар
```

### Dual format

| | Index first 4B | Data |
|--|----------------|------|
| DLL-native | `"adii"` | `"adid"` + stream |
| Collector | u32 ThumbSize (32=0x20) | raw records + avg/var |

---

## 4. Принципы для следующих задач

1. Safety first — bounds на load/compare.  
2. Один thumb size на search (options ↔ DB `ThumbSize`).  
3. GPU = ускоритель CPU-семантики, не другой продукт.  
4. Core owns identity файлов; UI не двигает файлы без API.  
5. Новые settings: **default = старое поведение**.  
6. Не трогать collector wire layout без versioned migration.  
7. Минимум кода (Karpathy).

---

## 5. Work packages (что делать дальше)

WP-0…2 из старого guide **закрыты**. WP-A (GPU filter parity) **тоже закрыт** (re-check 16.08: ratio — `adEngine.cpp` `MatchCallback`; min/max — pack loop; transforms → CPU через `transformedImage == FALSE` в условии `useGpu`, **кроме pHASH-режима** — там трансформации считает ядро вариантов D4).

### ~~WP-A — Дожать GPU filter parity~~ ✅ ЗАКРЫТ (16.08)

Реализовано в `adEngine.cpp`: ratio check в `MatchCallback`, min/max size при pack, `useGpu=false` при `transformedImage == TRUE` для SqSum/SSIM (в pHASH — `useGpu` разрешён).

### WP-B — Batch cancel (FIND-8)

| | |
|--|--|
| **Goal** | Cancel прерывает long batch delete/move |
| **Files** | `AutoSelector.cs`, caller in `MainMenu` / dialogs, optionally `ProgressForm` |
| **Sketch** | `CancellationToken` / flag; check each iteration; UI Stop button |
| **Default** | без cancel = as now |
| **Tests** | 1000 marks, cancel mid-way; partial counts correct |

### WP-C — Product: сравнение без предварительной DB

| | |
|--|--|
| **Goal** | Пользователь выбирает папку(и) → search без ручного Collector (или auto-collect background) |
| **Source** | `PROJECT_CONTEXT.md` «Планы на будущее» |
| **Не ломать** | существующий DB workflow, pools, registry |
| **Подходы (выбрать явно с user)** | |
| | **A.** UI «Quick scan»: temp collector → temp DB → Search → optional keep |
| | **B.** Classic `SearchImages` path when no enabled DB (уже есть fallback) + UX clarify |
| | **C.** Incremental: watch folder → `--update` collector |
| **Recommended start** | **B+A**: честный empty-state «нет DB → предложить Collector / quick scan»; не silent hybrid |
| **Files** | `SearchExecuterForm.cs`, `DatabaseManagerForm.cs`, `MainMenu`, maybe spawn `NvJpegCollector` |
| **Consequences** | | Risk | Mitigation |
| | | Долгий first scan | Progress + cancel |
| | | Путаница temp vs named DB | Явные имена `databases/_quick_*` |
| | | Thumb size mismatch | Sync reducedImageSize ↔ collector `--size` |

### WP-D — Hygiene (низкий приоритет)

| Item | Notes |
|------|--------|
| BUG-08 warn | **Отклонено 19.10** (dupsearch этап 7): усечение только пропускает пары (ложных срабатываний нет), строка `GPU comparison truncated: N pairs dropped` уже пишется в `trace.log`, прогонов на 10⁶ пар нет |
| BUG-13 hash | `SimpleCRC32(path)` в collector — только perf multimap |
| BUG-14 tests | Минимальный native/C# fixture: magic detect, thumb bounds, MarkRemoved enum size |
| Shutdown Join | `WaitForWorker` до Finish без жёсткого 10s, или cancel save |

### WP-E — Не делать (закрыто / skipped)

| Item | Why |
|------|-----|
| BUG-10 GPU→CPU fallback | Сознательно skipped |
| BUG-12 hybrid DB+FS always | By design DB workflow |
| Unify .adi formats | Migration hell |
| WPF feature parity drive-by | Out of track unless asked |

---

## 6. Порядок внедрения

```
WP-B (cancel)  →  WP-C (no-DB UX, с явным выбором A/B/C)  →  WP-D
```

Один WP ≈ один PR. Не смешивать MarkRemoved/interop с UI redesign.

---

## 7. Testing strategy

### Manual smoke (обязательно)

См. §1. Дополнительно:

- WP-A: Options → ratio control on, GPU search; min size high → tiny images excluded; `transformedImage` on → поведение задокументировано (кроме pHASH: трансформации считает ядро)  
- dupsearch: pHASH-порог 33 % на корпусе с известной истиной; batch на **копии** папки (удаление — в корзину, файлы protected-путей пропускаются); фильтр «разобрано» (скрытые пары не участвуют в batch)  
- GUI: окно поднимается и держится ~10 с (см. §1 п.6)  

### Runtime notes already used

- ~7.3k images, SqSum + SSIM, GPU SUCCESS (после ae1e450 / 4c915df)  
- 19.10: корпус A, pHASH 33/35 %, прогоны g61–g65 (см. журнал гейтов)  

### dupsearch gates (локальный харнесс)

`Research/2026-10-18-dupsearch/work/gates5` — подкоманды `g61`, `g61b`, `g62`, `g62b/s/t`, `g63`, `g64`, `g65` (синтетические корпуса и реальный корпус A), `ggui <userPath>` — smoke старта GUI. Результаты и допуски — `.../work/bench/results/gates-log.md`. Контрактные тесты interop — `tests/AntiDupl.Contract.Tests` (66 проверок; гонять после правок `AntiDupl.h` / `adOptions.h` / `CoreDll.cs`).

### Unit (желательно WP-D)

| Case | Expect |
|------|--------|
| first u32 `adii` / `32` | DLL / collector |
| thumbBytes ≠ side² | no overflow; skip or fail |
| LocalAction 14/15 | bound checks / switch |

---

## 8. Code map — hooks

```
adEngine.cpp
  MatchCallback          ← WP-A filters (done: ratio, type/size/folder/searchPath)
  ExecuteGpuAllVsAll     ← pack side + min/max (done); pHASH: EnsurePhash + pack хэшей + maxHamming (dupsearch 1)
  Search()               ← useGpu / skip flag order + transformedImage→CPU для SqSum/SSIM (done; pHASH остаётся на GPU)
adPhash.cpp / adImageComparer.cpp
  EnsurePhash, варианты D4, порог в % (dupsearch 1)
adImageDataStorage.cpp
  Load / LoadCollectorData  ← magic + thumbBytes (done); атомарная запись + lock (dupsearch 2)
adGPU.cu
  poolMask cleanup (done); match cap (BUG-08 — warn отклонён 19.10); GpuCompareAllVsAllHash (dupsearch 1)
adUndoRedoEngine.*
  MarkRemoved (done) — pattern for non-delete result updates; TargetType.Selected — база batch-удаления (dupsearch 5)
AutoSelector.ExecuteBatch
  delete через выборку ядра (SetSelection + ApplyToResult(*, Selected)); move File.Move + MarkRemoved (dupsearch 5)
  protected paths + KeepOnlyOne / UndoMarking (dupsearch 5); фильтр и отметки ProcessedPairs / ResultFilter (dupsearch 6)
  ← WP-B cancel (не реализовано — нет CancellationToken)
SearchExecuterForm / DatabaseManager
  ← WP-C empty-state / quick scan (не реализовано)
  + folder remap (RemapFrom) уже есть
```

### Interop checklist (любой новый LocalAction)

1. `AntiDupl.h` enum  
2. `adUndoRedoEngine` switch + implementation  
3. `CoreDll.cs` enum **same numeric values**  
4. `AD_LOCAL_ACTION_SIZE` / bounds usage still valid  
5. WinForms call site  

---

## 9. Micro-patches

A (ratio), B (min/max), C (transforms→CPU) — **реализованы** в `adEngine.cpp` (см. §5).

### D. Match cap warning (WP-D) — ❌ отклонено 19.10

Сейчас `ctx.bufferFullCount` не заполняется ядром и не проверяется. Решение (dupsearch этап 7): не делать — усечение только пропускает пары, ложных срабатываний не даёт, а строка `GPU comparison truncated: N pairs dropped` уже попадает в `trace.log`. Если появится сценарий на 10⁶+ пар — вернуться к этому пункту.

---

## 10. Anti-patterns

| Не делать | Почему |
|-----------|--------|
| Менять collector `fwrite` order | Ломает все DB |
| Detect index as `"adid"` | Это data magic |
| `memcpy` thumb без check `side` | OOB (уже ловили) |
| `File.Move` без MarkRemoved/API | Stale results (уже ловили) |
| Поднять nvJPEG batch >1 на consumer | Медленнее / illegal address risk |
| Drive-by WPF / rename solution | Out of track |
| «Унифицировать» два `.adi` без миграции | Data loss |
| Ручной edit `adExternal.h` / `External.cs` | Auto-gen from `version.txt` |
| Считать FULL_AUDIT источником истины без diff | Уже fixed в master |

---

## 11. Success metrics

| Metric | Target |
|--------|--------|
| P0/P1 из аудита 17.07 | ✅ closed (кроме intentional skips) |
| GPU ratio/min-max | ✅ = CPU-фильтры (WP-A closed) |
| dupsearch 1–6 | ✅ закрыты, гейты — `gates-log.md`: pHASH, атомарность/лок DB, ratio, пресеты, безопасный batch, «разобрано» |
| dupsearch 7 | ✅ не требуется (решение 19.10): sidecar-хэши ≈ MS по времени, тайлинг для 10⁶ не нужен, warn об усечении отклонён |
| GUI старт | ✅ окно поднимается (краш 19.10 пофиксен; проверка ручная — §1 п.6) |
| Batch cancel | Partial progress + no crash (WP-B) |
| No-DB path | User can search folder without manual DB steps (WP-C) |
| Collector DBs | Existing load unchanged |
| Diff size | Surgical |

---

## 12. Checklist перед PR

- [ ] Один WP  
- [ ] Нет unrelated format  
- [ ] I1–I9 invariants  
- [ ] Smoke §1 (включая старт GUI)  
- [ ] Interop enum sync если трогали actions (+ контрактные тесты, 66)  
- [ ] Не коммитить `bin/`, local DBs, hand-edited External  
- [ ] Обновить `PROJECT_CONTEXT.md` статусы если closed new items  
- [ ] User-facing фичи — обновить `README.md` (RU+EN) и локали `data/resources/strings/*.xml`  

---

## 13. Quick reference

| Need | File |
|------|------|
| Исторический bug list | `Audit/FULL_AUDIT_2026-07-17.md` |
| Живой статус продукта | `PROJECT_CONTEXT.md` |
| User-facing docs (RU+EN) | `README.md` |
| Build / invariants | `AGENTS.md` |
| Этот guide | `Audit/DEV_GUIDE_RELIABILITY_GPU_DB_2026-07-17.md` |
| Discipline | `.agents/skills/karpathy/SKILL.md` |

---

## 14. Summary for incoming agent

Большая часть reliability-аудита **уже в master**, включая WP-A (GPU filter parity). Не начинай с WP-0/magic/OOB/ratio — это сделано. BUG-13 (hash=0) всё ещё открыт — `SimpleCRC32` в collector сейчас идёт только на имя файла, записи пишутся с `hash=0`.

**dupsearch (18–19.10): этапы 1–6 в master** — pHASH (порог в %, дефолт 33, GPU-ядро хэшей), атомарность и лок DB, целочисленный ratio, пресеты поиска, безопасный batch (удаление через выборку ядра, protected paths, KeepOnlyOne, Ctrl-U), отметки «разобрано» с фильтром и счётчиками. Этап 7 закрыт как «не требуется» (решение пользователя 19.10); этап 8 — **Р2: верификация пикселями поверх хэшей** (дизайн — §11 плана, не реализовано). Статусы — `PROJECT_CONTEXT.md`, гейты — `Research/2026-10-18-dupsearch/work/bench/results/gates-log.md`.

**Проверка перед выдачей сборки:** `cmd\Deploy.cmd` + вручную убедиться, что GUI стартует (§1 п.6) — конструирование GUI не покрыто автотестами: 19.10 краш старта прошёл все гейты.

**Следующие осмысленные шаги:**

1. **dupsearch этап 8 (Р2)** — если нужна более строгая верификация совпадений pHASH.  
2. **WP-B** — cancel для batch (в `AutoSelector.cs` нет отмены).  
3. **WP-C** — «сравнение без ручной DB» (сначала согласовать A/B/C с человеком).  
4. **WP-D** — BUG-13/BUG-14; match-cap warn отклонён 19.10.  

Не redesign. Не ломать collector format. Маленькие патчи + smoke.

**Suggested next user command:**  
`implement WP-B only`  
или  
`design dupsearch stage 8: pixel verification`  
или  
`design WP-C: quick scan vs classic SearchImages` (если нужен product direction).

---

*End of dev guide — updated 2026-10-19*
