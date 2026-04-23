# Architecture Overview

## Что это за приложение сейчас

`AdbControl` — desktop workbench для Android TV через ADB.  
Текущая форма системы: **Windows-only modular monolith** на `WPF`, где каждый экран оформлен как отдельный tool-модуль, а shell собирает их в единое приложение с вкладками.

Это важно: архитектура уже вышла из стадии “пустой shell”. В коде есть рабочие инструменты `Устройства`, `Подключение`, `APK`, `Logcat`, `Top` и `Журнал`.

## Архитектурная модель

### 1. Один процесс, один shell, много tools

Приложение работает как один WPF-процесс:

- `AdbControl.App` — composition root и startup
- `AdbControl.Shell` — окно, навигация, общие стили
- `AdbControl.Tools.*` — feature-модули

Здесь нет runtime-plugin system, контейнера плагинов или IPC между модулями.  
Это осознанный выбор: сейчас выгоднее держать систему как модульный монолит с явными границами, чем преждевременно усложнять её.

### 2. Tool-based composition

Основная единица расширения — не “команда”, а **tool**.

Цепочка такая:

1. модуль реализует `IToolModule`
2. модуль отдаёт одну или несколько `ToolRegistration`
3. `ToolCatalog` собирает все registrations
4. `WorkspaceService` открывает нужный tool как вкладку
5. `ToolActivationContext` передаёт tool-экрану все общие сервисы и shared state

Это даёт сильную точку расширения:

- можно добавлять новые экраны без переписывания shell
- можно открывать tools из навигации или из других tools
- можно переиспользовать общие application-сервисы без глобальных singleton-хака

### 3. Явная composition root, без DI-контейнера

Сейчас зависимости собираются вручную в [App.xaml.cs](../src/AdbControl.App/App.xaml.cs).

Startup делает следующее:

1. создаёт `LocalWorkspaceStorage` и рабочие каталоги
2. подключает crash logging
3. инициализирует shared state и persistent stores
4. создаёт ADB-сервисы и file-based infrastructure
5. регистрирует tool modules
6. собирает `ToolCatalog` и `WorkspaceService`
7. создаёт `ShellViewModel` и показывает `ShellWindow`

Это проще, чем заводить полноценный DI-контейнер, и пока хорошо соответствует размеру проекта.  
Минус у подхода тоже есть: composition root уже стал важным связующим файлом, и дальше его нужно держать аккуратным.

## Слои solution

### `AdbControl.Core`

Минимальный слой с базовыми типами:

- `TvDeviceProfile`
- `ToolDescriptor`
- `ToolCategory`
- `ToolTargetScope`
- `ModuleDescriptor`

Этот слой не знает про WPF и не должен знать про file storage или `adb`.

### `AdbControl.Application`

Слой orchestration и shared state:

- `WorkspaceService`
- `ToolCatalog`
- `ToolActivationContext`
- `DeviceInventoryState`
- `DeviceAliasCatalog`
- `CommandTraceJournal`
- контракты `IApkDeploymentService`, `IDeviceLogcatService`, `IDeviceTopService` и т.д.

Это главный слой приложения. Он не должен зависеть от WPF-конкретики.

### `AdbControl.Infrastructure`

Адаптеры к реальному миру:

- ADB runner и device services
- поиск устройств в сети
- file-based persistence
- runtime path policy

Ключевые классы:

- `AdbProcessRunner`
- `AdbConnectionService`
- `AdbDeviceActionService`
- `AdbApkDeploymentService`
- `AdbApkDevicePackageService`
- `AdbLogcatService`
- `AdbTopService`
- `LocalApkLibraryService`
- `CommandTraceFileStore`
- `DeviceAliasFileStore`

### `AdbControl.Shell`

Общее окно приложения и визуальная система:

- `ShellWindow`
- `ShellViewModel`
- глобальные XAML styles

### `AdbControl.Tools.*`

Feature-модули с собственными VM, View и templates.

Текущие рабочие модули:

