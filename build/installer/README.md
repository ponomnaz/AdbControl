# Installer Layer

В этой зоне лежит инсталлятор на `Inno Setup`.

## Что здесь есть

- `inno/AdbControl.iss` — скрипт Inno Setup
- `scripts/Build-Installer.ps1` — publish приложения + сборка инсталлятора
- `scripts/Clean-LocalAppData.ps1` — ручная очистка `%LOCALAPPDATA%\AdbControl` (не часть установки/удаления)

## Что делает installer

- ставит приложение в `Program Files\AdbControl`
- создаёт ярлык в меню "Пуск"
- по желанию пользователя (чекбокс) создаёт ярлык на рабочем столе
- регистрирует запись в "Программы и компоненты" со штатным uninstall

## Что делает uninstall

- удаляет установленные файлы и папку установки
- удаляет ярлыки
- удаляет рабочие данные приложения `%LOCALAPPDATA%\AdbControl` (логи, кэш, алиасы устройств и т.п.)

## Требования для сборки

- [Inno Setup 6](https://jrsoftware.org/isdl.php) — должен быть установлен (предоставляет `ISCC.exe`)
- .NET SDK (для `dotnet publish`)

## Сборка

Из корня репозитория:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1
```

Результат:

```text
artifacts\installer\win-x64\AdbControl-Setup.exe
```

Это единственный файл, который нужно отдавать пользователю.

## Параметры сборки

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1 `
  -Version 0.1.0 `
  -Runtime win-x64 `
  -Configuration Release
```

## Сразу запустить установщик после сборки

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1 -Run
```
