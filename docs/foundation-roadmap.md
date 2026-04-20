# Foundation Roadmap

## Этап 0. Уже сделано

- solution и git-friendly структура репозитория
- модульный shell без ADB-команд
- tool registry и workspace tabs
- базовые placeholder-модули
- политика хранения runtime-данных вне директории установки
- папки под assets, styles и installer

## Этап 1. Device Foundation

Цель: подготовить реальную рабочую основу для ТВ до добавления тяжёлых команд.

- сделать saved TV inventory
- добавить экран редактирования сетевых endpoint'ов
- ввести global selection state и pinned selection для вкладок
- определить жизненный цикл device presence: `Known / Reachable / Connected / Offline`
- решить, как будет выглядеть device rail в компактном окне

## Этап 2. Execution Foundation

Цель: добавить общий execution pipeline, но ещё не размазывать конкретные команды по UI.

- ввести `OperationRequest / OperationResult`
- сделать executor abstraction для single-device и multi-device fan-out
- определить статусные состояния выполнения
- предусмотреть cancel/timeout contract
- отделить UI-формы от самой ADB execution логики

## Этап 3. Первая полезная вертикаль

Рекомендую первой довести до конца именно одну простую вертикаль, а не распыляться.

Порядок:

1. device inventory
2. quick actions: power / reconnect / restart app
3. packages explorer
4. display settings

## Этап 4. Streaming Layer

- logcat session
- CPU / memory live metrics
- bounded buffers и virtualization
- честный disconnect UX без auto-reconnect магии

## Этап 5. Productivity Layer

- favorites
- presets
- command palette
- shareable presets
- simple workflows

## Этап 6. Packaging

- WiX-based MSI
- нормальный install path
- uninstall binaries
- отдельный cleanup local app data по явному решению
