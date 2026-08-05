using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace AdbControl.Shell.Behaviors;

/// <summary>
/// Выделение в списках как в проводнике: обычный щелчок заменяет выделение,
/// Ctrl добавляет, Shift выделяет диапазон, Ctrl+A выделяет всё, протяжка по пустому
/// месту рисует рамку, щелчок по пустому месту снимает выделение.
///
/// Сам щелчок по строке обрабатывает штатный ListBox — здесь только то, чего в нём нет.
/// </summary>
public static class ListSelectionBehavior
{
    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.RegisterAttached(
            "SelectedItems",
            typeof(IList),
            typeof(ListSelectionBehavior),
            new PropertyMetadata(null, OnSelectedItemsChanged));

    /// <summary>
    /// Рамка выделения. В плотных списках без пустого места — например в логе —
    /// она мешает штатному протягиванию мышью, поэтому её можно выключить.
    /// </summary>
    public static readonly DependencyProperty IsMarqueeEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsMarqueeEnabled",
            typeof(bool),
            typeof(ListSelectionBehavior),
            new PropertyMetadata(true));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(ListSelectionBehavior),
            new PropertyMetadata(false));

    /// <summary>Подписка на привязанную коллекцию — храним, чтобы было что отцепить.</summary>
    private static readonly DependencyProperty BoundSelectionHandlerProperty =
        DependencyProperty.RegisterAttached(
            "BoundSelectionHandler",
            typeof(NotifyCollectionChangedEventHandler),
            typeof(ListSelectionBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty MarqueeProperty =
        DependencyProperty.RegisterAttached(
            "Marquee",
            typeof(MarqueeState),
            typeof(ListSelectionBehavior),
            new PropertyMetadata(null));

    public static IList? GetSelectedItems(DependencyObject dependencyObject)
    {
        return (IList?)dependencyObject.GetValue(SelectedItemsProperty);
    }

    public static void SetSelectedItems(DependencyObject dependencyObject, IList? value)
    {
        dependencyObject.SetValue(SelectedItemsProperty, value);
    }

    public static bool GetIsMarqueeEnabled(DependencyObject dependencyObject)
    {
        return (bool)dependencyObject.GetValue(IsMarqueeEnabledProperty);
    }

    public static void SetIsMarqueeEnabled(DependencyObject dependencyObject, bool value)
    {
        dependencyObject.SetValue(IsMarqueeEnabledProperty, value);
    }

    private static bool GetIsUpdating(DependencyObject dependencyObject)
    {
        return (bool)dependencyObject.GetValue(IsUpdatingProperty);
    }

    private static void SetIsUpdating(DependencyObject dependencyObject, bool value)
    {
        dependencyObject.SetValue(IsUpdatingProperty, value);
    }

    private static MarqueeState GetMarquee(ListBox listBox)
    {
        if (listBox.GetValue(MarqueeProperty) is MarqueeState state)
        {
            return state;
        }

        var created = new MarqueeState();
        listBox.SetValue(MarqueeProperty, created);
        return created;
    }

    private static void OnSelectedItemsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ListBox listBox)
        {
            return;
        }

        listBox.SelectionChanged -= OnListBoxSelectionChanged;
        listBox.SelectionChanged += OnListBoxSelectionChanged;

        // Привязка работает в обе стороны: выделение может измениться и снаружи —
        // например, когда его правит фоновая сверка устройств.
        if (e.OldValue is INotifyCollectionChanged oldSource &&
            listBox.GetValue(BoundSelectionHandlerProperty) is NotifyCollectionChangedEventHandler oldHandler)
        {
            oldSource.CollectionChanged -= oldHandler;
            listBox.ClearValue(BoundSelectionHandlerProperty);
        }

        if (e.NewValue is INotifyCollectionChanged newSource)
        {
            void Handler(object? _, NotifyCollectionChangedEventArgs __) => ApplyBoundSelection(listBox);

            newSource.CollectionChanged += Handler;
            listBox.SetValue(BoundSelectionHandlerProperty, (NotifyCollectionChangedEventHandler)Handler);
        }

        if (listBox.SelectionMode == SelectionMode.Single || !GetIsMarqueeEnabled(listBox))
        {
            return;
        }

        listBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        listBox.PreviewMouseMove -= OnPreviewMouseMove;
        listBox.PreviewMouseMove += OnPreviewMouseMove;
        listBox.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        listBox.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        listBox.LostMouseCapture -= OnLostMouseCapture;
        listBox.LostMouseCapture += OnLostMouseCapture;

        // Переключение вкладки посреди протяжки: без этого рамка уедет вместе со списком.
        listBox.Unloaded -= OnListBoxUnloaded;
        listBox.Unloaded += OnListBoxUnloaded;
    }

    /// <summary>
    /// Переносит выделение из привязанной коллекции в список. Правки точечные: полная
    /// пересборка сбрасывала бы якорь Shift-выделения и мигала бы подсветкой.
    /// </summary>
    private static void ApplyBoundSelection(ListBox listBox)
    {
        if (GetIsUpdating(listBox))
        {
            return;
        }

        var source = GetSelectedItems(listBox);
        if (source is null)
        {
            return;
        }

        SetIsUpdating(listBox, true);
        try
        {
            for (var index = listBox.SelectedItems.Count - 1; index >= 0; index--)
            {
                var item = listBox.SelectedItems[index];

                if (!source.Contains(item))
                {
                    listBox.SelectedItems.Remove(item);
                }
            }

            foreach (var item in source)
            {
                if (!listBox.SelectedItems.Contains(item))
                {
                    listBox.SelectedItems.Add(item);
                }
            }
        }
        finally
        {
            SetIsUpdating(listBox, false);
        }
    }

    private static void OnListBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || GetIsUpdating(listBox))
        {
            return;
        }

        var selectedItems = GetSelectedItems(listBox);
        if (selectedItems is null)
        {
            return;
        }

        SetIsUpdating(listBox, true);
        try
        {
            selectedItems.Clear();
            foreach (var item in listBox.SelectedItems)
            {
                selectedItems.Add(item);
            }
        }
        finally
        {
            SetIsUpdating(listBox, false);
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        // Клик по кнопке, полю ввода или полосе прокрутки к выделению отношения не имеет.
        if (IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        // Предыдущая рамка могла не закрыться — например, кнопку отпустили за пределами
        // окна. Снимаем её до того, как начнётся новая.
        EndMarquee(listBox);

        var isOnRow = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null;

        var state = GetMarquee(listBox);
        var origin = e.GetPosition(listBox);

        state.Origin = origin;
        state.LastPosition = origin;
        state.IsPending = true;
        state.IsActive = false;
        state.Range = null;

        // Точка начала запоминается не экранными координатами, а строкой, на которой
        // нажали: при прокрутке рамка должна ехать вместе с содержимым, как в проводнике.
        state.AnchorIndex = ResolveIndex(listBox, origin.Y);
        state.AnchorOffset = origin.Y - ResolveAnchorTop(listBox, state.AnchorIndex, origin.Y);

        // С Ctrl рамка добавляет к уже выделенному, без модификаторов — заменяет.
        state.BaseSelection = Keyboard.Modifiers == ModifierKeys.None
            ? []
            : [.. listBox.SelectedItems.Cast<object>()];

        // Нажатие на строку оставляем списку: одиночный щелчок должен выделять строку,
        // а рамка включится только если мышь действительно повели.
        if (isOnRow)
        {
            return;
        }

        // Пустое место: как в проводнике, выделение снимается сразу по нажатию.
        if (Keyboard.Modifiers == ModifierKeys.None && listBox.SelectedItems.Count > 0)
        {
            listBox.SelectedItems.Clear();
        }

        listBox.Focus();
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        var state = GetMarquee(listBox);

        // Кнопка отпущена, а события об этом не пришло — закрываем рамку здесь,
        // иначе она останется на экране до конца жизни окна.
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (state.IsPending || state.IsActive)
            {
                EndMarquee(listBox);
            }

            return;
        }

        if (!state.IsPending)
        {
            return;
        }

        var current = e.GetPosition(listBox);
        state.LastPosition = current;

        if (!state.IsActive)
        {
            var moved = Math.Abs(current.X - state.Origin.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(current.Y - state.Origin.Y) >= SystemParameters.MinimumVerticalDragDistance;

            if (!moved)
            {
                return;
            }

            if (!BeginMarquee(listBox, state))
            {
                return;
            }
        }

        UpdateMarquee(listBox, state);
        e.Handled = true;
    }

    /// <summary>
    /// Пересчёт рамки и выделения. Вызывается и на движении мыши, и по таймеру
    /// автопрокрутки — курсор может стоять за краем списка неподвижно.
    /// </summary>
    private static void UpdateMarquee(ListBox listBox, MarqueeState state)
    {
        if (!state.IsActive)
        {
            return;
        }

        var originY = ResolveOriginY(listBox, state);
        var rectangle = new Rect(
            new Point(state.Origin.X, originY),
            state.LastPosition);

        // Адорнер слоем не обрезается: без обрезки рамка вылезала бы поверх соседних
        // панелей, когда курсор уходит за край списка.
        var visible = Rect.Intersect(rectangle, new Rect(0, 0, listBox.ActualWidth, listBox.ActualHeight));
        state.Adorner?.Update(visible);

        ApplyRange(listBox, state, ResolveRange(listBox, state));
    }

    /// <summary>
    /// Диапазон строк между точкой начала и курсором. Считается по индексам, а не по
    /// пересечению с прямоугольником: у виртуализованного списка контейнеры есть только
    /// для видимых строк, и всё, что уехало за край, иначе теряло бы выделение.
    /// </summary>
    private static (int Min, int Max)? ResolveRange(ListBox listBox, MarqueeState state)
    {
        var count = listBox.Items.Count;
        if (count == 0)
        {
            return null;
        }

        var current = ResolveIndex(listBox, state.LastPosition.Y);
        var min = Math.Min(state.AnchorIndex, current);
        var max = Math.Min(Math.Max(state.AnchorIndex, current), count - 1);

        return min > max || min >= count ? null : (min, max);
    }

    /// <summary>Экранная координата точки начала: она привязана к строке и едет с ней.</summary>
    private static double ResolveOriginY(ListBox listBox, MarqueeState state)
    {
        var count = listBox.Items.Count;
        if (count == 0)
        {
            return state.Origin.Y;
        }

        // Начали ниже последней строки — точка привязана к концу содержимого.
        var index = Math.Min(state.AnchorIndex, count - 1);

        if (GetContainerBounds(listBox, index) is { } bounds)
        {
            var top = state.AnchorIndex >= count ? bounds.Bottom : bounds.Top;
            return top + state.AnchorOffset;
        }

        // Строка уехала за пределы видимой части — рамка должна уходить за тот же край.
        return IsAboveViewport(listBox, index) ? -4 : listBox.ActualHeight + 4;
    }

    private static double ResolveAnchorTop(ListBox listBox, int anchorIndex, double fallbackY)
    {
        var count = listBox.Items.Count;
        if (count == 0)
        {
            return fallbackY;
        }

        var index = Math.Min(anchorIndex, count - 1);
        if (GetContainerBounds(listBox, index) is not { } bounds)
        {
            return fallbackY;
        }

        return anchorIndex >= count ? bounds.Bottom : bounds.Top;
    }

    /// <summary>
    /// Индекс строки под экранной координатой. Ниже последней строки возвращается
    /// Items.Count — это «за концом списка», а не последняя строка.
    /// </summary>
    private static int ResolveIndex(ListBox listBox, double y)
    {
        var count = listBox.Items.Count;
        if (count == 0)
        {
            return 0;
        }

        var first = -1;
        var firstTop = 0d;
        var last = -1;
        var lastBottom = 0d;
        var nearest = -1;
        var nearestDistance = double.MaxValue;

        foreach (var container in GetRealizedContainers(listBox))
        {
            var index = listBox.ItemContainerGenerator.IndexFromContainer(container);
            if (index < 0)
            {
                continue;
            }

            var bounds = GetBounds(container, listBox);
            if (bounds.IsEmpty)
            {
                continue;
            }

            if (y >= bounds.Top && y < bounds.Bottom)
            {
                return index;
            }

            if (first < 0 || index < first)
            {
                first = index;
                firstTop = bounds.Top;
            }

            if (index > last)
            {
                last = index;
                lastBottom = bounds.Bottom;
            }

            var distance = y < bounds.Top ? bounds.Top - y : y - bounds.Bottom;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = index;
            }
        }

        if (first < 0)
        {
            return count;
        }

        if (y < firstTop)
        {
            return first;
        }

        if (y > lastBottom)
        {
            // Ниже видимой части: если это ещё не конец списка, автопрокрутка подтянет
            // остальные строки, поэтому упираемся в последнюю созданную.
            return last >= count - 1 ? count : last;
        }

        // Между строками есть отступ, и координата может не попасть ни в одну из них.
        // Без этого индекс на каждом зазоре срывался в конец списка.
        return nearest;
    }

    private static bool IsAboveViewport(ListBox listBox, int index)
    {
        foreach (var container in GetRealizedContainers(listBox))
        {
            var realized = listBox.ItemContainerGenerator.IndexFromContainer(container);
            if (realized >= 0 && realized < index)
            {
                return false;
            }
        }

        return true;
    }

    private static Rect? GetContainerBounds(ListBox listBox, int index)
    {
        if (listBox.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container ||
            !container.IsVisible)
        {
            return null;
        }

        var bounds = GetBounds(container, listBox);
        return bounds.IsEmpty ? null : bounds;
    }

    /// <summary>
    /// Только созданные контейнеры. Перебирать Items нельзя: в логе их десятки тысяч,
    /// и обход на каждое движение мыши положил бы отзывчивость.
    /// </summary>
    private static IEnumerable<ListBoxItem> GetRealizedContainers(ListBox listBox)
    {
        var host = GetItemsHost(listBox);
        if (host is null)
        {
            yield break;
        }

        foreach (var child in host.Children)
        {
            if (child is ListBoxItem container && container.IsVisible)
            {
                yield return container;
            }
        }
    }

    private static Panel? GetItemsHost(ListBox listBox)
    {
        var state = GetMarquee(listBox);
        if (state.ItemsHost is { IsVisible: true })
        {
            return state.ItemsHost;
        }

        var presenter = FindDescendant<ItemsPresenter>(listBox);
        if (presenter is null || VisualTreeHelper.GetChildrenCount(presenter) == 0)
        {
            return null;
        }

        state.ItemsHost = VisualTreeHelper.GetChild(presenter, 0) as Panel;
        return state.ItemsHost;
    }

    /// <summary>
    /// Выделение правится точечно: пересобирать его целиком на каждое движение мыши
    /// слишком дорого, когда рамкой захвачены тысячи строк.
    /// </summary>
    private static void ApplyRange(ListBox listBox, MarqueeState state, (int Min, int Max)? range)
    {
        if (Nullable.Equals(state.Range, range))
        {
            return;
        }

        var items = listBox.Items;
        var previous = state.Range;

        if (previous is { } old)
        {
            for (var index = old.Min; index <= old.Max && index < items.Count; index++)
            {
                if (range is { } kept && index >= kept.Min && index <= kept.Max)
                {
                    continue;
                }

                var item = items[index];
                if (item is null || state.BaseSelection.Contains(item))
                {
                    continue;
                }

                listBox.SelectedItems.Remove(item);
            }
        }

        if (range is { } fresh)
        {
            for (var index = fresh.Min; index <= fresh.Max && index < items.Count; index++)
            {
                if (previous is { } had && index >= had.Min && index <= had.Max)
                {
                    continue;
                }

                var item = items[index];
                if (item is null || listBox.SelectedItems.Contains(item))
                {
                    continue;
                }

                listBox.SelectedItems.Add(item);
            }
        }

        state.Range = range;
    }

    /// <summary>Курсор за краем списка — подкручиваем, как это делает проводник.</summary>
    private static void OnAutoScrollTick(ListBox listBox, MarqueeState state)
    {
        if (!state.IsActive)
        {
            return;
        }

        var scrollViewer = state.ScrollViewer ??= FindDescendant<ScrollViewer>(listBox);
        if (scrollViewer is null)
        {
            return;
        }

        var overshoot = state.LastPosition.Y < 0
            ? state.LastPosition.Y
            : state.LastPosition.Y - listBox.ActualHeight;

        if (state.LastPosition.Y >= 0 && overshoot <= 0)
        {
            return;
        }

        // Шаг растёт с удалением курсора от края: у самой границы прокрутка мягкая.
        var steps = Math.Clamp((int)(Math.Abs(overshoot) / 24) + 1, 1, 4);
        for (var step = 0; step < steps; step++)
        {
            if (overshoot < 0)
            {
                scrollViewer.LineUp();
            }
            else
            {
                scrollViewer.LineDown();
            }
        }

        // Строки после прокрутки встанут на место не сразу — пересчёт после раскладки.
        listBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => UpdateMarquee(listBox, state));
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            EndMarquee(listBox);
        }
    }

    private static void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        // Начало протяжки само перехватывает мышь у списка в пользу прокрутки —
        // это событие не про конец рамки.
        if (GetMarquee(listBox).IsChangingCapture)
        {
            return;
        }

        EndMarquee(listBox);
    }

    private static void OnListBoxUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            // Слой адорнеров у выгруженного списка свой — уносим рамку вместе с ним.
            DetachMarquee(listBox);
        }
    }

    /// <summary>
    /// Адорнер заводится один раз на список и живёт в слое до его выгрузки: раньше он
    /// создавался на каждую протяжку, и любой пропуск снятия оставлял прямоугольник
    /// висеть навсегда. Теперь экземпляр всегда один, накопить их невозможно.
    /// </summary>
    private static bool BeginMarquee(ListBox listBox, MarqueeState state)
    {
        if (state.Adorner is null)
        {
            var adornerLayer = AdornerLayer.GetAdornerLayer(listBox);
            if (adornerLayer is null)
            {
                state.IsPending = false;
                return false;
            }

            state.Adorner = new MarqueeAdorner(listBox, ResolveAccentColor(listBox));
            state.AdornerLayer = adornerLayer;
            adornerLayer.Add(state.Adorner);
        }

        // Мышь захватывает прокрутка, а не сам список. Штатное протягивание ListBox
        // включается через MouseEnter строк и только при Mouse.Captured == список:
        // иначе оно на каждой новой строке сбрасывало выделение рамки в одну строку.
        // События при этом никуда не деваются — прокрутка лежит внутри списка.
        state.ScrollViewer ??= FindDescendant<ScrollViewer>(listBox);
        state.CaptureTarget = state.ScrollViewer ?? (UIElement)listBox;

        // Смена владельца мыши сразу поднимает у списка LostMouseCapture, а Mouse.Captured
        // в этот момент ещё указывает на старого владельца — сравнением не отличить.
        // Поэтому передача помечается явно, иначе обработчик закрывал едва начатую рамку.
        state.IsActive = true;
        state.IsChangingCapture = true;

        bool captured;
        try
        {
            captured = state.CaptureTarget.CaptureMouse();
        }
        finally
        {
            state.IsChangingCapture = false;
        }

        if (!captured)
        {
            state.Adorner.Update(Rect.Empty);
            state.CaptureTarget = null;
            state.IsActive = false;
            state.IsPending = false;
            return false;
        }

        // Список уже успел выделить строку, на которой нажали. Возвращаем выделение
        // к базовому — дальше его целиком задаёт диапазон рамки.
        listBox.SelectedItems.Clear();
        foreach (var item in state.BaseSelection)
        {
            listBox.SelectedItems.Add(item);
        }

        state.Range = null;

        state.AutoScrollTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(30),
            DispatcherPriority.Input,
            (_, _) => OnAutoScrollTick(listBox, state),
            listBox.Dispatcher);
        state.AutoScrollTimer.Start();

        return true;
    }

    /// <summary>Рамка прячется очисткой прямоугольника, а не удалением из слоя.</summary>
    private static void EndMarquee(ListBox listBox)
    {
        var state = GetMarquee(listBox);

        state.Adorner?.Update(Rect.Empty);
        state.AutoScrollTimer?.Stop();

        // Захват снимаем только свой: во время штатного протягивания ListBox держит
        // мышь сам, и вмешательство сломало бы обычное выделение.
        if (state.IsActive && state.CaptureTarget is { IsMouseCaptured: true } target)
        {
            target.ReleaseMouseCapture();
        }

        state.CaptureTarget = null;

        state.IsPending = false;
        state.IsActive = false;
        state.Range = null;
        state.BaseSelection = [];
    }

    private static void DetachMarquee(ListBox listBox)
    {
        var state = GetMarquee(listBox);

        if (state.Adorner is not null)
        {
            state.AdornerLayer?.Remove(state.Adorner);
            state.Adorner = null;
            state.AdornerLayer = null;
        }

        state.AutoScrollTimer?.Stop();
        state.AutoScrollTimer = null;
        state.ScrollViewer = null;
        state.ItemsHost = null;

        state.IsPending = false;
        state.IsActive = false;
        state.Range = null;
        state.BaseSelection = [];
    }

    private static Rect GetBounds(ListBoxItem container, ListBox listBox)
    {
        try
        {
            var transform = container.TransformToAncestor(listBox);
            return transform.TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            // Контейнер уже отсоединён от дерева.
            return Rect.Empty;
        }
    }

    private static Color ResolveAccentColor(ListBox listBox)
    {
        return listBox.TryFindResource("Brush.AccentPrimary") is SolidColorBrush accentBrush
            ? accentBrush.Color
            : Color.FromRgb(0x4C, 0x8D, 0xFF);
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        return VisualTreeSearch.FindDescendant<T>(root);
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        return VisualTreeSearch.FindAncestor<T>(current);
    }

    private static bool IsInteractiveElement(DependencyObject? current)
    {
        return FindAncestor<ButtonBase>(current) is not null ||
               FindAncestor<TextBoxBase>(current) is not null ||
               FindAncestor<ComboBox>(current) is not null ||
               FindAncestor<ScrollBar>(current) is not null;
    }

    private sealed class MarqueeState
    {
        public Point Origin { get; set; }

        public Point LastPosition { get; set; }

        /// <summary>Строка, на которой начали. Items.Count — начали ниже последней.</summary>
        public int AnchorIndex { get; set; }

        /// <summary>Сдвиг точки начала внутри этой строки.</summary>
        public double AnchorOffset { get; set; }

        public (int Min, int Max)? Range { get; set; }

        public bool IsPending { get; set; }

        public bool IsActive { get; set; }

        public IReadOnlyCollection<object> BaseSelection { get; set; } = [];

        public MarqueeAdorner? Adorner { get; set; }

        public AdornerLayer? AdornerLayer { get; set; }

        public DispatcherTimer? AutoScrollTimer { get; set; }

        public ScrollViewer? ScrollViewer { get; set; }

        /// <summary>Кто держит мышь на время протяжки.</summary>
        public UIElement? CaptureTarget { get; set; }

        /// <summary>Мышь прямо сейчас передаётся списком прокрутке.</summary>
        public bool IsChangingCapture { get; set; }

        public Panel? ItemsHost { get; set; }
    }

    private sealed class MarqueeAdorner : Adorner
    {
        private readonly Brush _fill;
        private readonly Pen _border;
        private Rect _rectangle;

        public MarqueeAdorner(UIElement adornedElement, Color accent)
            : base(adornedElement)
        {
            IsHitTestVisible = false;

            _fill = new SolidColorBrush(Color.FromArgb(48, accent.R, accent.G, accent.B));
            _fill.Freeze();

            var borderBrush = new SolidColorBrush(Color.FromArgb(180, accent.R, accent.G, accent.B));
            borderBrush.Freeze();

            _border = new Pen(borderBrush, 1);
            _border.Freeze();
        }

        public void Update(Rect rectangle)
        {
            if (_rectangle == rectangle)
            {
                return;
            }

            _rectangle = rectangle;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (_rectangle.IsEmpty || _rectangle.Width < 1 || _rectangle.Height < 1)
            {
                return;
            }

            drawingContext.DrawRectangle(_fill, _border, _rectangle);
        }
    }
}
