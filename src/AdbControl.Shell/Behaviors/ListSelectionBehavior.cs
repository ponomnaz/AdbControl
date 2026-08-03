using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

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

        // Клик по строке обрабатывает сам ListBox — стандартной логики достаточно.
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var state = GetMarquee(listBox);
        state.Origin = e.GetPosition(listBox);
        state.IsPending = true;
        state.IsActive = false;
        state.BaseSelection = listBox.SelectedItems.Cast<object>().ToArray();

        // Пустое место: как в проводнике, выделение снимается сразу по нажатию.
        if (Keyboard.Modifiers == ModifierKeys.None && listBox.SelectedItems.Count > 0)
        {
            listBox.SelectedItems.Clear();
            state.BaseSelection = [];
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
        if (!state.IsPending || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(listBox);

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

        var rectangle = new Rect(state.Origin, current);
        state.Adorner?.Update(rectangle);
        ApplyMarqueeSelection(listBox, state, rectangle);
        e.Handled = true;
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
        if (sender is ListBox listBox)
        {
            EndMarquee(listBox);
        }
    }

    private static bool BeginMarquee(ListBox listBox, MarqueeState state)
    {
        var adornerLayer = AdornerLayer.GetAdornerLayer(listBox);
        if (adornerLayer is null || !listBox.CaptureMouse())
        {
            state.IsPending = false;
            return false;
        }

        state.Adorner = new MarqueeAdorner(listBox, ResolveAccentColor(listBox));
        adornerLayer.Add(state.Adorner);
        state.IsActive = true;
        return true;
    }

    private static void EndMarquee(ListBox listBox)
    {
        var state = GetMarquee(listBox);

        if (state.Adorner is not null)
        {
            AdornerLayer.GetAdornerLayer(listBox)?.Remove(state.Adorner);
            state.Adorner = null;
        }

        if (state.IsActive && listBox.IsMouseCaptured)
        {
            listBox.ReleaseMouseCapture();
        }

        state.IsPending = false;
        state.IsActive = false;
        state.BaseSelection = [];
    }

    /// <summary>
    /// Виртуализованные списки создают контейнеры только для видимых строк, поэтому
    /// рамка захватывает то, что видно на экране, — как и в проводнике.
    /// </summary>
    private static void ApplyMarqueeSelection(ListBox listBox, MarqueeState state, Rect rectangle)
    {
        var wanted = new List<object>(state.BaseSelection);

        foreach (var item in listBox.Items)
        {
            if (listBox.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container ||
                !container.IsVisible)
            {
                continue;
            }

            if (!rectangle.IntersectsWith(GetBounds(container, listBox)))
            {
                continue;
            }

            if (!wanted.Contains(item))
            {
                wanted.Add(item);
            }
        }

        if (IsSameSelection(listBox.SelectedItems, wanted))
        {
            return;
        }

        listBox.SelectedItems.Clear();
        foreach (var item in wanted)
        {
            listBox.SelectedItems.Add(item);
        }
    }

    private static bool IsSameSelection(IList current, List<object> wanted)
    {
        if (current.Count != wanted.Count)
        {
            return false;
        }

        foreach (var item in wanted)
        {
            if (!current.Contains(item))
            {
                return false;
            }
        }

        return true;
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

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current),
                FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent ?? LogicalTreeHelper.GetParent(frameworkContentElement),
                ContentElement contentElement => ContentOperations.GetParent(contentElement) ?? LogicalTreeHelper.GetParent(contentElement),
                _ => LogicalTreeHelper.GetParent(current)
            };
        }

        return null;
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

        public bool IsPending { get; set; }

        public bool IsActive { get; set; }

        public IReadOnlyList<object> BaseSelection { get; set; } = [];

        public MarqueeAdorner? Adorner { get; set; }
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
