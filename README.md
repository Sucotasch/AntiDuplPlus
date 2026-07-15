# AntiDuplPlus

GPU-ускоренный поисковик дубликатов/похожих изображений. Fork [AntiDupl.NET](https://github.com/ermig1979/AntiDupl) с CUDA/nvJPEG ускорением.

![AntiDupl screenshot](https://ermig1979.github.io/AntiDupl/data/help/english/files/MainForm.png)

## Что изменилось по сравнению с оригиналом

| Компонент | Оригинал AntiDupl.NET | AntiDuplPlus |
|-----------|----------------------|--------------|
| Декодирование JPEG | CPU (libjpeg-turbo) | **GPU (nvJPEG)** — в 5-10x быстрее |
| Сравнение изображений | CPU (AllVsAll + OneVsList) | **GPU AllVsAll** (Squared Sum + SSIM) |
| Создание баз данных | N/A | **NvJpegCollector** — GPU-декодирование + генерация превью |
| Инкрементальное обновление | N/A | **--update** — добавление/удаление изменённых файлов |
| Управление базами | Search Paths (legacy) | **Database Manager** — создание, обновление, удаление, пулы |
| Pool сравнение | N/A | **Pool1 vs Pool2** — кросс-пуловое сравнение |
| Auto-Select | Базовый | **Расширенный** — время, размер, качество, разрешение, пулы (AND-логика) |
| Удаление файлов | Только в корзину | **Корзина + перемещение** в выбранную папку |
| Качество изображений | Базовый | **blockiness + blurring** — вычисляются при создании базы |
| SSIM | Нет | **GPU SSIM** — полнофункциональный алгоритм |

## Требования

### Для запуска
- **Windows** 10/11 x64
- **.NET 8.0 Runtime** — [Скачать](https://dotnet.microsoft.com/download/dotnet/8.0)
- **NVIDIA GPU** с поддержкой CUDA (RTX 20xx и новее) — для GPU-ускорения
- Если GPU нет — программа работает на CPU (медленнее)

### Для сборки
- **Visual Studio 2022** (Community Edition или выше)
- Workloads: `.NET Desktop development` + `Desktop development with C++`
- **CUDA Toolkit 12.8+** — [Скачать](https://developer.nvidia.com/cuda-downloads)
- **vcpkg** — [Репозиторий](https://github.com/Microsoft/vcpkg)
- **.NET 8.0 SDK**

## Быстрый старт

1. Скачайте [последний релиз](https://github.com/Sucotasch/AntiDuplPlus/releases)
2. Распакуйте архив
3. Установите [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
4. Запустите `AntiDupl.NET.WinForms.exe`

## Workflow (рабочий процесс)

### Шаг 1: Создание базы данных

1. Откройте **Tools → GPU Collector** (или нажмите кнопку на тулбаре)
2. Выберите папку с изображениями
3. NvJpegCollector декодирует изображения через GPU (JPEG) или CPU (PNG/BMP/TIFF/WebP/GIF)
4. Вычисляет **blockiness** и **blurring** для каждого изображения
5. Создаётся база: `index.adi` + `0000.adi` в папке `databases/<имя>/`
6. База автоматически регистрируется в `ad_database.xml`

### Шаг 2: Управление базами

1. Откройте **Tools → Database Manager**
2. Включите/выключите базы для поиска (галочка **On**)
3. Назначьте базы в **Pool1** (Reference) и **Pool2** (Target) для кросс-пулового сравнения
4. Выберите **Pool Comparison Mode**:
   - None — все пары
   - Pool1 Internal — только внутри Pool1
   - Pool2 Internal — только внутри Pool2
   - Cross — Pool1 vs Pool2
   - All Pools — только помеченные пулом

### Шаг 3: Поиск

1. Нажмите **Search** (или F5)
2. Программа загружает включённые базы через DLL
3. GPU сравнивает все изображения (AllVsAll)
4. Результаты отображаются в таблице
5. **Результаты сохраняются** и загружаются при следующем запуске

### Шаг 4: Обработка результатов

**Ручное удаление/перемещение:**
- ПКМ → Delete First / Delete Second / Delete Both
- ПКМ → Move First → Second folder / Move Second → First folder

**Автоматический выбор (Auto-Select):**
- Edit → Auto-Select → быстрые варианты (Older, Newer, Smaller, etc.)
- Edit → Auto-Select → Advanced → комбинированные критерии (AND-логика)
- Edit → Delete Selected / Move Selected to Folder

**Ручной edit:**
- Клик на столбец **Target** → переключение: 1st / 2nd / (пусто)
- Выделение строк → правка вручную

### Шаг 5: Обновление базы

Когда файлы в папке-источнике изменились:
```
NvJpegCollector.exe --input "D:\photos" --update
```
Или через **Database Manager → Update** кнопку.

## Структура проекта

```
AntiDuplPlus/
├── src/
│   ├── AntiDupl/                    # C++/CUDA ядро (AntiDupl.dll)
│   │   ├── adEngine.cpp             # Главный движок поиска
│   │   ├── adImageDataStorage.cpp   # Загрузка/сохранение баз (.adi, .adr)
│   │   ├── adGPU.cu                 # CUDA ядра сравнения (Mean Square + SSIM)
│   │   ├── adSearcher.cpp           # Загрузка баз + file scan
│   │   ├── adImageInfo.cpp          # Actual() — проверка актуальности файлов
│   │   └── adResultStorage.cpp      # Хранение результатов
│   ├── NvJpegCollector/             # GPU-утилита создания баз
│   │   └── main.cpp                 # nvJPEG декодирование + blockiness/blurring + запись .adi
│   ├── AntiDupl.NET.Core/           # C# обёртка (P/Invoke)
│   ├── AntiDupl.NET.WinForms/       # GUI приложение (основной)
│   │   ├── Forms/
│   │   │   ├── MainForm.cs          # Главная форма
│   │   │   ├── DatabaseManagerForm.cs  # Менеджер баз (Pool1/Pool2)
│   │   │   └── AutoSelectDialog.cs  # Расширенный автовыбор
│   │   ├── GUIControl/
│   │   │   ├── MainMenu.cs          # Меню + toolbar
│   │   │   ├── ResultsListView.cs   # Таблица результатов
│   │   │   └── ResultsListViewContextMenu.cs  # КМБ
│   │   ├── AutoSelector.cs          # Логика автовыбора
│   │   └── Options.cs               # Настройки
│   └── AntiDupl.NET.WPF/            # GUI приложение (WPF, вторичное)
├── bin/Release/                     # Готовые к запуску файлы
├── release/                         # Релизные архивы
└── Audit/                           # Отчёты аудита кода
```

## Формат баз данных

### Два формата .adi (не путать!)
- **DLL-native**: заголовок `"adid"` + version. Записывается `adImageDataStorage.cpp` (SaveData). Читается `adImageDataStorage.cpp` (LoadData).
- **Collector-native**: без заголовков, raw fwrite. Записывается `NvJpegCollector/main.cpp`. Читается `adSearcher.cpp` (LoadDatabase).

### Collector-native формат (0000.adi)
```
thumbSize(u32) + key(i16) + first(wstring) + last(wstring) + count(u64)
+ N records:
  path(wstring) + size(u64) + time(u64) + hash(u32) + type(u8)
  + width(u32) + height(u32) + blockiness(f64) + blurring(f64)
  + defect(u8) + crc32c(u64) + filled(u8)
  + thumb_size(u64) + thumb_data(bytes)
  + average(f32) + varianceSquare(f32)
```

## GPU-ускорение

| Операция | CPU | GPU | Ускорение |
|----------|-----|-----|-----------|
| Декодирование JPEG | libjpeg-turbo | nvJPEG | 5-10x |
| Сравнение AllVsAll | adImageComparer | adGPU.cu | 10-50x |
| Сравнение SSIM | adImageComparer | adGPU.cu (SsimKernel) | 10-50x |

### Поддерживаемые GPU
- RTX 20xx (Turing, SM 7.5)
- RTX 30xx (Ampere, SM 8.6)
- RTX 40xx (Ada Lovelace, SM 8.9)

## Командная строка

### NvJpegCollector
```bash
# Создать новую базу
NvJpegCollector.exe --input "D:\photos" --output "databases" --name "MyPhotos"

# Обновить существующую базу
NvJpegCollector.exe --input "D:\photos" --update

# Параметры
--input, -i    Путь к папке с изображениями
--output, -o   Корневая папка баз (по умолчанию: databases/)
--name, -n     Имя базы (по умолчанию: имя папки)
--size, -s     Размер превью (по умолчанию: 32)
--batch, -b    Размер батча nvJPEG (по умолчанию: 64)
--update, -u   Инкрементальное обновление
```

### Поддерживаемые форматы
| Формат | Декодирование | Примечание |
|--------|---------------|------------|
| JPEG/JPG/JFIF | GPU (nvJPEG) | Максимальная скорость |
| PNG | CPU (WIC) | |
| BMP | CPU (WIC) | |
| TIFF | CPU (WIC) | |
| WebP | CPU (WIC) | |
| GIF | CPU (WIC) | |
| HEIF/AVIF/JXL | CPU (WIC) | |

## Сборка

```bash
# 1. Клонировать репозиторий
git clone https://github.com/Sucotasch/AntiDuplPlus.git
cd AntiDuplPlus

# 2. Установить vcpkg (если не установлен)
git clone https://github.com/Microsoft/vcpkg.git
cd vcpkg && bootstrap-vcpkg.bat && vcpkg integrate install

# 3. Открыть решение в Visual Studio
start src\AntiDupl.sln

# 4. Собрать (Build → Build Solution)
# Зависимости загружаются автоматически через vcpkg
```

## Лицензия

MIT License — см. [LICENSE](LICENSE) (наследуется от оригинального AntiDupl.NET).

## Благодарности

- [AntiDupl.NET](https://github.com/ermig1979/AntiDupl) — оригинальный проект
- [NVIDIA nvJPEG](https://developer.nvidia.com/nvjpeg) — GPU декодирование JPEG
- [CUDA Toolkit](https://developer.nvidia.com/cuda-toolkit) — GPU вычисления
- [vcpkg](https://github.com/Microsoft/vcpkg) — управление C++ зависимостями
