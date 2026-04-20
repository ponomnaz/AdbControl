# Installer Layer

Эта зона зарезервирована под installer/uninstall инфраструктуру.

## План

- `wix/` — будущий WiX-проект или набор `.wxs` файлов
- `assets/` — installer-only ресурсы
- `scripts/` — вспомогательные скрипты, например cleanup local data

## Базовая политика

- приложение устанавливается в `Program Files`
- приложение не пишет runtime-данные в install directory
- runtime-данные живут в `%LOCALAPPDATA%\AdbControl`
- uninstall удаляет бинарники и installer-артефакты
- удаление пользовательских локальных данных — отдельное, явное действие
