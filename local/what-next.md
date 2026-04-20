# Что Дальше

## Ближайший практический порядок

1. Доделать `Devices` модуль до реального inventory экрана
2. Добавить хранение saved TV endpoints в `settings`
3. Ввести execution pipeline без UI-командной мешанины
4. Сделать первый набор quick actions
5. После этого только идти в packages/logcat/display

## Что важно не делать раньше времени

- не добавлять plugin system
- не делать workflow engine
- не строить installer раньше, чем появится первая полезная вертикаль
- не тащить историю операций и аудит
- не скатываться в одноразовый MVP UI с code-behind и hardcoded стилями

## Что стоит закрепить сразу

- единый контракт для tool registration
- единый runtime data root в `%LOCALAPPDATA%\AdbControl`
- все новые глобальные стили класть в `src/AdbControl.Shell/Styles/`
- все новые иконки и картинки класть в `assets/`
- фичи добавлять постепенно, но поверх нормального MVVM-фундамента
