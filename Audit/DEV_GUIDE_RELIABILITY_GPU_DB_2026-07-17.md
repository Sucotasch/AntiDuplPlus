# Agent Development Guide — Reliability / GPU / DB (post-audit)

| Field | Value |
|-------|--------|
| **Created** | 2026-07-17 |
| **Updated** | 2026-08-16 (после фиксов BUG-01…07, 09, 11 + WP-A filter parity; re-check кода) |
| **Track** | Надёжность GPU-поиска, dual `.adi`, lifecycle результатов; дальше — UX без обязательной DB |
| **Audit** | `Audit/FULL_AUDIT_2026-07-17.md` (исторический снимок; **код новее**) + корневой `Audit.md` (полный аудит 2026-08-16, актуальные P1: C1, N2, N3, S1, S4, B1) |
| **Status source** | `PROJECT_CONTEXT.md` (18.07.2026) + re-check кода |
| **Rules** | `AGENTS.md`, `.agents/skills/karpathy/SKILL.md` |
| **Code in this doc** | **None** — только документация |

> **Trust code.** Если `FULL_AUDIT` говорит «баг открыт», а код уже чинит — верь коду и этому guide.

---

## 0. Концепция продукта (не менять)

**AntiDuplPlus** — Windows x64 desktop:

1. **NvJpegCollector** строит portable DB (`index.adi` + `0000.adi`).  
2. **Database Manager** — multi-DB, Pool1/Pool2.  
3. **GPU AllVsAll** (SqSum / SSIM) ищет near-duplicates.  
4. WinForms — primary UI; delete/move/auto-select; данные рядом с exe.

Не делать: cloud, отказ от collector-формата, замена WinForms «по пути», silent breaking change wire-layout `.adi`.

---

## 1. Onboarding за 15 минут

### Сборка

```bash
msbuild src\AntiDupl.sln /p:Configuration=Release /p:Platform=x64
```

Prereqs: VS2022 v143, CUDA 12.8+, vcpkg `x64-windows-static`, .NET 8.  
Simd path quirk и publish: `AGENTS.md` / `PROJECT_CONTEXT.md` § release.

**Важно:** C# output часто в `src/AntiDupl.NET.WinForms/bin/Release/` — при ручной сборке копировать exe/dll в `bin/Release/` рядом с `AntiDupl.dll`.

### Smoke (после любой правки в search/DB)

1. Collector: папка → `databases/<Name>/`  
2. Database Manager: On → Search (SqSum и SSIM)  
3. Delete pair → Recycle Bin  
4. Auto-Select → Move → файл на месте, пара исчезла из списка  
5. Restart → `.adr` результаты на месте  

Автотестов **нет**. CI только build.

### Must-read files

| Роль | Путь |
|------|------|
| Search / GPU pack / MatchCallback | `src/AntiDupl/adEngine.cpp` |
| Dual `.adi` load/save | `src/AntiDupl/adImageDataStorage.cpp` |
| Kernels / VRAM | `src/AntiDupl/adGPU.cu` |
| Collector write | `src/NvJpegCollector/main.cpp` |
| Multi-DB entry | `src/AntiDupl/adSearcher.cpp` |
| MarkRemoved | `src/AntiDupl/adUndoRedoEngine.cpp`, `AntiDupl.h` |
| Search UI | `src/AntiDupl.NET.WinForms/Form/SearchExecuterForm.cs` |
| Batch delete/move | `src/AntiDupl.NET.WinForms/AutoSelector.cs` |
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

---

## 2. Статус аудита (что уже сделано)

Коммиты: `ae1e450` (8 bugs), `4c915df` (BUG-05 MarkRemoved), `c445e48` (docs), `895c8d2` (native robustness/leaks/CI), плюс пост-аудит: WP-A filter parity, collector rewrite (`4ddd1cd`…`f6aecaa`), DB folder remap (`06378b2`), `--size`+VRAM (`6618a3d`).

| ID | Sev | Статус | Где в коде (re-check 18.07) |
|----|-----|--------|----------------------------|
| BUG-01 | P0 | ✅ | `adEngine.cpp` pack: `data->side == reducedImageSize` |
| BUG-02 | P1 | ✅ | `Load()`: `0x69696461u` (`"adii"`) |
| BUG-03 | P1 | ✅ | `LoadCollectorData`: `thumbBytes == side*side` else skip thumb |
| BUG-04 | P1 | ✅ partial | `MatchCallback`: type/size/folder/searchPath; **нет ratio / min-max / transforms** |
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

### Что появилось после аудита 17.07 (re-check 16.08)

