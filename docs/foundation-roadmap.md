# Foundation Roadmap

## Что уже поднято

Фундамент больше не пустой. В проекте уже есть:

- рабочий WPF shell с левой навигацией и tab-workspace
- tool-model через `IToolModule`, `ToolRegistration`, `ToolCatalog`
- shared state для устройств, aliases и command trace
- единый ADB execution path через `AdbProcessRunner`
- локальная APK-библиотека
- `Устройства`, `Подключение`, `APK`, `Logcat`, `Top`, `Журнал`
- политика хранения runtime-данных в `%LOCALAPPDATA%\AdbControl`

То есть следующий этап — не “строить каркас”, а стабилизировать и расширять уже существующий workbench.

## Ближайший практический roadmap

### Этап 1. Stabilize current tools

Цель: довести существующие инструменты до предсказуемого рабочего состояния.

- дополировать `Logcat`:
  - presets фильтров
  - pause/freeze без остановки сессии
  - экспорт выделенного или всего окна
- дополировать `APK`:
  - лучшее UX-разделение install / installed packages
  - честные bulk-операции и статусы по устройствам
  - ещё меньше лишнего текста и промежуточных состояний
- унифицировать поведение selection / scroll / empty-states между модулями

### Этап 2. Productivity layer

Цель: ускорить повторяющиеся сценарии.

- избранные действия
- простые пресеты
- command palette
- быстрый вход в частые действия по выбранному ТВ

### Этап 3. Device tooling expansion

Цель: расширять охват без переписывания архитектуры.

Приоритетные кандидаты:

- просмотр/удаление кэша
- display tools (`wm size`, `wm density`)
- shell/file tools
- richer package actions
- дополнительные диагностические экраны

### Этап 4. Packaging and distribution

Цель: превратить dev-workbench в нормально устанавливаемое приложение.

- MSI / installer
- uninstall flow
- cleanup `%LOCALAPPDATA%\AdbControl` только по явному решению
- решение по `adb`: внешний `PATH` или bundled runtime

## Что важно не ломать по дороге

- не тащить ADB-логику в UI напрямую
- не плодить специальные пути выполнения мимо `AdbProcessRunner`
- не превращать shell в набор несвязанных исключений
- не смешивать локальные APK и device packages в одну абстракцию

## Что пока сознательно не делаем

- cross-platform
- runtime plugin system
- heavy workflow engine
- полноценную test-infrastructure

Это не запреты навсегда, а сознательный сдвиг сложности вправо, пока продукт ещё быстро меняет форму.
