# План развития AntiDuplPlus — Актуальное состояние (обновлено 16.08.2026)

## Архитектурный справочник

### Два формата .adi
- **DLL-native**: index использует magic `"adii"` (`0x69696461`), data — `"adid"` + version. Записывается при сканировании файлов (CPU) / DLL save.
- **Collector-native**: Без заголовков, raw fwrite (первый u32 = ThumbSize). Записывается NvJpegCollector. blockiness/blurring — f32.
- `LoadData()` читает DLL-native, `Load()` авто-детектит формат → `LoadCollectorNative()`. Не путать.

### GPU режимы
- `GpuCompareAllVsAll` — аллоцирует собственную VRAM через `cudaMalloc`. Глобальные буферы (`GpuCreateBuffer`/`GpuUploadThumbnail`) используются только в `OneVsList` (CPU fallback).

---

## ✅ Завершённые этапы

### Этап 1: Стабилизация — ВЫПОЛНЕН
| Пункт | Статус | Файл |
|-------|--------|------|
| 1.1 Валидация `&&`→`||` | ✅ | adResultStorage.cpp |
| 1.2 Recursive mutex | ✅ | adGPUManager.h:122 |
| 1.3 TThreadQueue::Clear | ✅ | adThreadManagement.cpp:71-79 |
| 1.4 SSIM average/variance | ✅ | adImageDataStorage.cpp:628-629 (Load), NvJpegCollector/main.cpp (Save) |

### Этап 2: Мульти-базовость — ВЫПОЛНЕН
| Пункт | Статус | Файл |
|-------|--------|------|
| 2.1 ClearMemory в Search | ✅ | adEngine.cpp:540-541 |
| 2.2 Multi-DB load loop | ✅ | adEngine.cpp:564-581 + prevCount guard |

### Этап 3: Архитектура Пулов — ВЫПОЛНЕН
| Пункт | Статус | Файл |
|-------|--------|------|
| 3.1 Pool API (poolCompareMode) | ✅ | adCompareOptions в AntiDupl.h:441, adOptions.cpp:109 |
| 3.2 GPU pool masking | ✅ | adGPU.cu:77-78, 106-122 (5 режимов) |
| 3.3 Database Manager UI | ✅ | DatabaseManagerForm.cs (Pool1/Pool2 кнопки, ComboBox) |
| Pool lazy load | ✅ | DatabaseManagerForm.cs:726 |

### Аудит-фиксы (14.07.2026) — ВЫПОЛНЕНЫ
| ID | Статус | Файл |
|----|--------|------|
| B04 WPF progress | ✅ | SearchDllCommand.cs:126 |
| B03 Non-ASCII paths | ✅ | NvJpegCollector/main.cpp:629,632,656 |
| B09 gpu_debug.log | ✅ | adEngine.cpp:517-522, 616 |
| B02 static gpuBuffer | ✅ | adDataCollector.cpp:157-166 |
| B07 GPU failure msg | ✅ | adEngine.cpp:727 |
| B11 Size validation | ✅ | adOptions.cpp:153-155 |
| SSIM load fix | ✅ | adImageDataStorage.cpp:628-629 |
| Results Actual() fix | ✅ | adImageInfo.cpp:130 |
| Pool lazy load | ✅ | DatabaseManagerForm.cs:726 |
| blockiness/blurring | ✅ | NvJpegCollector/main.cpp (4 места) |
| B06 .NET 8 check | ✅ | Program.cs:37,58,77-90 |
| B08 Thread.Sleep race | ✅ | SearchDllCommand.cs (ManualResetEventSlim) |

---

## 📋 Известные ограничения

1. **hash=0 в NvJpegCollector**: Все изображения записывают `hash=0` (одно место после rewrite: `ProcessGray` в main.cpp; `SimpleCRC32` идёт только на имя файла БД). `Actual()` не проверяет hash — это нормально, но hash не несёт полезной информации.

2. **vcpkg simd**: Заголовки/библиотеки simd не попадают в triplet-директорию. Workaround: ручное копирование + `/p:VcpkgManifestInstall=false`.

3. **Нет тестов**: CI только проверяет что сборка проходит. Нет unit-тестов, интеграционных тестов.

4. **BUG-08**: match buffer cap 5e6 — `ctx.bufferFullCount` не заполняется ядром и не проверяется (мёртвое поле).

---

## ✅ Закрыто с момента предыдущей ревизии плана

- WPF framework mismatch — WPF переведён на `net8.0-windows`.
- Инкрементальное обновление баз — реализовано (`--update` в NvJpegCollector + кнопка Update в Database Manager).
- GPU filter parity (WP-A аудита) — ratio/min-max/transforms в `adEngine.cpp`.
- DB source folder remap — `RemapFrom`, трансляция путей при загрузке.
- C# output — общий `bin/<Config>/`.

---

## 🔮 Возможные направления развития

### Качество кода
- Исправить `hash=0` в NvJpegCollector (вычислять `SimpleCRC32(path)` при создании записи)
- BUG-08: заполнение `bufferFullCount` в kernel + warning в UI
- Добавить unit-тесты для критических модулей (adImageDataStorage, adResultStorage)

### Функциональность
- Batch cancel (WP-B аудита): CancellationToken в `AutoSelector.ExecuteBatch`
- Сравнение без ручной DB / quick scan (WP-C аудита — согласовать подход с пользователем)
- Кэширование загруженных баз в памяти
- Расширенный Auto-Select (CloneSpy-style массовый выбор)
