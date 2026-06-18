# Installer / Uninstall

## Что принимаем как правило

- установщик кладёт приложение в `Program Files`
- приложение не пишет рядом с exe ничего, кроме того, что реально относится к установке
- все рабочие данные приложения идут в `%LOCALAPPDATA%\AdbControl`

## Что сейчас принято в проекте

В репозитории выбран `Inno Setup`.

Почему:

- один скрипт `.iss` → один `setup.exe`
- встроенный мастер: выбор папки, чекбокс "ярлык на рабочем столе"
- штатная регистрация в "Программы и компоненты" и штатный uninstall
- не нужно собирать самодельный setup/uninstall host

## Что уже делает текущий installer

- ставит приложение в `Program Files`
- регистрирует uninstall
- не пишет runtime-данные рядом с exe
- создаёт ярлык на рабочем столе (по чекбоксу при установке)
- создаёт ярлык в меню Пуск

## Что должен делать uninstall

- удалить установленные бинарники и папку установки
- убрать shortcuts
- убрать installer registration
- удалить `%LOCALAPPDATA%\AdbControl` (логи, кэш, алиасы устройств, пресеты) — полная очистка

Это настроено через `[UninstallDelete]` в `build/installer/inno/AdbControl.iss`.

Отдельный ручной cleanup script (на случай, если нужно сбросить данные приложения без удаления самого приложения):

- [build/installer/scripts/Clean-LocalAppData.ps1](../build/installer/scripts/Clean-LocalAppData.ps1)

## Как собрать installer

Из корня репозитория:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1
```

Скрипт по умолчанию:

1. делает `dotnet publish` приложения (self-contained, win-x64)
2. собирает установщик через Inno Setup (`ISCC.exe`)
3. кладёт результат в `artifacts\installer\win-x64\AdbControl-Setup.exe`

Требуется установленный [Inno Setup 6](https://jrsoftware.org/isdl.php).

## Какой файл запускать

```text
artifacts\installer\win-x64\AdbControl-Setup.exe
```

Это единственный файл, который нужно отдавать пользователю. Мастер сам спросит папку установки и предложит чекбокс "Создать ярлык на рабочем столе".

## Открыть мастер сразу после сборки

```powershell
powershell -ExecutionPolicy Bypass -File .\build\installer\scripts\Build-Installer.ps1 -Run
```

## Что важно помнить

- для сборки нужен установленный Inno Setup (ISCC.exe), NuGet не требуется
- runtime-данные приложения идут в `%LOCALAPPDATA%\AdbControl`
- uninstall удаляет локальные данные автоматически
