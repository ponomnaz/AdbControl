# AdbControl

Windows-only desktop workbench на `C# / .NET 10 / WPF` для работы с Android TV через ADB.

Это уже не пустой архитектурный каркас. В репозитории есть рабочий shell, модульные инструменты, локальная APK-библиотека, сетевое подключение устройств, live `Logcat`, `Top` и журнал ADB-команд.

## Что уже умеет приложение

- `Устройства` — список подключённых ТВ, multi-select, псевдонимы, быстрые действия
- `Подключение` — поиск ТВ в локальной сети, ручной ввод IP, `connect / disconnect`
- `APK` — локальная библиотека APK, установка на одно или несколько устройств, просмотр и удаление пакетов на ТВ
- `Logcat` — live-вывод для выбранного устройства, фильтр, поиск, очистка экрана и буфера
- `Top` — снимок нагрузки процессов выбранного устройства
- `Журнал` — трассировка ADB-команд, времени выполнения, `stdout`, `stderr` и ошибок

## Как это устроено

- один WPF-процесс и один shell с вкладками
- модульный монолит, а не runtime-plugin system
- каждый экран регистрируется как `IToolModule -> ToolRegistration`
- вкладки и открытие tools управляются через `WorkspaceService`
- все ADB-команды проходят через `AdbProcessRunner`
- ADB-команды и их результат попадают в `CommandTraceJournal`
- runtime-данные приложения живут в `%LOCALAPPDATA%\\AdbControl`, а не рядом с `exe`

Подробная схема лежит в [docs/architecture-overview.md](docs/architecture-overview.md).

## Требования

- Windows
- `.NET 10 SDK`
- `adb.exe` должен быть доступен в `PATH`

Если `adb` не найден, это отразится в `Журнале` и команды устройств работать не будут.

## Сборка и запуск

```powershell
dotnet build .\AdbControl.sln -m:1
dotnet run --project .\src\AdbControl.App\AdbControl.App.csproj
```

Если приложение уже запущено и держит DLL:

```powershell
Get-Process AdbControl.App -ErrorAction SilentlyContinue | Stop-Process -Force
```

Подробности и частые сценарии запуска: [docs/build-run.md](docs/build-run.md).

## Runtime-данные

Рабочий каталог приложения:

```text
%LOCALAPPDATA%\AdbControl
```

Основные подпапки:

- `settings`
- `presets`
- `layout`
- `logs`
- `cache`
- `apks`
- `sessions`
- `exports`

Полезные файлы:

- `%LOCALAPPDATA%\AdbControl\logs\command-trace.jsonl`
- `%LOCALAPPDATA%\AdbControl\logs\app-crash.log`
- `%LOCALAPPDATA%\AdbControl\settings\device-aliases.json`
- `%LOCALAPPDATA%\AdbControl\settings\apk-library.json`

## Документация

- [docs/architecture-overview.md](docs/architecture-overview.md) — текущая архитектура и runtime-модель
- [docs/repo-structure.md](docs/repo-structure.md) — структура solution и назначение проектов
- [docs/foundation-roadmap.md](docs/foundation-roadmap.md) — актуальный roadmap после поднятия фундамента
- [docs/build-run.md](docs/build-run.md) — сборка, запуск и типовые проблемы
- [local/README.md](local/README.md) — локальные проектные инструкции
- [local/design-system.md](local/design-system.md) — зафиксированная дизайн-система
- [local/installer.md](local/installer.md) — заметки по installer / uninstall
- [assets/README.md](assets/README.md) — куда класть иконки и другие ассеты

## Installer

Сборка инсталлера:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1
```

Открыть мастер установки сразу после сборки:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1 -OpenMsi
```

Если нужен мастер с выбором папки установки и ярлыка на рабочем столе, открывай:

```text
artifacts\installer\win-x64\AdbControl.Setup.msi
```

`setup.exe` больше не нужен для обычной установки и не является основным сценарием.

## Ограничения текущего состояния

- автоматических тестов пока нет
- dependency graph собирается вручную в `App.xaml.cs`, без DI-контейнера
- приложение зависит от установленного `adb` в `PATH`
- shell и UX ещё активно шлифуются, особенно в streaming-инструментах
