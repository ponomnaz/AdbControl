# Design System

## 1. Принцип

AdbControl — это **инженерный инструмент, но не мрачный**.

Что это значит:

- не игрушка
- не дизайнерская витрина
- не псевдо-консоль
- аккуратный
- спокойный
- быстрый
- читаемый

Главный приоритет:

- скорость > красота
- читаемость > эффекты
- консистентность > креатив

## 2. Реализация в коде

Глобальные стили лежат в:

- `src/AdbControl.Shell/Styles/Colors.xaml`
- `src/AdbControl.Shell/Styles/Metrics.xaml`
- `src/AdbControl.Shell/Styles/Typography.xaml`
- `src/AdbControl.Shell/Styles/Panels.xaml`
- `src/AdbControl.Shell/Styles/Buttons.xaml`
- `src/AdbControl.Shell/Styles/Inputs.xaml`
- `src/AdbControl.Shell/Styles/Lists.xaml`
- `src/AdbControl.Shell/Styles/Tabs.xaml`
- `src/AdbControl.Shell/Styles/Icons.xaml`
- `src/AdbControl.Shell/Styles/ShellTheme.xaml`

Правило:

- глобальные UI tokens и базовые контролы меняем только здесь
- tool-модули используют эти ресурсы, а не хардкодят свои цвета и радиусы

## 3. Цветовая система

### Основные

- `AccentPrimary` = `#10B981`
- `AccentHover` = `#059669`
- `AccentSoft` = `#34D399`

### Фоны

- `BackgroundPrimary` = `#F7F9FC`
- `BackgroundSecondary` = `#EEF2F7`

### Поверхности

- `SurfacePrimary` = `#FFFFFF`
- `SurfaceAlt` = `#F1F5F9`

### Границы

- `BorderDefault` = `#E2E8F0`

### Текст

- `TextPrimary` = `#0F172A`
- `TextSecondary` = `#475569`
- `TextDisabled` = `#94A3B8`

### Состояния выбора

- `AccentSelection` = `#DCFCE7`

### Темная тема

В MVP не включаем, но токены зарезервированы:

- `DarkBackground` = `#0F172A`
- `DarkSurface` = `#111827`
- `DarkText` = `#E5E7EB`

## 4. Типографика

Шрифт по умолчанию:

- `Segoe UI`

Если позже понадобится явный брендовый UI-проход, можно рассмотреть `Inter`, но не раньше.

Размеры:

- `12` — мета / подписи
- `13` — строки таблиц и плотные данные
- `14` — основной UI
- `16` — заголовки секций
- `20` — заголовки страниц

Правило:

- никаких огромных заголовков
- плотный UI
- контент важнее декоративности

## 5. Радиусы

- `4` — input, button, мелкие interactive элементы
- `6` — карточки и list rows
- `8` — панели
- `12` — диалоги

Запрещено:

- mobile-style giant rounding
- pill everything

## 6. Глубина

Тени минимальные.

- `ShadowSm` — почти плоская глубина
- `ShadowMd` — только для более поднятых контейнеров, если реально нужно

Правило:

- интерфейс плоский
- но не мёртвый

## 7. Компоненты

Уже зафиксированы базовые стили:

- `PrimaryButtonStyle`
- `SecondaryButtonStyle`
- `GhostButtonStyle`
- `ToolNavigationButtonStyle`
- `TextBoxInputStyle`
- `PanelSurfaceStyle`
- `PanelSurfaceAltStyle`
- `PanelInsetStyle`
- `WorkbenchListBoxStyle`
- `WorkbenchListBoxItemStyle`
- глобальный стиль вкладок

## 8. Layout

Базовая схема shell:

```text
[ Devices ] | [ Tabs / Content ] | [ Context ]
             [ Bottom: Logs / Activity ]
```

Рабочие ориентиры:

- left rail: `220–260px`
- context pane: `260–320px`
- центральная зона отдается вкладкам и контенту

## 9. Иконки

Источник логотипа:

- `assets/icons/icon_adb.svg`

Сгенерированные продуктовые форматы:

- `assets/icons/icon_adb.png`
- `assets/icons/icon_adb.ico`

Для UI иконография должна идти в сторону:

- Fluent Icons
- Tabler Icons

Правило:

- outline / semi-filled
- без пестроты
- без визуального шума

## 10. Состояния

Всегда проектировать состояния:

- loading
- empty
- error
- selected
- hover
- disabled

Их реализация должна быть компактной, без драматичных экранов и “красных простыней”.

## 11. Что делать дальше

Перед следующими экранами не придумывать новый стиль заново.

Новые элементы должны:

1. сначала опираться на существующие tokens/components
2. если не хватает общего паттерна — расширять глобальные styles
3. только потом использоваться в feature-модуле

## 12. Чего не делать

- не плодить hardcoded цвета в tool views
- не делать одноразовый MVP-UI с code-behind логикой
- не смешивать глобальные shell styles и feature-specific hacks
- не делать “красиво”, если это ухудшает плотность и читаемость
