# Installer / Uninstall

## Что принимаем как правило

- установщик кладёт приложение в `Program Files`
- приложение не пишет рядом с exe ничего, кроме того, что реально относится к установке
- все рабочие данные приложения идут в `%LOCALAPPDATA%\AdbControl`

## Рекомендуемый стек

Для Windows-only десктопа я бы брал `WiX Toolset` и собирал `MSI`.

Почему:

- нормальная установка в `Program Files`
- штатный uninstall
- понятная модель ярлыков, upgrade и versioning
- не нужно изобретать собственный установщик

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

## Как подходить к реализации installer

1. Завести `build/installer/wix/`
2. Подключить product icon из `assets/icons/`
3. Настроить install path
4. Настроить shortcuts
5. Настроить uninstall
6. Отдельно решить, предлагать ли cleanup local data чекбоксом или оставлять как отдельный script