| Фича | Где |
|------|-----|
| **DB source folder remap** — БД «переезжает» за перемещённой папкой: `RemapFrom` пишется в registry, DLL транслирует пути при загрузке; позже `Update` переписывает пути | `DatabaseManagerForm.cs` (`RemapFrom`, col. `RemapIndicator`), `adDatabaseRegistry.cpp` / `adSearcher.cpp` |
| **Collector переписан**: Y-decode, async pipeline с worker pool, Simd-детекторы, single sequential reader (anti HDD-thrash), фикс hang на недекодируемых файлах, per-file stats + failed-file log | `NvJpegCollector/main.cpp` (коммиты `4ddd1cd`…`f6aecaa`) |
| **GUI передаёт `reducedImageSize` в collector (`--size`)** — thumb size DB синхронен с options; VRAM hint + pre-check размера БД | `MainMenu.cs` / `DatabaseManagerForm.cs`, `main.cpp` (`--size, -s`) |
| Версия | `src/version.txt` = 2.5.3 |

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
       useGpu? set m_skipComparisonDuringCollection
       CollectManager.Start()   // threads count sees skip flag
       collect loop → Finish
       if useGpu: ExecuteGpuAllVsAll
            pack only side==reducedImageSize
            kernel → MatchCallback (metadata filters) → AddDuplImagePair
       else: CompareManager (full CPU filters)
       optional FilterByPool

Batch move
  File.Move → ApplyToResult(MarkRemoved*) → CheckImageData
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

WP-0…2 из старого guide **закрыты**. WP-A (GPU filter parity) **тоже закрыт** (re-check 16.08: ratio — `adEngine.cpp` `MatchCallback`; min/max — pack loop; transforms → CPU через `transformedImage == FALSE` в условии `useGpu`). Ниже — актуальный backlog.

### ~~WP-A — Дожать GPU filter parity~~ ✅ ЗАКРЫТ (16.08)

Реализовано в `adEngine.cpp`: ratio check в `MatchCallback`, min/max size при pack, `useGpu=false` при `transformedImage == TRUE`.

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
| BUG-08 warn | Если `bufferFullCount>0` — status/log, не silent |
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

См. §1. Дополнительно для WP-A:

- Options: ratio control on; GPU search  
- Options: min size high; tiny images excluded  
- `transformedImage` on: document behavior after change  

### Runtime notes already used

- ~7.3k images, SqSum + SSIM, GPU SUCCESS (после ae1e450 / 4c915df)

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
  ExecuteGpuAllVsAll     ← pack side + min/max (done)
  Search()               ← useGpu / skip flag order + transformedImage→CPU (done)
adImageDataStorage.cpp
  Load / LoadCollectorData  ← magic + thumbBytes (done)
adGPU.cu
  poolMask cleanup (done); match cap (BUG-08 open — bufferFullCount не заполняется)
adUndoRedoEngine.*
  MarkRemoved (done) — pattern for non-delete result updates
AutoSelector.ExecuteBatch
  delete ApplyToResult; move File.Move+MarkRemoved (done)
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

### D. Match cap warning (WP-D) — единственный оставшийся

Сейчас `ctx.bufferFullCount` не заполняется ядром и не проверяется. Нужно: счётчик в kernel → после GPU success `if (ctx.bufferFullCount > 0)` → status/log, не silent.

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
| Batch cancel | Partial progress + no crash (WP-B) |
| No-DB path | User can search folder without manual DB steps (WP-C) |
| Collector DBs | Existing load unchanged |
| Diff size | Surgical |

---

## 12. Checklist перед PR

- [ ] Один WP  
- [ ] Нет unrelated format  
- [ ] I1–I5 invariants  
- [ ] Smoke §1  
- [ ] Interop enum sync если трогали actions  
- [ ] Не коммитить `bin/`, local DBs, hand-edited External  
- [ ] Обновить `PROJECT_CONTEXT.md` статусы если closed new items  

---

## 13. Quick reference

| Need | File |
|------|------|
| Исторический bug list | `Audit/FULL_AUDIT_2026-07-17.md` |
| Живой статус продукта | `PROJECT_CONTEXT.md` |
| Build / invariants | `AGENTS.md` |
| Этот guide | `Audit/DEV_GUIDE_RELIABILITY_GPU_DB_2026-07-17.md` |
| Discipline | `.agents/skills/karpathy/SKILL.md` |

---

## 14. Summary for incoming agent

Большая часть reliability-аудита **уже в master**, включая WP-A (GPU filter parity). Не начинай с WP-0/magic/OOB/ratio — это сделано. BUG-13 (hash=0) всё ещё открыт — `SimpleCRC32` в collector сейчас идёт только на имя файла, записи пишутся с `hash=0`.

**Следующие осмысленные шаги:**

1. **WP-B** — cancel для batch (в `AutoSelector.cs` нет отмены).  
2. **WP-C** — «сравнение без ручной DB» (сначала согласовать A/B/C с человеком).  
3. **WP-D** — BUG-08: `bufferFullCount` сейчас мёртвое поле (не заполняется ядром); tests — по остатку.

Не redesign. Не ломать collector format. Маленькие патчи + smoke.

**Suggested next user command:**  
`implement WP-B only`  
или  
`design WP-C: quick scan vs classic SearchImages` (если нужен product direction).

---

*End of dev guide — updated 2026-08-16*
