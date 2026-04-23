# Repo Structure

## Корень репозитория

- `AdbControl.sln` — основная solution
- `src/` — код приложения
- `docs/` — общая проектная документация
- `local/` — локальные заметки команды по дизайну и installer-процессу
- `assets/` — иконки, брендовые и продуктовые ассеты
- `build/installer/` — заготовки под installer / uninstall

## Проекты в `src/`

### Базовые слои

- `AdbControl.Core`
  - базовые типы домена и tool metadata
  - не должен знать про WPF, файловую систему и `adb`

- `AdbControl.Application`
  - shared state и orchestration
  - `WorkspaceService`, `ToolCatalog`, `DeviceInventoryState`, `CommandTraceJournal`
  - контракты для device/apk/logcat/top сервисов

- `AdbControl.Infrastructure`
  - file persistence
  - runtime data paths
  - исполнение ADB-команд
  - сетевой discovery и адаптеры к внешней среде

- `AdbControl.Shell`
  - главное окно
  - shell viewmodels
  - глобальные XAML-стили и тема

- `AdbControl.App`
  - startup
  - composition root
  - загрузка ресурсных словарей
  - создание shell window

### Feature-модули

- `AdbControl.Tools.Devices`
  - `Устройства`
  - `Подключение`

- `AdbControl.Tools.Apk`
  - локальная библиотека APK
  - установка APK на ТВ
  - просмотр и удаление пакетов на выбранных ТВ

- `AdbControl.Tools.Logcat`
  - live `logcat`
  - фильтр, поиск, очистка экрана и буфера

- `AdbControl.Tools.Top`
  - snapshot `top`
  - форматирование вывода для чтения внутри UI

- `AdbControl.Tools.CommandLog`
  - ADB command trace
  - просмотр `stdout`, `stderr`, ошибок и времени выполнения

### Служебные модули

- `AdbControl.Tools.Home`
  - стартовый экран
  - сейчас не используется как основной пользовательский вход

- `AdbControl.Tools.Library`
  - внутренний каталог доступных tools
  - не является частью основного пользовательского UX

## Где править что

### Shell и общие стили

Глобальная тема лежит в `src/AdbControl.Shell/Styles/`:

- `Colors.xaml`
- `Metrics.xaml`
- `Typography.xaml`
- `Panels.xaml`
- `Buttons.xaml`
- `Inputs.xaml`
- `Lists.xaml`
- `Tabs.xaml`
- `Icons.xaml`
- `ShellTheme.xaml`

Если меняется общий визуальный язык приложения, начинать нужно отсюда.

### Tool-specific templates

Если меняется отображение конкретного модуля, смотреть его `Themes/` и `Views/`.

Примеры:

- `src/AdbControl.Tools.Apk/Themes/ApkTemplates.xaml`
- `src/AdbControl.Tools.Logcat/Themes/LogcatTemplates.xaml`
- `src/AdbControl.Tools.Top/Themes/TopTemplates.xaml`

### Startup и graph зависимостей

Главная точка сборки зависимостей:

- `src/AdbControl.App/App.xaml.cs`

Если добавляется новый shared-service или новый tool-модуль, почти наверняка нужно менять этот файл.

### Runtime data policy

Главная точка политики хранения:

- `src/AdbControl.Infrastructure/Persistence/AppDataPaths.cs`

Рабочие каталоги создаются через:

- `src/AdbControl.Infrastructure/Persistence/LocalWorkspaceStorage.cs`

## Assets и брендинг

- `assets/icons/` — app icon и связанные форматы
- `assets/images/` — изображения, мокапы, иллюстрации
- `assets/branding/` — брендовые материалы

Installer-specific assets лучше держать отдельно в `build/installer/`, а не смешивать с продуктовым UI.

## Документация

- `docs/architecture-overview.md` — архитектурная модель и runtime
- `docs/repo-structure.md` — этот документ
- `docs/foundation-roadmap.md` — что развивать дальше
- `docs/build-run.md` — как собирать и запускать

Локальные заметки:

- `local/design-system.md`
- `local/installer.md`
- `local/README.md`

## Runtime-данные приложения

Они не должны жить в директории установки.

Рабочий корень:

```text
%LOCALAPPDATA%\AdbControl
```

Текущая структура:

- `settings`
- `presets`
- `layout`
- `logs`
- `cache`
- `apks`
- `sessions`
- `exports`
