# Installer / Uninstall

## Что принимаем как правило

- установщик кладёт приложение в `Program Files`
- приложение не пишет рядом с exe ничего, кроме того, что реально относится к установке
- все рабочие данные приложения идут в `%LOCALAPPDATA%\AdbControl`

## Что сейчас принято в проекте

В репозитории выбран `WiX Toolset` и `MSI`.

Почему:

- нормальная установка в `Program Files`
- штатный uninstall
- предсказуемые major upgrade
- не нужно собирать самодельный setup

## Что уже делает текущий installer

- ставит приложение в `Program Files`
- регистрирует uninstall
- не пишет runtime-данные рядом с exe
- создаёт ярлык на рабочем столе
- создаёт ярлык в меню Пуск
- собирает отдельный `setup.exe` поверх MSI

## Что должен делать uninstall

Минимум:

- удалить установленные бинарники
- убрать shortcuts
- убрать installer registration

По умолчанию не нужно автоматически удалять:

- логи
- пресеты
- layout
- экспортированные файлы

Для этого уже добавлен отдельный cleanup script:

- [build/installer/scripts/Clean-LocalAppData.ps1](../build/installer/scripts/Clean-LocalAppData.ps1)

## Как собрать installer

Из корня репозитория:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1
```

Скрипт по умолчанию:

1. делает `dotnet publish` приложения
2. собирает `MSI` через `WiX`
3. кладёт результат в `artifacts\installer\win-x64`

## Какой файл запускать

Если нужен нормальный мастер установки, где есть:

- выбор папки установки
- выбор создания ярлыка на рабочем столе

открывай именно:

```text
artifacts\installer\win-x64\AdbControl.Setup.msi
```

Это и есть основной установщик. Дополнительный `setup.exe` больше не нужен для обычной установки.

## Открыть мастер сразу после сборки

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1 -OpenMsi
```

## Если всё же нужен bootstrapper exe

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1 -IncludeBootstrapper
```

## Параметры сборки

Можно переопределить версию и runtime:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1 `
  -Version 0.1.0 `
  -Runtime win-x64 `
  -Configuration Release
```

## Что важно помнить

- для первой сборки нужен доступ к `NuGet`, потому что `WixToolset.Sdk` подтянется как пакет
- runtime-данные приложения по-прежнему идут в `%LOCALAPPDATA%\AdbControl`
- uninstall не удаляет локальные данные автоматически
