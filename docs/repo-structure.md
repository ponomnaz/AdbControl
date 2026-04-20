# Структура Репозитория

## Корень

- `AdbControl.sln` — основная solution
- `assets/` — иконки, брендинг, изображения и installer-ассеты
- `build/installer/` — место под installer/uninstall артефакты
- `docs/` — общая документация по архитектуре и развитию
- `local/` — локальные проектные инструкции для разработки и поставки
- `src/` — код приложения

## src

- `AdbControl.Core` — доменные типы и базовые сущности
- `AdbControl.Application` — orchestration, tool catalog, workspace state, shared UI-independent state
- `AdbControl.Infrastructure` — файловое хранение, локальные пути, внешние адаптеры
- `AdbControl.Shell` — shell window, viewmodels и **глобальные стили**
- `AdbControl.Tools.Home` — стартовый workspace-модуль
- `AdbControl.Tools.Devices` — базовый модуль device workbench
- `AdbControl.Tools.Library` — каталог подключенных tools
- `AdbControl.App` — composition root и startup

## Где править стили

Все глобальные shell-стили и общие ресурсы должны жить в:

- `src/AdbControl.Shell/Styles/Colors.xaml`
- `src/AdbControl.Shell/Styles/Metrics.xaml`
- `src/AdbControl.Shell/Styles/Typography.xaml`
- `src/AdbControl.Shell/Styles/Panels.xaml`
- `src/AdbControl.Shell/Styles/Buttons.xaml`
- `src/AdbControl.Shell/Styles/Inputs.xaml`
- `src/AdbControl.Shell/Styles/Lists.xaml`
- `src/AdbControl.Shell/Styles/Tabs.xaml`
- `src/AdbControl.Shell/Styles/Icons.xaml`
- `src/AdbControl.Shell/Styles/ShellTheme.xaml`

Tool-specific DataTemplates пока живут внутри самих модулей в `Themes/`.

## Где класть ресурсы

- `assets/icons/` — app icons, toolbar icons, tray icons
- `assets/images/` — изображения, мокапы, иллюстрации
- `assets/branding/` — логотипы, splash и brand assets
- `build/installer/assets/` — ассеты, нужные только installer-слою

## Где будут данные приложения

Runtime-данные не должны жить в директории установки.
Рабочий корень данных приложения:

```text
%LOCALAPPDATA%\AdbControl
```

Это относится к логам, кэшу, пресетам, layout state, экспортам и временным session-файлам.
