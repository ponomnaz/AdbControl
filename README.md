# AdbControl

Windows-only desktop workbench на `C# / .NET 10 / WPF` для работы с Android TV через ADB.  
Сейчас в репозитории уже есть архитектурный фундамент: shell, workspace-вкладки, tool-модули, каталог инструментов, дизайн-система и общая схема хранения локальных данных приложения.

## Что уже подготовлено

- `AdbControl.sln` с разделением на `Core`, `Application`, `Infrastructure`, `Shell`, `Tools.*`, `App`
- базовый WPF shell с вкладками, device rail, context pane и нижней activity-зоной
- модульная регистрация tools без runtime plugin system
- зафиксированная дизайн-система в `src/AdbControl.Shell/Styles/`
- логотип приложения в `assets/icons/` и подключение его в продукт
- документы по структуре, roadmap и installer/uninstall

## Как собрать

```powershell
dotnet build AdbControl.sln
```

Если сборка запускается в ограниченной среде или sandbox, можно использовать:

```powershell
dotnet build AdbControl.sln -m:1
```

## Где что лежит

- [docs/repo-structure.md](docs/repo-structure.md) — структура репозитория
- [docs/foundation-roadmap.md](docs/foundation-roadmap.md) — roadmap по наращиванию фич
- [local/README.md](local/README.md) — локальные проектные инструкции
- [local/design-system.md](local/design-system.md) — зафиксированная дизайн-система
- [local/installer.md](local/installer.md) — как подходить к installer/uninstall
- [local/what-next.md](local/what-next.md) — ближайшие следующие шаги
- [assets/README.md](assets/README.md) — куда класть иконки и другие ассеты

## Логотип приложения

Источник:

- `assets/icons/icon_adb.svg`

Сгенерированные продуктовые форматы:

- `assets/icons/icon_adb.png`
- `assets/icons/icon_adb.ico`

## Политика хранения данных приложения

Приложение **не должно** писать runtime-данные в директорию установки.  
Все создаваемые приложением файлы должны жить в:

```text
%LOCALAPPDATA%\AdbControl
```

Сейчас для этого уже заложены директории:

- `settings`
- `presets`
- `layout`
- `logs`
- `cache`
- `sessions`
- `exports`

Эта политика закреплена в [AppDataPaths.cs](src/AdbControl.Infrastructure/Persistence/AppDataPaths.cs).
