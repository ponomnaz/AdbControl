# Installer Layer

В этой зоне лежит foundation для Windows-инсталлятора на `WiX Toolset`.

## Что здесь есть

- `wix/AdbControl.Installer.wixproj` — WiX SDK-проект
- `wix/AdbControl.Bundle.wixproj` — bundle-проект для `setup.exe` (опционально)
- `wix/Package.wxs` — авторинг MSI
- `wix/Bundle.wxs` — авторинг bootstrapper exe
- `scripts/Build-Installer.ps1` — publish приложения + build MSI
- `scripts/Clean-LocalAppData.ps1` — явная очистка `%LOCALAPPDATA%\AdbControl`

## Что делает installer

- ставит приложение в `Program Files`
- создаёт запись для uninstall
- не пишет runtime-данные в install directory
- не удаляет пользовательские данные молча

## Что не делает uninstall по умолчанию

- не удаляет логи
- не удаляет локальные настройки
- не удаляет пресеты и экспортированные файлы

Для этого есть отдельный cleanup script.

## Сборка

Из корня репозитория:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1
```

Обычный сценарий:

- запускаешь одну команду сборки
- получаешь один понятный мастер установки

Если нужен мастер установки с:

- выбором директории
- выбором ярлыка на рабочем столе

открывай именно этот файл:

```text
artifacts\installer\win-x64\AdbControl.Setup.msi
```

`setup.exe` не нужен для обычной установки. Он собирается только если явно попросить:

```text
artifacts\installer\win-x64\AdbControl.Setup.exe
```

## Открыть мастер сразу после сборки

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1 -OpenMsi
```

## Опциональный bootstrapper

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1 -IncludeBootstrapper
```