- `Tools.Devices`
- `Tools.Apk`
- `Tools.Logcat`
- `Tools.Top`
- `Tools.CommandLog`

Служебные:

- `Tools.Home`
- `Tools.Library`

## Runtime-модель

### Device inventory

Общее состояние устройств хранится в `DeviceInventoryState`.

Сейчас там два основных набора:

- `KnownDevices` — то, что приложение считает известными/доступными устройствами
- `SelectedDevices` — текущий выбор для multi-device сценариев

`DeviceInventoryState` — shared state между tools, а не private state одного экрана.

### Device aliases

Псевдонимы устройств хранятся отдельно через:

- `DeviceAliasCatalog`
- `DeviceAliasFileStore`

Это позволяет показывать alias в `Устройствах`, `Подключении`, `APK`, `Logcat`, `Top` и `Журнале`, не дублируя логику в каждом модуле.

### Command trace

Все ADB-команды проходят через `AdbProcessRunner`, который пишет их в `CommandTraceJournal`.

`CommandTraceJournal`:

- хранит live-entries в памяти
- пишет записи в файл
- держит unread error count
- питает экран `Журнал`

Это важный архитектурный инвариант проекта: tool вызывает не shell и не UI, а application/infrastructure-сервис, а трассировка происходит в одном месте.

### Long-running sessions

Streaming-сценарии вынесены в отдельные сервисы/сессии:

- `IDeviceLogcatSession`
- `IDeviceTopService`

Это правильно отделяет live-поток от обычных one-shot команд.

## Как идут ADB-команды

### One-shot команды

Путь выполнения:

`Tool ViewModel -> Application contract -> Infrastructure service -> AdbProcessRunner -> adb.exe`

Примеры:

- connect / disconnect
- power / reboot / force-stop
- install / uninstall
- `pm list packages`

### Streaming

Для `Logcat` используется отдельная сессия процесса и поток строк.  
UI получает уже готовые строки и сам решает, как их буферизовать и показывать.

Для `Top` пока используется snapshot-модель, а не бесконечный терминальный сеанс.

## Хранение данных

Приложение не пишет данные рядом с `exe`.

Рабочий корень:

```text
%LOCALAPPDATA%\AdbControl
```

Назначение подпапок:

- `settings` — aliases, library metadata и другие пользовательские настройки
- `presets` — задел под пресеты
- `layout` — состояние shell/layout
- `logs` — command trace и crash logs
- `cache` — временный runtime cache
- `apks` — локально импортированные APK
- `sessions` — временные session-данные
- `exports` — файлы, которые пользователь может забирать наружу

## Сильные стороны текущей архитектуры

- понятные границы между shell, application и infra
- tool-model хорошо подходит под рост числа экранов
- ADB execution централизован
- runtime data policy уже зафиксирована правильно
- новые feature-модули можно добавлять без ломки всего приложения

## Ограничения и текущий техдолг

### 1. Нет автоматических тестов

Сейчас это ручной продуктовый цикл. Для ранней стадии допустимо, но риск регрессий уже растёт.

### 2. Много ручной WPF-логики

В некоторых местах есть code-behind для:

- сброса выделения
- special-case scroll behavior
- работы с focus/editing

Это не катастрофа, но нужно следить, чтобы такие куски не стали доминировать над системой.

### 3. `App.xaml.cs` уже становится важной точкой сборки

Пока это нормально, но дальше composition root нужно будет держать под контролем и не превращать в свалку регистрации всего подряд.

### 4. `adb` пока внешний

Система сейчас завязана на `adb.exe` в `PATH`.  
Для dev-инструмента это терпимо, но для установки “как продукт” позже может понадобиться bundled ADB или хотя бы явная настройка пути.

## Практический вывод

Текущая архитектура уже годится для наращивания реальных функций.  
Главная задача дальше — не “перепридумать всё”, а:

- стабилизировать UX и shell
- развивать tool-модули вертикалями
- удерживать ADB execution централизованным
- не размазывать device/package/logging-логику по UI
